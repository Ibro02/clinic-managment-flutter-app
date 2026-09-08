using ClinicNow.Model.Common;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Documents;
using ClinicNow.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicNow.Services.Database;

/// <summary>
/// Grows the seed to a demoable size and, more importantly, fixes the
/// stale-demo-data bug: every <c>HasData</c> row in
/// <c>ClinicNow.Services/Database/Configurations</c> is pinned to a fixed
/// August 2026 date because <c>HasData</c> values must be constant at
/// model-build time - so on the day this repository is actually reviewed,
/// every seeded appointment/block/interaction has already happened and the
/// dashboard, weekly trend, and mobile "Predstojeći" list all show empty
/// (rulebook §3.1 explicitly threatens "insufficient data" submissions with
/// not being evaluated in detail).
///
/// This class runs once at API startup, after migrations, and anchors every
/// time-sensitive row it adds to <see cref="ClinicTimeZone.NowLocal"/> at
/// that moment instead of a fixed literal - so the demo stays current no
/// matter when <c>docker-compose up</c> is actually run. It only ever adds
/// rows (fixed IDs, never colliding with the ≤8 <c>HasData</c> IDs already in
/// use) and is idempotent: a second run (container restart) is a single
/// indexed existence check away from a no-op, since the whole batch commits
/// in one <see cref="DbContext.SaveChangesAsync"/> call.
/// </summary>
public class DemoDataSeeder
{
    /// <summary>Below this, an ID belongs to a migration's <c>HasData</c> row - never reused here.</summary>
    private const int FirstSeederId = 100;

    /// <summary>
    /// Floor for the Appointments/AppointmentAuditLogs range - the actual
    /// starting id is computed at seed time as the higher of this and
    /// whatever real usage has already pushed those two tables past (see
    /// <see cref="SeedAsync"/>). Unlike every other table this seeder writes
    /// to, real usage creates new Appointment rows constantly (every booking),
    /// so a literal like the other tables' <see cref="FirstSeederId"/> would
    /// eventually collide with a real row - which is exactly what happened
    /// here once the app had been exercised for a day or two before this
    /// seeder's first successful run.
    /// </summary>
    private const int MinAppointmentId = 1000;

    private readonly ClinicNowContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(ClinicNowContext context, IPasswordHasher passwordHasher, ILogger<DemoDataSeeder> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        // Single indexed existence check makes the whole batch idempotent:
        // every row below is inserted in one SaveChangesAsync, so User 100
        // existing implies everything else does too.
        if (await _context.Users.AnyAsync(u => u.Id == FirstSeederId, cancellationToken))
        {
            _logger.LogInformation("Demo data already seeded - skipping.");
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var nowLocal = ClinicTimeZone.NowLocal;
        var today = DateOnly.FromDateTime(nowLocal);

        var users = BuildUsers();
        var userRoles = BuildUserRoles();
        var doctors = BuildDoctors();
        var doctorSpecializations = BuildDoctorSpecializations();
        var workingHours = BuildWorkingHours();
        var patients = BuildPatients();
        var medicalRecords = BuildMedicalRecords(nowUtc);

        // Real usage books appointments constantly (unlike accounts/patients/
        // doctors, which only staff create), so IDs can drift arbitrarily far
        // past MinAppointmentId before this seeder ever gets a chance to run
        // once. Starting from a fixed literal here is exactly what caused a
        // real "duplicate key" / IDENTITY_INSERT collision against genuine
        // booking data - this computes a floor that is always above every
        // Appointment/AppointmentAuditLog id already in the database.
        // The +100000 margin (not just +1) is deliberate: SQL Server advances
        // an IDENTITY column's internal counter even for an insert that was
        // ultimately rejected/rolled back (e.g. a prior run of this exact
        // seeder that failed partway through before this class toggled
        // IDENTITY_INSERT correctly), so MAX(Id) alone can already read lower
        // than what the next real auto-generated value will be.
        var maxExistingAppointmentId = await _context.Appointments.MaxAsync(a => (int?)a.Id, cancellationToken) ?? 0;
        var maxExistingAuditLogId = await _context.AppointmentAuditLogs.MaxAsync(l => (int?)l.Id, cancellationToken) ?? 0;
        var firstAppointmentId = Math.Max(MinAppointmentId, Math.Max(maxExistingAppointmentId, maxExistingAuditLogId) + 100000);

        var (appointments, auditLogs) = BuildAppointments(nowUtc, today, firstAppointmentId, out var resultingReferralAppointment);
        appointments.Add(resultingReferralAppointment.Appointment);
        auditLogs.Add(resultingReferralAppointment.AuditLog);

        var referrals = BuildReferrals(appointments, resultingReferralAppointment.Appointment.Id, nowUtc);
        var labFindings = BuildLabFindings(appointments, nowUtc);
        var medicalRecordEntries = BuildMedicalRecordEntries(nowUtc);
        var recommenderInteractions = BuildRecommenderInteractions(nowUtc);
        var notifications = BuildNotifications(nowUtc);
        var newsItem = BuildNewsItem(nowUtc);
        var scheduleBlock = BuildScheduleBlock(nowLocal);

        // Unlike a migration's HasData (which the EF tooling wraps in
        // generated `SET IDENTITY_INSERT ON/OFF` SQL automatically), a plain
        // runtime SaveChangesAsync does NOT toggle that for an ordinary
        // insert - every one of these entities has an explicitly-assigned,
        // fixed Id going into an identity column, so SQL Server rejects the
        // whole batch with "Cannot insert explicit value for identity column"
        // unless this class does the toggling itself. One transaction so a
        // failure partway through never leaves the idempotency check (User
        // 100 exists) satisfied by a half-seeded database.
        //
        // Run through the context's own execution strategy (CLAUDE.md §8.2's
        // "retry a deadlock/serialization conflict instead of a 500" applies
        // just as much to a startup seed as to a booking): the API's SQL
        // Server connection is configured with EnableRetryOnFailure, and that
        // retrying strategy refuses a bare Database.BeginTransactionAsync
        // outside of it, since it needs to retry the *whole* unit - including
        // the BEGIN - on a transient failure.
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // Discards anything a previous, retried attempt left tracked as
            // Added but never committed - the entity lists themselves are
            // plain in-memory objects and are safe to re-add on retry.
            _context.ChangeTracker.Clear();

            var transaction = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await using var disposableTransaction = transaction;

            await InsertWithExplicitIdsAsync(_context.Users, users, "Users", cancellationToken);

            // Composite key (UserId, RoleId), not an identity column - no
            // IDENTITY_INSERT involved. Without this, a seeded account logs
            // in fine (authentication only needs a password hash) but its
            // JWT carries no role claim at all, so every role-gated action
            // - including every one of this doctor/patient's own screens -
            // fails in whatever way an absent role happens to be treated.
            _context.UserRoles.AddRange(userRoles);
            await _context.SaveChangesAsync(cancellationToken);

            await InsertWithExplicitIdsAsync(_context.Doctors, doctors, "Doctors", cancellationToken);

            // Composite key (DoctorId, SpecializationId), not an identity column -
            // no IDENTITY_INSERT involved.
            _context.DoctorSpecializations.AddRange(doctorSpecializations);
            await _context.SaveChangesAsync(cancellationToken);

            await InsertWithExplicitIdsAsync(_context.WorkingHoursEntries, workingHours, "WorkingHoursEntries", cancellationToken);
            await InsertWithExplicitIdsAsync(_context.Patients, patients, "Patients", cancellationToken);
            await InsertWithExplicitIdsAsync(_context.MedicalRecords, medicalRecords, "MedicalRecords", cancellationToken);
            await InsertWithExplicitIdsAsync(_context.Appointments, appointments, "Appointments", cancellationToken);
            await InsertWithExplicitIdsAsync(_context.AppointmentAuditLogs, auditLogs, "AppointmentAuditLogs", cancellationToken);
            await InsertWithExplicitIdsAsync(_context.Referrals, referrals, "Referrals", cancellationToken);
            await InsertWithExplicitIdsAsync(_context.LabFindings, labFindings, "LabFindings", cancellationToken);
            await InsertWithExplicitIdsAsync(_context.MedicalRecordEntries, medicalRecordEntries, "MedicalRecordEntries", cancellationToken);
            await InsertWithExplicitIdsAsync(_context.RecommenderInteractions, recommenderInteractions, "RecommenderInteractions", cancellationToken);
            await InsertWithExplicitIdsAsync(_context.Notifications, notifications, "Notifications", cancellationToken);
            await InsertWithExplicitIdsAsync(_context.NewsItems, [newsItem], "NewsItems", cancellationToken);
            await InsertWithExplicitIdsAsync(_context.ScheduleBlocks, [scheduleBlock], "ScheduleBlocks", cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        });

        _logger.LogInformation(
            "Seeded demo data: {Patients} patients, {Doctors} doctors, {Appointments} appointments, " +
            "{LabFindings} lab findings, {Referrals} referrals, {Entries} medical record entries, " +
            "{Interactions} recommender interactions.",
            patients.Count, doctors.Count, appointments.Count, labFindings.Count, referrals.Count,
            medicalRecordEntries.Count, recommenderInteractions.Count);
    }

    /// <summary>
    /// Inserts a batch of rows carrying explicit, pre-assigned primary keys
    /// into a table whose Id column is a SQL Server IDENTITY - toggling
    /// IDENTITY_INSERT around the insert, which a runtime SaveChangesAsync
    /// never does on its own (unlike EF migrations' HasData). SQL Server only
    /// allows one table's IDENTITY_INSERT to be ON per connection at a time,
    /// so this must run one table fully to completion (insert, then OFF)
    /// before the next table's rows are added - never interleaved.
    ///
    /// The IDENTITY_INSERT toggle is relational-only SQL and unsupported (and
    /// unnecessary - it has no identity-column restriction at all) on the
    /// EF Core InMemory provider these tests run against, so it is skipped
    /// there via <see cref="RelationalDatabaseFacadeExtensions.IsRelational"/>.
    /// </summary>
    private async Task InsertWithExplicitIdsAsync<TEntity>(
        Microsoft.EntityFrameworkCore.DbSet<TEntity> set, IReadOnlyCollection<TEntity> entities, string tableName, CancellationToken cancellationToken)
        where TEntity : class
    {
        if (entities.Count == 0)
        {
            return;
        }

        var isRelational = _context.Database.IsRelational();

        // tableName is always one of this method's own hardcoded call-site
        // literals (see SeedAsync) - never external input - and a table
        // identifier can't be a SQL parameter in the first place, so
        // ExecuteSqlAsync's parameterization wouldn't apply here anyway.
#pragma warning disable EF1002
        if (isRelational)
        {
            await _context.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT [{tableName}] ON", cancellationToken);
        }

        set.AddRange(entities);
        await _context.SaveChangesAsync(cancellationToken);

        if (isRelational)
        {
            await _context.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT [{tableName}] OFF", cancellationToken);
        }
#pragma warning restore EF1002
    }

    // --- identity: 3 new doctors + 2 new patients-with-login ------------------

    private List<User> BuildUsers()
    {
        // Same password ("test") as every other demo account, for a
        // consistent, documentable login story - these two patient accounts
        // (110/111) exist to give the recommender real per-patient history
        // (see BuildRecommenderInteractions), not as separately-documented
        // review credentials.
        var passwordHash = _passwordHasher.Hash("test");

        return
        [
            new User { Id = 100, Email = "doctor3@clinicnow.test", PasswordHash = passwordHash, FirstName = "Damir", LastName = "Pedijatrić", PhoneNumber = "+38761000100", IsActive = true, CreatedAtUtc = DateTime.UtcNow },
            new User { Id = 101, Email = "doctor4@clinicnow.test", PasswordHash = passwordHash, FirstName = "Lejla", LastName = "Ginekologić", PhoneNumber = "+38761000101", IsActive = true, CreatedAtUtc = DateTime.UtcNow },
            new User { Id = 102, Email = "doctor5@clinicnow.test", PasswordHash = passwordHash, FirstName = "Tarik", LastName = "Kardiologić", PhoneNumber = "+38761000102", IsActive = true, CreatedAtUtc = DateTime.UtcNow },
            new User { Id = 110, Email = "patient2@clinicnow.test", PasswordHash = passwordHash, FirstName = "Adna", LastName = "Selimović", PhoneNumber = "+38762000110", IsActive = true, CreatedAtUtc = DateTime.UtcNow },
            new User { Id = 111, Email = "patient3@clinicnow.test", PasswordHash = passwordHash, FirstName = "Kenan", LastName = "Hodžić", PhoneNumber = "+38762000111", IsActive = true, CreatedAtUtc = DateTime.UtcNow }
        ];
    }

    /// <summary>
    /// Without this, an account authenticates fine (login only needs a
    /// password hash) but its JWT carries no role claim at all - see
    /// <see cref="Model.Security.Roles"/>/<c>RoleConfiguration</c> for the
    /// fixed role ids (3 = Doctor, 4 = Patient) every other seeded account
    /// already gets via <c>UserRoleConfiguration.HasData</c>.
    /// </summary>
    private static List<UserRole> BuildUserRoles() =>
    [
        new UserRole { UserId = 100, RoleId = 3 },
        new UserRole { UserId = 101, RoleId = 3 },
        new UserRole { UserId = 102, RoleId = 3 },
        new UserRole { UserId = 110, RoleId = 4 },
        new UserRole { UserId = 111, RoleId = 4 }
    ];

    private static List<Doctor> BuildDoctors() =>
    [
        new Doctor { Id = 100, UserId = 100, LocationId = 3, LicenseNumber = "LKB-20014", Bio = "Pedijatar, radi i opće preglede.", CreatedAtUtc = DateTime.UtcNow },
        new Doctor { Id = 101, UserId = 101, LocationId = 1, LicenseNumber = "LKB-20055", Bio = "Ginekolog i dermatolog.", CreatedAtUtc = DateTime.UtcNow },
        new Doctor { Id = 102, UserId = 102, LocationId = 2, LicenseNumber = "LKB-20098", Bio = "Specijalista opće medicine i kardiologije.", CreatedAtUtc = DateTime.UtcNow }
    ];

    private static List<DoctorSpecialization> BuildDoctorSpecializations() =>
    [
        new DoctorSpecialization { DoctorId = 100, SpecializationId = 3 }, // Pedijatrija
        new DoctorSpecialization { DoctorId = 100, SpecializationId = 1 }, // Opća medicina (bookable)
        new DoctorSpecialization { DoctorId = 101, SpecializationId = 5 }, // Ginekologija
        new DoctorSpecialization { DoctorId = 101, SpecializationId = 2 }, // Dermatologija (bookable)
        new DoctorSpecialization { DoctorId = 102, SpecializationId = 1 }, // Opća medicina (bookable)
        new DoctorSpecialization { DoctorId = 102, SpecializationId = 4 }  // Kardiologija (bookable)
    ];

    private static List<WorkingHours> BuildWorkingHours() =>
    [
        new WorkingHours { Id = 100, DoctorId = 100, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(15, 0) },
        new WorkingHours { Id = 101, DoctorId = 100, DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(15, 0) },
        new WorkingHours { Id = 102, DoctorId = 100, DayOfWeek = DayOfWeek.Wednesday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(15, 0) },
        new WorkingHours { Id = 103, DoctorId = 101, DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(16, 0) },
        new WorkingHours { Id = 104, DoctorId = 101, DayOfWeek = DayOfWeek.Thursday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(16, 0) },
        new WorkingHours { Id = 105, DoctorId = 102, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(14, 0) },
        new WorkingHours { Id = 106, DoctorId = 102, DayOfWeek = DayOfWeek.Wednesday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(14, 0) },
        new WorkingHours { Id = 107, DoctorId = 102, DayOfWeek = DayOfWeek.Friday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(14, 0) }
    ];

    // --- patients: 2 with a login (100, 101), 5 walk-ins (102-106) ------------

    private static List<Patient> BuildPatients()
    {
        var createdAtUtc = DateTime.UtcNow;

        return
        [
            new Patient { Id = 100, UserId = 110, FirstName = "Adna", LastName = "Selimović", PersonalIdNumber = "1204995185552", DateOfBirth = new DateOnly(1995, 4, 12), Gender = Gender.Female, PhoneNumber = "+38762000110", Email = "patient2@clinicnow.test", Address = "Titova 5, Sarajevo", CreatedAtUtc = createdAtUtc },
            new Patient { Id = 101, UserId = 111, FirstName = "Kenan", LastName = "Hodžić", PersonalIdNumber = "0311988180019", DateOfBirth = new DateOnly(1988, 11, 3), Gender = Gender.Male, PhoneNumber = "+38762000111", Email = "patient3@clinicnow.test", Address = "Maršala Tita 22, Tuzla", CreatedAtUtc = createdAtUtc },
            new Patient { Id = 102, UserId = null, FirstName = "Amina", LastName = "Bećirović", PersonalIdNumber = "2207992185563", DateOfBirth = new DateOnly(1992, 7, 22), Gender = Gender.Female, PhoneNumber = "+38763000102", Email = "amina.becirovic@example.test", Address = "Kranjčevićeva 8, Mostar", CreatedAtUtc = createdAtUtc },
            new Patient { Id = 103, UserId = null, FirstName = "Faruk", LastName = "Mušić", PersonalIdNumber = "1509978190024", DateOfBirth = new DateOnly(1978, 9, 15), Gender = Gender.Male, PhoneNumber = "+38763000103", Email = "faruk.music@example.test", Address = "Alipašina 3, Sarajevo", CreatedAtUtc = createdAtUtc },
            new Patient { Id = 104, UserId = null, FirstName = "Lamija", LastName = "Turković", PersonalIdNumber = "0106001185571", DateOfBirth = new DateOnly(2001, 6, 1), Gender = Gender.Female, PhoneNumber = "+38763000104", Email = "lamija.turkovic@example.test", Address = "Muse Ćazima Ćatića 14, Tuzla", CreatedAtUtc = createdAtUtc },
            new Patient { Id = 105, UserId = null, FirstName = "Haris", LastName = "Đulić", PersonalIdNumber = "2812965190035", DateOfBirth = new DateOnly(1965, 12, 28), Gender = Gender.Male, PhoneNumber = "+38763000105", Email = "haris.djulic@example.test", Address = "Bulevar Meše Selimovića 2, Mostar", CreatedAtUtc = createdAtUtc },
            new Patient { Id = 106, UserId = null, FirstName = "Ines", LastName = "Vejzović", PersonalIdNumber = "1704999185589", DateOfBirth = new DateOnly(1999, 4, 17), Gender = Gender.Female, PhoneNumber = "+38763000106", Email = "ines.vejzovic@example.test", Address = "Envera Šehovića 9, Sarajevo", CreatedAtUtc = createdAtUtc }
        ];
    }

    private static List<MedicalRecord> BuildMedicalRecords(DateTime nowUtc)
    {
        var records = new List<MedicalRecord>();
        for (var patientId = 100; patientId <= 106; patientId++)
        {
            records.Add(new MedicalRecord { Id = patientId, PatientId = patientId, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc });
        }

        return records;
    }

    // --- appointments: the actual fix for the stale-data bug ------------------

    private readonly record struct DoctorBookingProfile(int DoctorId, int LocationId, int CreatedByUserId, int[] ServiceIds, (DayOfWeek Day, TimeOnly Start)[] Windows);

    private static readonly DoctorBookingProfile[] DoctorProfiles =
    [
        new(1, 1, 2, [1, 2, 3, 4], [(DayOfWeek.Monday, new TimeOnly(8, 0)), (DayOfWeek.Tuesday, new TimeOnly(8, 0)), (DayOfWeek.Wednesday, new TimeOnly(8, 0)), (DayOfWeek.Thursday, new TimeOnly(8, 0)), (DayOfWeek.Friday, new TimeOnly(8, 0))]),
        new(2, 2, 2, [1, 3, 4], [(DayOfWeek.Monday, new TimeOnly(9, 0)), (DayOfWeek.Wednesday, new TimeOnly(9, 0)), (DayOfWeek.Friday, new TimeOnly(9, 0))]),
        new(100, 3, 2, [1, 3, 4], [(DayOfWeek.Monday, new TimeOnly(8, 0)), (DayOfWeek.Tuesday, new TimeOnly(8, 0)), (DayOfWeek.Wednesday, new TimeOnly(8, 0))]),
        new(101, 1, 2, [2], [(DayOfWeek.Tuesday, new TimeOnly(10, 0)), (DayOfWeek.Thursday, new TimeOnly(10, 0))]),
        new(102, 2, 2, [1, 3, 4], [(DayOfWeek.Monday, new TimeOnly(8, 0)), (DayOfWeek.Wednesday, new TimeOnly(8, 0)), (DayOfWeek.Friday, new TimeOnly(8, 0))])
    ];

    private static readonly Dictionary<int, int> ServiceDurationMinutes = new()
    {
        [1] = 30, // Opći pregled
        [2] = 30, // Dermatološki pregled
        [3] = 20, // Ultrazvuk
        [4] = 15, // Laboratorijske analize
        [5] = 45  // Kardiološki pregled (referral-only)
    };

    private static readonly int[] PatientPool = [1, 2, 100, 101, 102, 103, 104, 105, 106];

    /// <summary>
    /// Books roughly every third eligible working day per doctor across a
    /// today-60..today+21 window (deterministic, no <see cref="Random"/>, so a
    /// re-seed of a fresh database always produces the same shape of demo
    /// data).
    ///
    /// Past and future are capped by <em>separate</em> targets
    /// (<paramref name="pastTarget"/>/<paramref name="futureTarget"/>) rather
    /// than one combined total. A single shared target is what caused the
    /// original bug: the loop walked chronologically from today-60 and, with
    /// 5 doctors averaging ~5.3 booked slots/week, a combined target of 45
    /// appointments was already exhausted by the past 60-day half before the
    /// loop ever reached "today" - so on every real review date the future
    /// half (including every <see cref="AppointmentStatus.Pending"/> row) was
    /// empty. Verified by simulating the old single-target loop across six
    /// different "today" dates: 45/45 past, 0 future, every time.
    /// </summary>
    private (List<Appointment> Appointments, List<AppointmentAuditLog> AuditLogs) BuildAppointments(
        DateTime nowUtc, DateOnly today, int firstAppointmentId, out (Appointment Appointment, AppointmentAuditLog AuditLog) referralResultAppointment)
    {
        const int pastTarget = 30;
        const int futureTarget = 18;
        var startDate = today.AddDays(-60);
        var endDate = today.AddDays(21);

        var appointments = new List<Appointment>();
        var auditLogs = new List<AppointmentAuditLog>();
        var perDoctorWorkingDayCount = new Dictionary<int, int>();
        var pastCounter = 0;
        var futureCounter = 0;
        var pastBooked = 0;
        var futureBooked = 0;
        var nextId = firstAppointmentId;

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            if (pastBooked >= pastTarget && futureBooked >= futureTarget)
            {
                break;
            }

            var dayOffset = date.DayNumber - startDate.DayNumber;

            for (var doctorIndex = 0; doctorIndex < DoctorProfiles.Length; doctorIndex++)
            {
                var profile = DoctorProfiles[doctorIndex];
                var window = Array.Find(profile.Windows, w => w.Day == date.DayOfWeek);
                if (window == default)
                {
                    continue;
                }

                perDoctorWorkingDayCount.TryGetValue(profile.DoctorId, out var count);
                count++;
                perDoctorWorkingDayCount[profile.DoctorId] = count;

                // Throttle: only every 3rd eligible working day actually gets
                // booked, which is what keeps each half near its own target
                // instead of packing every single working day for 82 days.
                if (count % 3 != 1)
                {
                    continue;
                }

                var startUtc = ClinicTimeZone.ToUtc(date, window.Start);
                var isPast = startUtc < nowUtc;

                // Once a half's own target is full, stop adding to it - but
                // keep iterating dates/doctors, since the other half may still
                // need rows (e.g. the past 60 days fill up long before the
                // loop reaches "today").
                if (isPast && pastBooked >= pastTarget)
                {
                    continue;
                }

                if (!isPast && futureBooked >= futureTarget)
                {
                    continue;
                }

                var serviceId = profile.ServiceIds[(count / 3) % profile.ServiceIds.Length];
                var patientId = PatientPool[(dayOffset + doctorIndex) % PatientPool.Length];
                var endUtc = startUtc.AddMinutes(ServiceDurationMinutes[serviceId]);

                AppointmentStatus status;
                string? cancellationReason = null;

                if (isPast)
                {
                    pastCounter++;
                    // Every 7th past appointment is cancelled instead of
                    // completed, so the demo shows a real mix of terminal
                    // statuses rather than "everything in the past succeeded".
                    if (pastCounter % 7 == 0)
                    {
                        status = AppointmentStatus.Cancelled;
                        cancellationReason = "Pacijent se razbolio.";
                    }
                    else
                    {
                        status = AppointmentStatus.Completed;
                    }

                    pastBooked++;
                }
                else if (date == today)
                {
                    // Always Confirmed, never Pending - a same-day slot has
                    // already passed the point where staff would still be
                    // waiting to confirm it.
                    status = AppointmentStatus.Confirmed;
                    futureBooked++;
                }
                else
                {
                    futureCounter++;
                    status = futureCounter % 2 == 0 ? AppointmentStatus.Pending : AppointmentStatus.Confirmed;
                    futureBooked++;
                }

                var appointmentId = nextId++;
                appointments.Add(new Appointment
                {
                    Id = appointmentId,
                    PatientId = patientId,
                    DoctorId = profile.DoctorId,
                    MedicalServiceId = serviceId,
                    LocationId = profile.LocationId,
                    StartUtc = startUtc,
                    EndUtc = endUtc,
                    Status = status,
                    CancellationReason = cancellationReason,
                    CreatedByUserId = profile.CreatedByUserId,
                    CreatedAtUtc = startUtc.AddDays(-2)
                });

                auditLogs.Add(new AppointmentAuditLog
                {
                    Id = appointmentId,
                    AppointmentId = appointmentId,
                    Status = status,
                    ActingUserId = profile.CreatedByUserId,
                    OccurredAtUtc = startUtc.AddDays(-2),
                    Description = DescribeStatus(status)
                });
            }
        }

        // The throttle above is deterministic but, depending on which weekday
        // "today" happens to be, can leave the dashboard's 24h "Sljedeći
        // termini" window empty (e.g. a boot on Saturday/Sunday, when no
        // DoctorProfiles entry works at all). Top up to at least two
        // appointments inside [nowUtc, nowUtc+24h) explicitly, rather than
        // just guaranteeing one row somewhere on "today" as before - a single
        // guaranteed slot at a fixed local time can itself already be in the
        // past by the time a reviewer opens the app later in the day.
        foreach (var (guaranteedAppointment, guaranteedAuditLog) in BuildGuaranteedNearTermAppointments(nowUtc, today, appointments, ref nextId))
        {
            appointments.Add(guaranteedAppointment);
            auditLogs.Add(guaranteedAuditLog);
        }

        // The referral demo's "continue to booking" appointment (review item
        // C5): Doctor 2 holds Kardiologija, MedicalService 5 requires a
        // referral, and this is deliberately non-terminal (Confirmed) so the
        // referral it belongs to stays visible as "in progress" instead of
        // auto-archiving.
        var referralDate = NextOccurrenceOfWeekday(today.AddDays(7), DayOfWeek.Monday);
        var referralStartUtc = ClinicTimeZone.ToUtc(referralDate, new TimeOnly(9, 0));
        var referralAppointmentId = nextId;
        var referralAppointment = new Appointment
        {
            Id = referralAppointmentId,
            PatientId = 1,
            DoctorId = 2,
            MedicalServiceId = 5,
            LocationId = 2,
            StartUtc = referralStartUtc,
            EndUtc = referralStartUtc.AddMinutes(ServiceDurationMinutes[5]),
            Status = AppointmentStatus.Confirmed,
            CreatedByUserId = 2,
            CreatedAtUtc = referralStartUtc.AddDays(-2)
        };
        var referralAuditLog = new AppointmentAuditLog
        {
            Id = referralAppointmentId,
            AppointmentId = referralAppointmentId,
            Status = AppointmentStatus.Confirmed,
            ActingUserId = 2,
            OccurredAtUtc = referralStartUtc.AddDays(-2),
            Description = DescribeStatus(AppointmentStatus.Confirmed)
        };
        referralResultAppointment = (referralAppointment, referralAuditLog);

        return (appointments, auditLogs);
    }

    /// <summary>
    /// Tops up <paramref name="alreadyBuilt"/> to at least two appointments
    /// starting inside the next 24 hours - the exact window
    /// <c>dashboard_screen.dart</c>'s "Sljedeći termini" table queries. Looks
    /// for a real <see cref="DoctorProfiles"/> window on "today" or "tomorrow"
    /// first, so a guaranteed slot still matches a doctor's actual working
    /// hours whenever the calendar allows it; only falls back to a time
    /// relative to <paramref name="nowUtc"/> with no matching
    /// <see cref="WorkingHours"/> row when neither day has any doctor working
    /// at all (every <see cref="DoctorProfiles"/> entry is Monday-Friday, so
    /// this only triggers when "today" and "tomorrow" are both weekend days) -
    /// the same working-hours compromise the single-appointment version of
    /// this fallback already made.
    /// </summary>
    private static List<(Appointment Appointment, AppointmentAuditLog AuditLog)> BuildGuaranteedNearTermAppointments(
        DateTime nowUtc, DateOnly today, List<Appointment> alreadyBuilt, ref int nextId)
    {
        const int requiredWithin24h = 2;
        var windowEndUtc = nowUtc.AddHours(24);

        var alreadyWithinWindow = alreadyBuilt.Count(a => a.StartUtc >= nowUtc && a.StartUtc < windowEndUtc);
        var missing = requiredWithin24h - alreadyWithinWindow;
        var results = new List<(Appointment, AppointmentAuditLog)>();
        if (missing <= 0)
        {
            return results;
        }

        var candidates = new List<(DoctorBookingProfile Profile, DateTime StartUtc)>();
        foreach (var dayOffset in new[] { 0, 1 })
        {
            var date = today.AddDays(dayOffset);
            foreach (var profile in DoctorProfiles)
            {
                var window = Array.Find(profile.Windows, w => w.Day == date.DayOfWeek);
                if (window == default)
                {
                    continue;
                }

                var startUtc = ClinicTimeZone.ToUtc(date, window.Start);
                if (startUtc >= nowUtc && startUtc < windowEndUtc)
                {
                    candidates.Add((profile, startUtc));
                }
            }
        }

        var syntheticOffsets = new[] { TimeSpan.FromHours(3), TimeSpan.FromHours(20) };

        for (var i = 0; i < missing; i++)
        {
            DoctorBookingProfile profile;
            DateTime startUtc;
            if (i < candidates.Count)
            {
                (profile, startUtc) = candidates[i];
            }
            else
            {
                profile = DoctorProfiles[i % DoctorProfiles.Length];
                startUtc = nowUtc.Add(syntheticOffsets[i % syntheticOffsets.Length]);
            }

            var serviceId = profile.ServiceIds[0];
            var appointmentId = nextId++;
            var appointment = new Appointment
            {
                Id = appointmentId,
                PatientId = PatientPool[i % PatientPool.Length],
                DoctorId = profile.DoctorId,
                MedicalServiceId = serviceId,
                LocationId = profile.LocationId,
                StartUtc = startUtc,
                EndUtc = startUtc.AddMinutes(ServiceDurationMinutes[serviceId]),
                Status = AppointmentStatus.Confirmed,
                CreatedByUserId = profile.CreatedByUserId,
                CreatedAtUtc = nowUtc.AddDays(-2)
            };
            var auditLog = new AppointmentAuditLog
            {
                Id = appointmentId,
                AppointmentId = appointmentId,
                Status = AppointmentStatus.Confirmed,
                ActingUserId = profile.CreatedByUserId,
                OccurredAtUtc = nowUtc.AddDays(-2),
                Description = DescribeStatus(AppointmentStatus.Confirmed)
            };
            results.Add((appointment, auditLog));
        }

        return results;
    }

    private static DateOnly NextOccurrenceOfWeekday(DateOnly from, DayOfWeek day)
    {
        var date = from;
        while (date.DayOfWeek != day)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    private static string DescribeStatus(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Completed => "Termin završen.",
        AppointmentStatus.Cancelled => "Pacijent se razbolio.",
        AppointmentStatus.Confirmed => "Termin potvrđen.",
        _ => "Termin zakazan."
    };

    // --- referrals: 3 rows, 1 continued into an actual booking ----------------

    private static List<Referral> BuildReferrals(List<Appointment> appointments, int resultingAppointmentId, DateTime nowUtc)
    {
        // Completed/Confirmed appointments make sense as "the exam this
        // referral was issued during" - a Pending/Cancelled one wouldn't
        // have actually happened yet.
        var sourceCandidates = appointments
            .Where(a => a.Status is AppointmentStatus.Completed or AppointmentStatus.Confirmed)
            .OrderBy(a => a.Id)
            .Take(3)
            .ToList();

        var referrals = new List<Referral>();
        for (var i = 0; i < sourceCandidates.Count; i++)
        {
            var source = sourceCandidates[i];
            referrals.Add(new Referral
            {
                Id = FirstSeederId + i,
                PatientId = source.PatientId,
                ReferringDoctorId = source.DoctorId,
                SourceAppointmentId = source.Id,
                TargetSpecializationId = 4, // Kardiologija
                Reason = "Povišen krvni pritisak i nepravilan puls - potrebna kardiološka evaluacija.",
                ResultingAppointmentId = i == 0 ? resultingAppointmentId : null,
                CreatedByUserId = source.CreatedByUserId,
                CreatedAtUtc = source.CreatedAtUtc
            });
        }

        return referrals;
    }

    // --- lab findings: one per each of the first 8 completed appointments -----

    private static readonly byte[] SeedPdfBytes = System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
        "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\n" +
        "xref\n0 4\n0000000000 65535 f \ntrailer<</Size 4/Root 1 0 R>>\nstartxref\n0\n%%EOF");

    private static List<LabFinding> BuildLabFindings(List<Appointment> appointments, DateTime nowUtc)
    {
        var completed = appointments
            .Where(a => a.Status == AppointmentStatus.Completed)
            .OrderBy(a => a.Id)
            .Take(8)
            .ToList();

        var findings = new List<LabFinding>();
        for (var i = 0; i < completed.Count; i++)
        {
            var appointment = completed[i];
            findings.Add(new LabFinding
            {
                Id = FirstSeederId + i,
                PatientId = appointment.PatientId,
                AppointmentId = appointment.Id,
                Result = "Kompletna krvna slika - uredni parametri.",
                FileName = $"nalaz-{appointment.Id}.pdf",
                ContentType = "application/pdf",
                FileData = SeedPdfBytes,
                FileSizeBytes = SeedPdfBytes.LongLength,
                ContentHash = ContentHash.Compute(SeedPdfBytes),
                EnteredByUserId = appointment.CreatedByUserId,
                CreatedAtUtc = appointment.StartUtc.AddHours(2)
            });
        }

        return findings;
    }

    // --- medical record entries: spread across a few different records --------

    private static List<MedicalRecordEntry> BuildMedicalRecordEntries(DateTime nowUtc)
    {
        (int RecordId, string Diagnosis, string Treatment, string Description, int CreatedByUserId)[] rows =
        [
            (1, "I10 - Esencijalna hipertenzija", "Antihipertenziv, kontrola za mjesec dana", "Povišen krvni pritisak na kontroli, uveden lijek.", 3),
            (1, "J06.9 - Akutna infekcija gornjih disajnih puteva", "Simptomatska terapija", "Blaga infekcija, savjetovan odmor i tečnost.", 3),
            (100, "Z00.1 - Rutinska pedijatrijska kontrola", "Nema", "Uredan razvoj, sve vakcine ažurne.", 100),
            (100, "J45.9 - Astma", "Inhalator po potrebi", "Blagi napadi na fizički napor, propisan inhalator.", 100),
            (100, "L20.9 - Atopijski dermatitis", "Lokalna terapija", "Blage promjene na koži, kontrola za 3 mjeseca.", 101),
            (101, "N94.6 - Dismenoreja", "Analgetik po potrebi", "Bolni menstrualni ciklusi, savjetovana terapija.", 101),
            (101, "E03.9 - Hipotireoza", "Nadomjesna terapija hormonima štitnjače", "Nalazi ukazuju na hipotireozu, uvedena terapija.", 3),
            (102, "I25.9 - Hronična ishemijska bolest srca", "Redovna kontrola i terapija", "Stabilno stanje, nastaviti propisanu terapiju.", 102),
            (102, "E11.9 - Dijabetes melitus tip 2", "Dijeta i oralna terapija", "Novodijagnostikovan dijabetes, edukacija pacijenta.", 102)
        ];

        var entries = new List<MedicalRecordEntry>();
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            entries.Add(new MedicalRecordEntry
            {
                Id = FirstSeederId + i,
                MedicalRecordId = row.RecordId,
                EntryDate = DateOnly.FromDateTime(nowUtc.AddDays(-(rows.Length - i) * 4)),
                Diagnosis = row.Diagnosis,
                Treatment = row.Treatment,
                Description = row.Description,
                CreatedByUserId = row.CreatedByUserId,
                CreatedAtUtc = nowUtc.AddDays(-(rows.Length - i) * 4)
            });
        }

        return entries;
    }

    // --- recommender interactions: 3 distinct patients with real history ------

    private static List<RecommenderInteraction> BuildRecommenderInteractions(DateTime nowUtc)
    {
        // Combined with the existing HasData rows (all UserId=4, Patient 1),
        // these two new patients-with-login bring the total to 3 distinct
        // patients with real interaction history - the minimum the content-
        // based recommender needs to demonstrate more than a cold start.
        (int UserId, InteractionType Type, int? DoctorId, int? MedicalServiceId, int DaysAgo)[] rows =
        [
            (110, InteractionType.DoctorView, 100, null, 1),
            (110, InteractionType.MedicalServiceView, null, 3, 2),
            (110, InteractionType.Search, 100, 3, 4),
            (110, InteractionType.DoctorView, 100, null, 6),
            (110, InteractionType.MedicalServiceView, null, 1, 9),
            (111, InteractionType.DoctorView, 101, null, 1),
            (111, InteractionType.MedicalServiceView, null, 2, 3),
            (111, InteractionType.Search, 101, 2, 5),
            (111, InteractionType.DoctorView, 101, null, 8),
            (111, InteractionType.MedicalServiceView, null, 2, 11)
        ];

        var interactions = new List<RecommenderInteraction>();
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            interactions.Add(new RecommenderInteraction
            {
                Id = FirstSeederId + i,
                UserId = row.UserId,
                InteractionType = row.Type,
                DoctorId = row.DoctorId,
                MedicalServiceId = row.MedicalServiceId,
                DateTimeUtc = nowUtc.AddDays(-row.DaysAgo)
            });
        }

        return interactions;
    }

    // --- notifications, news, schedule block -----------------------------------

    private static List<Notification> BuildNotifications(DateTime nowUtc)
    {
        (int UserId, string Title, string Text, bool IsRead, int HoursAgo)[] rows =
        [
            (4, "Podsjetnik za termin", "Vaš termin je zakazan za danas. Ne zaboravite ponijeti ličnu kartu.", false, 20),
            (1, "Novi pacijent registrovan", "Novi pacijent se registrovao putem mobilne aplikacije.", true, 30),
            (2, "Nadolazeći termini", "Provjerite raspored za sutra - nekoliko termina čeka potvrdu.", false, 40),
            (3, "Termin potvrđen", "Pacijent je potvrdio dolazak na zakazani termin.", true, 50),
            (100, "Novi termin zakazan", "Zakazan je novi termin pregleda.", false, 15),
            (110, "Dobrodošli u ClinicNow", "Vaš nalog je uspješno kreiran. Zakažite svoj prvi termin iz aplikacije.", false, 60)
        ];

        var notifications = new List<Notification>();
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            var createdAtUtc = nowUtc.AddHours(-row.HoursAgo);
            notifications.Add(new Notification
            {
                Id = FirstSeederId + i,
                UserId = row.UserId,
                Title = row.Title,
                Text = row.Text,
                IsRead = row.IsRead,
                CreatedAtUtc = createdAtUtc,
                ReadAtUtc = row.IsRead ? createdAtUtc.AddMinutes(10) : null
            });
        }

        return notifications;
    }

    private static readonly byte[] SeedImageBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static NewsItem BuildNewsItem(DateTime nowUtc) => new()
    {
        Id = FirstSeederId,
        Title = "Produženo radno vrijeme",
        Text = "Poliklinika sada radi i subotom u prijepodnevnim satima za hitne preglede.",
        ImageData = SeedImageBytes,
        ContentHash = ContentHash.Compute(SeedImageBytes),
        ImageContentType = "image/png",
        CreatedAtUtc = nowUtc.AddDays(-1)
    };

    private static ScheduleBlock BuildScheduleBlock(DateTime nowLocal)
    {
        var startLocal = DateOnly.FromDateTime(nowLocal.AddDays(14));
        var endLocal = DateOnly.FromDateTime(nowLocal.AddDays(18));

        return new ScheduleBlock
        {
            Id = FirstSeederId,
            DoctorId = 100,
            StartUtc = ClinicTimeZone.ToUtc(startLocal, TimeOnly.MinValue),
            EndUtc = ClinicTimeZone.ToUtc(endLocal, new TimeOnly(23, 59, 59)),
            Reason = "Godišnji odmor"
        };
    }
}
