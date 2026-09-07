using System.Security.Claims;
using ClinicNow.Model.Common;
using ClinicNow.Model.Configuration;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Services.Appointments;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ClinicNow.Services.Recommender;

public class RecommenderService : IRecommenderService
{
    private const string ModelCacheKey = "recommender:model";

    private readonly ClinicNowContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMemoryCache _cache;
    private readonly RecommenderOptions _options;
    private readonly ILogger<RecommenderService> _logger;

    public RecommenderService(
        ClinicNowContext context,
        IHttpContextAccessor httpContextAccessor,
        IMemoryCache cache,
        RecommenderOptions options,
        ILogger<RecommenderService> logger)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task LogInteractionAsync(RecommenderInteractionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.IsDefined(typeof(InteractionType), request.Type))
        {
            // Model binding happily turns any int into an InteractionType, so
            // without this an undefined value (e.g. `{"type": 99}`) would be
            // persisted and then silently never match any scoring branch.
            throw new ValidationException("type", "Nepoznat tip interakcije.");
        }

        if (request.DoctorId is null && request.MedicalServiceId is null)
        {
            // A row with neither can never be turned into a feature row (see
            // BuildInteractionHistoryRows below) - rejecting it here is what
            // makes "every collected signal is actually used" true rather
            // than aspirational (recommender-dokumentacija.md §3).
            throw new ValidationException("doctorId", "Interakcija mora biti vezana za doktora ili uslugu.");
        }

        if (request.DoctorId.HasValue && !await _context.Doctors.AnyAsync(d => d.Id == request.DoctorId.Value, cancellationToken))
        {
            throw new ValidationException("doctorId", "Odabrani doktor ne postoji.");
        }

        if (request.MedicalServiceId.HasValue && !await _context.MedicalServices.AnyAsync(s => s.Id == request.MedicalServiceId.Value, cancellationToken))
        {
            throw new ValidationException("medicalServiceId", "Odabrana usluga ne postoji.");
        }

        _context.RecommenderInteractions.Add(new RecommenderInteraction
        {
            UserId = CurrentUserId(CurrentUser()),
            InteractionType = request.Type,
            DoctorId = request.DoctorId,
            MedicalServiceId = request.MedicalServiceId,
            DateTimeUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    private ClaimsPrincipal CurrentUser() =>
        _httpContextAccessor.HttpContext?.User ?? throw new AuthenticationException("Nema aktivne sesije.");

    private static int CurrentUserId(ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    private async Task<int> GetOwnPatientIdAsync(int userId, CancellationToken cancellationToken)
    {
        var patient = await _context.Patients.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Nije pronađen medicinski karton za ovaj nalog.");
        return patient.Id;
    }

    // --- ML.NET feature rows -----------------------------------------------------

    /// <summary>
    /// One "content" row fed to the pipeline - either a training-catalog row,
    /// a patient history row, or a scoring candidate. Column names here are
    /// exactly what recommender-dokumentacija.md §4's pipeline diagram names.
    /// </summary>
    private class RecommenderFeatureRow
    {
        public string MedicalServiceText { get; set; } = string.Empty;
        public string SpecializationText { get; set; } = string.Empty;
        public string DoctorText { get; set; } = string.Empty;
        public string DayOfWeek { get; set; } = string.Empty;
        public string TimeOfDay { get; set; } = string.Empty;
    }

    private class RecommenderFeaturizedRow
    {
        [VectorType]
        public float[] Features { get; set; } = [];
    }

    private static RecommenderFeatureRow BuildFeatureRow(
        Doctor doctor, ClinicNow.Services.Database.Entities.MedicalService? medicalService, DateTime pointInTimeUtc)
    {
        var specializationText = string.Join(' ', doctor.DoctorSpecializations.Select(ds => ds.Specialization.Name));
        var doctorText = $"{doctor.User.FirstName} {doctor.User.LastName}";
        var medicalServiceText = medicalService is null
            ? string.Empty
            : $"{medicalService.Name} {medicalService.Description}".Trim();

        return new RecommenderFeatureRow
        {
            MedicalServiceText = medicalServiceText,
            SpecializationText = specializationText,
            DoctorText = doctorText,
            // Clinic-local, not UTC: "ponedjeljkom ujutro" is a statement about the
            // clinic's clock, and near midnight the UTC weekday is a different day
            // altogether (review item C1).
            DayOfWeek = ClinicTimeZone.LocalDayOfWeekOf(pointInTimeUtc).ToString(),
            TimeOfDay = pointInTimeUtc.ToClinicTimeOfDayBucket().ToString()
        };
    }

    private static IEstimator<ITransformer> BuildPipeline(MLContext mlContext) =>
        mlContext.Transforms.Text.FeaturizeText("MedicalServiceFeat", nameof(RecommenderFeatureRow.MedicalServiceText))
            .Append(mlContext.Transforms.Text.FeaturizeText("SpecializationFeat", nameof(RecommenderFeatureRow.SpecializationText)))
            .Append(mlContext.Transforms.Text.FeaturizeText("DoctorFeat", nameof(RecommenderFeatureRow.DoctorText)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("DayOfWeekFeat", nameof(RecommenderFeatureRow.DayOfWeek)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("TimeOfDayFeat", nameof(RecommenderFeatureRow.TimeOfDay)))
            .Append(mlContext.Transforms.Concatenate("Features", "MedicalServiceFeat", "SpecializationFeat", "DoctorFeat", "DayOfWeekFeat", "TimeOfDayFeat"));

    /// <summary>
    /// 0 until this process has made its one cold-start attempt to reuse the
    /// model already serialized on disk. Deliberately <c>static</c>: the
    /// service is <c>Scoped</c>, so an instance field would reset every
    /// request and make every cache miss look like a cold start. "Have we
    /// looked at the model file yet since startup" is a property of the
    /// process, not of a request.
    /// </summary>
    private static int _coldStartLoadAttempted;

    /// <summary>
    /// Serializes the train-or-load section of <see cref="GetOrTrainModelAsync"/>.
    /// <c>IMemoryCache</c>'s <c>TryGetValue</c>/<c>Set</c> pair is not atomic,
    /// so at a <see cref="RecommenderOptions.RetrainIntervalMinutes"/> boundary
    /// several concurrent requests can all observe the same cache miss (a
    /// classic cache stampede). Without this gate they would all reach
    /// <see cref="TrainAndSaveAsync"/> and call <c>Model.Save</c> on the same
    /// <see cref="RecommenderOptions.ModelPath"/> simultaneously, which throws
    /// <c>IOException</c> ("file in use") and surfaces as a 500 - and would
    /// recur at every TTL boundary under concurrent load. <c>static</c> for
    /// the same reason <see cref="_coldStartLoadAttempted"/> is: it has to
    /// serialize across the concurrent <c>Scoped</c> instances inside one
    /// process, not per request. Never disposed - it lives for the process.
    /// </summary>
    private static readonly SemaphoreSlim _modelTrainLock = new(1, 1);

    /// <summary>
    /// Returns the trained transformer, cached process-wide for
    /// <see cref="RecommenderOptions.RetrainIntervalMinutes"/> (doc §7:
    /// "teški resursi ... dijele se na nivou aplikacije uz odgovarajuće
    /// keširanje").
    ///
    /// Only the first cache miss after process start may reuse a model
    /// already on disk. Every later miss is a TTL expiry, and genuinely
    /// retrains - overwriting the file - which is what doc §7's "model se
    /// osvježava periodično / pri značajnijoj promjeni kataloga usluga i
    /// doktora (nova usluga, novi doktor)" requires: reloading the identical
    /// serialized bytes would freeze the TF-IDF vocabulary at whatever the
    /// catalog looked like on the very first train, so a doctor or service
    /// added later would stay permanently out-of-vocabulary.
    ///
    /// Train-or-load is guarded by <see cref="_modelTrainLock"/> with
    /// double-checked locking, so a cache-miss stampede produces exactly one
    /// train-or-load and every other racer reuses its result rather than
    /// racing it to write the model file.
    ///
    /// Only the <see cref="ITransformer"/> is cached, never the
    /// <see cref="MLContext"/> that produced it: <c>MLContext</c> is not
    /// thread-safe for concurrent operations, so a shared instance driving
    /// every concurrent request's featurization would be a data race. A
    /// transformer does not depend on its originating context - any
    /// <c>MLContext</c> can drive <c>Transform</c> - so callers create a cheap
    /// per-request one instead (see <see cref="GetRecommendationsAsync"/>).
    /// </summary>
    private async Task<ITransformer> GetOrTrainModelAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(ModelCacheKey, out ITransformer? cached) && cached is not null)
        {
            return cached;
        }

        await _modelTrainLock.WaitAsync(cancellationToken);
        try
        {
            // Second check, now under the lock: whoever held it may have just
            // trained/loaded and repopulated the cache while this caller was
            // waiting. Reuse that instead of doing the work (and the file
            // write) all over again.
            if (_cache.TryGetValue(ModelCacheKey, out ITransformer? cachedAfterWait) && cachedAfterWait is not null)
            {
                return cachedAfterWait;
            }

            var mlContext = new MLContext(seed: 0);
            ITransformer model;

            // Atomic test-and-set. Redundant under the lock, but it keeps the
            // "first attempt in this process wins" intent explicit and correct
            // independently of the surrounding synchronization.
            var isColdStart = Interlocked.Exchange(ref _coldStartLoadAttempted, 1) == 0;

            if (isColdStart && File.Exists(_options.ModelPath))
            {
                _logger.LogInformation("Cold start - loading recommender model from {Path}.", _options.ModelPath);
                model = mlContext.Model.Load(_options.ModelPath, out _);
            }
            else
            {
                if (isColdStart)
                {
                    _logger.LogInformation("No recommender model found at {Path} - training a new one.", _options.ModelPath);
                }
                else
                {
                    _logger.LogInformation("Recommender model cache expired - retraining and overwriting {Path}.", _options.ModelPath);
                }

                model = await TrainAndSaveAsync(mlContext, cancellationToken);
            }

            _cache.Set(ModelCacheKey, model, TimeSpan.FromMinutes(_options.RetrainIntervalMinutes));
            return model;
        }
        finally
        {
            _modelTrainLock.Release();
        }
    }

    private async Task<ITransformer> TrainAndSaveAsync(MLContext mlContext, CancellationToken cancellationToken)
    {
        var doctors = await _context.Doctors
            .Include(d => d.User)
            .Include(d => d.DoctorSpecializations).ThenInclude(ds => ds.Specialization)
            .ToListAsync(cancellationToken);
        var services = await _context.MedicalServices.ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        // The real catalog: every (Doctor, MedicalService) combination "at
        // this moment" - gives the text featurizers real vocabulary to learn
        // from (doc §7: "nad skupom svih usluga/termina").
        var catalogRows = doctors.SelectMany(d => services.Select(s => BuildFeatureRow(d, s, now))).ToList();

        // OneHotEncoding only learns categories it has actually seen during
        // Fit. With only 2 seeded doctors, the real catalog above might not
        // exercise all 7 DayOfWeek / 3 TimeOfDay values, which would make
        // the encoder silently zero out any candidate/history row that later
        // lands on an unseen day/time bucket - a real correctness bug, not a
        // hypothetical one. These 10 extra rows exist purely to guarantee
        // every category value is present in the training data at least
        // once; their text fields are intentionally empty so they don't
        // skew the TF-IDF vocabulary.
        var vocabularySeedRows = Enum.GetValues<DayOfWeek>()
            .Select(day => new RecommenderFeatureRow { DayOfWeek = day.ToString(), TimeOfDay = TimeOfDayBucket.Morning.ToString() })
            .Concat(Enum.GetValues<TimeOfDayBucket>().Select(bucket => new RecommenderFeatureRow { DayOfWeek = DayOfWeek.Monday.ToString(), TimeOfDay = bucket.ToString() }))
            .ToList();

        var trainingData = mlContext.Data.LoadFromEnumerable(catalogRows.Concat(vocabularySeedRows));
        var pipeline = BuildPipeline(mlContext);
        var model = pipeline.Fit(trainingData);

        // Persisting is an optimization, never a precondition: the freshly
        // trained model is cached in IMemoryCache regardless. A read-only
        // volume, a directory the process user cannot write to, or a locked
        // file must degrade to "serve from memory, retrain next TTL" rather
        // than 500 the whole recommendation endpoint.
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_options.ModelPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            mlContext.Model.Save(model, trainingData.Schema, _options.ModelPath);
            _logger.LogInformation("Recommender model trained on {Count} catalog rows and saved to {Path}.", catalogRows.Count, _options.ModelPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recommender model trained on {Count} catalog rows but could not be persisted to {Path} - continuing with the in-memory model only.", catalogRows.Count, _options.ModelPath);
        }

        return model;
    }

    private static float[][] Featurize(MLContext mlContext, ITransformer model, List<RecommenderFeatureRow> rows)
    {
        if (rows.Count == 0) return [];
        var dataView = mlContext.Data.LoadFromEnumerable(rows);
        var transformed = model.Transform(dataView);
        return mlContext.Data.CreateEnumerable<RecommenderFeaturizedRow>(transformed, reuseRowObject: false)
            .Select(r => r.Features)
            .ToArray();
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        var length = Math.Min(a.Length, b.Length);
        for (var i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    /// <summary>One weighted item in the patient's "taste profile" (doc §5.2's `H`) - either a past completed/confirmed appointment or a logged interaction.</summary>
    private record HistoryItem(RecommenderFeatureRow Features, double Weight, int? DoctorId, int? MedicalServiceId, bool FromAppointment, string? DoctorLastName, string? MedicalServiceName, DateTime DateTimeUtc);

    /// <summary>
    /// doc §5.2's `recencyFactor(h) = 1 / (1 + daysSince(h) / 30)`, decaying
    /// from 1.0 (today) toward 0 as a history item ages.
    ///
    /// The age is clamped to non-negative because `H` legitimately contains
    /// future-dated items: `BuildAppointmentHistoryAsync` includes `Confirmed`
    /// appointments (doc §3 row 1), whose `StartUtc` has not happened yet.
    /// Without the clamp a future item's "days since" goes negative, which
    /// inflates the weight (25 days out would score ~6x a visit that happened
    /// today), divides by zero at exactly +30 days (PositiveInfinity -> NaN
    /// score -> a 500 when System.Text.Json serializes the DTO), and flips
    /// negative past +30 days, silently dropping the item at the
    /// `Where(h => h.Weight > 0)` filter. An upcoming appointment is treated
    /// as maximally recent (factor 1.0) instead.
    /// </summary>
    private static double RecencyFactor(DateTime pointInTimeUtc, DateTime nowUtc) =>
        1.0 / (1.0 + Math.Max(0.0, (nowUtc - pointInTimeUtc).TotalDays) / 30.0);

    /// <summary>Builds `H` from real `Appointment` rows (doc §3 row 1: status Completed/Confirmed) - `frequencyFactor(h)` is the count of history items sharing the same (Doctor, MedicalService) pair (repeat visits weigh more).</summary>
    private async Task<List<HistoryItem>> BuildAppointmentHistoryAsync(int patientId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var appointments = await _context.Appointments
            .Include(a => a.Doctor).ThenInclude(d => d.User)
            .Include(a => a.Doctor).ThenInclude(d => d.DoctorSpecializations).ThenInclude(ds => ds.Specialization)
            .Include(a => a.MedicalService)
            .Where(a => a.PatientId == patientId && (a.Status == Model.Common.AppointmentStatus.Completed || a.Status == Model.Common.AppointmentStatus.Confirmed))
            .ToListAsync(cancellationToken);

        return appointments.Select(a =>
        {
            var frequencyFactor = appointments.Count(x => x.DoctorId == a.DoctorId && x.MedicalServiceId == a.MedicalServiceId);
            var weight = frequencyFactor * RecencyFactor(a.StartUtc, nowUtc);
            return new HistoryItem(
                BuildFeatureRow(a.Doctor, a.MedicalService, a.StartUtc), weight,
                a.DoctorId, a.MedicalServiceId, FromAppointment: true,
                a.Doctor.User.LastName, a.MedicalService.Name, a.StartUtc);
        }).ToList();
    }

    /// <summary>
    /// Extends `H` with real interaction rows (doc §3 row 7 - "pojačanje
    /// (boost) profila"): every `RecommenderInteraction` this patient's user
    /// account produced in the last 180 days becomes its own weighted
    /// pseudo-history item, so the signal is never merely stored - it is
    /// scored exactly like an appointment, just at a smaller base weight
    /// (0.4x - a view/search is a weaker taste signal than a completed
    /// visit). A DoctorView row has no service (MedicalServiceText stays
    /// empty in its feature row) and vice versa for MedicalServiceView -
    /// FeaturizeText handles an empty string as a valid, near-zero vector,
    /// so this doesn't break the concatenated feature dimensionality.
    /// </summary>
    private async Task<List<HistoryItem>> BuildInteractionHistoryRows(int userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        const double InteractionBaseWeight = 0.4;
        var cutoff = nowUtc.AddDays(-180);

        var interactions = await _context.RecommenderInteractions
            .Include(i => i.Doctor).ThenInclude(d => d!.User)
            .Include(i => i.Doctor).ThenInclude(d => d!.DoctorSpecializations).ThenInclude(ds => ds.Specialization)
            .Include(i => i.MedicalService)
            .Where(i => i.UserId == userId && i.DateTimeUtc >= cutoff)
            .ToListAsync(cancellationToken);

        var result = new List<HistoryItem>();
        foreach (var interaction in interactions)
        {
            if (interaction.Doctor is null && interaction.MedicalService is null) continue; // defensive - LogInteractionAsync already forbids this combination

            var frequencyFactor = interactions.Count(x =>
                x.InteractionType == interaction.InteractionType &&
                x.DoctorId == interaction.DoctorId &&
                x.MedicalServiceId == interaction.MedicalServiceId);
            var weight = InteractionBaseWeight * frequencyFactor * RecencyFactor(interaction.DateTimeUtc, nowUtc);

            var doctor = interaction.Doctor;
            var featureRow = doctor is not null
                ? BuildFeatureRow(doctor, interaction.MedicalService, interaction.DateTimeUtc)
                : new RecommenderFeatureRow
                {
                    MedicalServiceText = $"{interaction.MedicalService!.Name} {interaction.MedicalService.Description}".Trim(),
                    DayOfWeek = ClinicTimeZone.LocalDayOfWeekOf(interaction.DateTimeUtc).ToString(),
                    TimeOfDay = interaction.DateTimeUtc.ToClinicTimeOfDayBucket().ToString()
                };

            result.Add(new HistoryItem(
                featureRow, weight, interaction.DoctorId, interaction.MedicalServiceId, FromAppointment: false,
                doctor?.User.LastName, interaction.MedicalService?.Name, interaction.DateTimeUtc));
        }
        return result;
    }

    /// <summary>
    /// Real candidate free slots (doc §5.2's `C`): for every (Doctor,
    /// MedicalService) pair not already actively booked by this patient, the
    /// earliest genuinely free slot within <see cref="RecommenderOptions.CandidateLookaheadDays"/>,
    /// running the same real-slot rule the booking flow uses (rulebook §7: only
    /// real free slots offered, never a synthetic guess) over a batch-loaded
    /// window.
    ///
    /// This used to call <see cref="IAppointmentService.GetAvailableSlotsAsync"/>
    /// inside a doctor x service x day triple loop, each call issuing several
    /// queries - SQL in a loop, which review item C19 rejects outright. The
    /// schedule is now loaded once for the whole lookahead
    /// (<see cref="LoadSlotWindowAsync"/>) and every pair is evaluated in memory
    /// through <see cref="SlotGeneration"/>, so the query count no longer grows
    /// with the size of the catalog.
    /// </summary>
    private async Task<List<(Doctor Doctor, ClinicNow.Services.Database.Entities.MedicalService MedicalService, DateTime StartUtc)>> BuildCandidatesAsync(
        int patientId, List<Doctor> doctors, List<ClinicNow.Services.Database.Entities.MedicalService> services, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var alreadyBooked = await _context.Appointments
            .Where(a => a.PatientId == patientId && (a.Status == Model.Common.AppointmentStatus.Pending || a.Status == Model.Common.AppointmentStatus.Confirmed))
            .Select(a => new { a.DoctorId, a.MedicalServiceId })
            .ToListAsync(cancellationToken);

        var candidates = new List<(Doctor, ClinicNow.Services.Database.Entities.MedicalService, DateTime)>();

        // Three queries for the entire lookahead, regardless of how many
        // doctors/services/days follow (review item C19).
        var schedule = await LoadSlotWindowAsync(doctors.Select(d => d.Id).ToList(), nowUtc, cancellationToken);

        foreach (var doctor in doctors)
        {
            foreach (var service in services)
            {
                if (alreadyBooked.Any(b => b.DoctorId == doctor.Id && b.MedicalServiceId == service.Id)) continue;

                // Never pair a doctor with a service they aren't qualified for
                // (review item C2) - this loop used to emit every doctor x every
                // service. Checked in memory: DoctorSpecializations is already
                // included for the feature row, so this costs no extra query.
                if (!Appointments.DoctorCompatibility.CanPerform(doctor, service)) continue;

                var earliest = EarliestFreeSlot(doctor, service, schedule, nowUtc);
                if (earliest is not null)
                {
                    candidates.Add((doctor, service, earliest.Value));
                }
            }
        }

        return candidates;
    }

    /// <summary>
    /// Every doctor's working hours, schedule blocks and non-cancelled
    /// appointments across the whole candidate lookahead, in three queries -
    /// the batch load review item C19 asks for.
    ///
    /// Blocks and appointments are fetched for the full window rather than per
    /// day: <see cref="SlotGeneration.FreeSlots"/> tests each span by overlap
    /// against the individual slot, so a wider set is correct, and one query
    /// beats one per day. Working hours are a recurring weekly pattern (seven
    /// rows per doctor at most), so they are simply loaded whole and grouped by
    /// day of week in memory.
    /// </summary>
    private async Task<SlotWindow> LoadSlotWindowAsync(
        IReadOnlyCollection<int> doctorIds, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var firstLocalDate = ClinicTimeZone.LocalDateOf(nowUtc);
        var windowStartUtc = ClinicTimeZone.LocalDateStartUtc(firstLocalDate);
        var windowEndUtc = ClinicTimeZone.LocalDateEndExclusiveUtc(firstLocalDate.AddDays(_options.CandidateLookaheadDays - 1));

        var workingHours = await _context.WorkingHoursEntries
            .Where(w => doctorIds.Contains(w.DoctorId))
            .ToListAsync(cancellationToken);

        var blocks = await _context.ScheduleBlocks
            .Where(b => doctorIds.Contains(b.DoctorId) && b.StartUtc < windowEndUtc && b.EndUtc > windowStartUtc)
            .Select(b => new { b.DoctorId, b.StartUtc, b.EndUtc })
            .ToListAsync(cancellationToken);

        var busy = await _context.Appointments
            .Where(a => doctorIds.Contains(a.DoctorId) && a.Status != Model.Common.AppointmentStatus.Cancelled
                        && a.StartUtc < windowEndUtc && a.EndUtc > windowStartUtc)
            .Select(a => new { a.DoctorId, a.StartUtc, a.EndUtc })
            .ToListAsync(cancellationToken);

        return new SlotWindow(
            workingHours.GroupBy(w => w.DoctorId).ToDictionary(g => g.Key, g => g.ToList()),
            blocks.GroupBy(b => b.DoctorId).ToDictionary(
                g => g.Key, g => g.Select(b => new SlotGeneration.Interval(b.StartUtc, b.EndUtc)).ToList()),
            busy.GroupBy(a => a.DoctorId).ToDictionary(
                g => g.Key, g => g.Select(a => new SlotGeneration.Interval(a.StartUtc, a.EndUtc)).ToList()));
    }

    /// <summary>
    /// The earliest genuinely free slot for one (doctor, service) pair inside the
    /// pre-loaded window, or null if the pair has none. Same rule as
    /// <see cref="IAppointmentService.GetAvailableSlotsAsync"/> - it shares
    /// <see cref="SlotGeneration.FreeSlots"/> with it - just fed from memory.
    ///
    /// One deliberate difference: <paramref name="nowUtc"/> is the single instant
    /// the whole recommendation is computed against, where the per-call path
    /// re-reads <c>DateTime.UtcNow</c> each time. A fixed instant is what makes a
    /// batch self-consistent (and what makes it testable).
    /// </summary>
    private DateTime? EarliestFreeSlot(
        Doctor doctor, ClinicNow.Services.Database.Entities.MedicalService service, SlotWindow schedule, DateTime nowUtc)
    {
        if (!schedule.WorkingHours.TryGetValue(doctor.Id, out var doctorHours))
        {
            return null;
        }

        var blocks = schedule.Blocks.GetValueOrDefault(doctor.Id, []);
        var busy = schedule.Busy.GetValueOrDefault(doctor.Id, []);
        var duration = TimeSpan.FromMinutes(service.DurationMinutes);
        var firstLocalDate = ClinicTimeZone.LocalDateOf(nowUtc);

        for (var offset = 0; offset < _options.CandidateLookaheadDays; offset++)
        {
            // Clinic-local calendar day - a UTC-derived one is off by a day near
            // midnight (review item C1).
            var date = firstLocalDate.AddDays(offset);
            var dayWindows = doctorHours.Where(w => w.DayOfWeek == date.DayOfWeek).ToList();
            if (dayWindows.Count == 0) continue;

            var slots = SlotGeneration.FreeSlots(date, dayWindows, blocks, busy, duration, nowUtc);
            // Earliest slot, not merely the first returned - FreeSlots appends
            // per working-hours window and does not promise a sorted list.
            if (slots.Count > 0) return slots.Min();
        }

        return null;
    }

    /// <summary>The whole clinic's schedule for the lookahead window, keyed by doctor.</summary>
    private sealed record SlotWindow(
        Dictionary<int, List<Database.Entities.WorkingHours>> WorkingHours,
        Dictionary<int, List<SlotGeneration.Interval>> Blocks,
        Dictionary<int, List<SlotGeneration.Interval>> Busy);

    /// <summary>Cold-start fallback (doc §2.2): most-booked (Doctor, MedicalService) pairs in the last <see cref="RecommenderOptions.PopularityWindowDays"/> days, via one GROUP BY - no per-user history required.</summary>
    private async Task<List<AppointmentRecommendationDto>> GetPopularityFallbackAsync(int patientId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var windowStart = nowUtc.AddDays(-_options.PopularityWindowDays);

        var popular = await _context.Appointments
            .Where(a => a.CreatedAtUtc >= windowStart && a.Status != Model.Common.AppointmentStatus.Cancelled)
            .GroupBy(a => new { a.DoctorId, a.MedicalServiceId })
            .Select(g => new { g.Key.DoctorId, g.Key.MedicalServiceId, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(_options.TopN)
            .ToListAsync(cancellationToken);

        if (popular.Count == 0)
        {
            // Nothing booked inside the window - most often a freshly migrated
            // database whose seeded appointments predate it. Returning an empty
            // list here would defeat the entire point of the fallback (this is
            // the cold-start path), so widen to all-time most-booked pairs
            // before giving up.
            popular = await _context.Appointments
                .Where(a => a.Status != Model.Common.AppointmentStatus.Cancelled)
                .GroupBy(a => new { a.DoctorId, a.MedicalServiceId })
                .Select(g => new { g.Key.DoctorId, g.Key.MedicalServiceId, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(_options.TopN)
                .ToListAsync(cancellationToken);
        }

        var doctors = await _context.Doctors.Include(d => d.User).Include(d => d.Location)
            .Include(d => d.DoctorSpecializations).ThenInclude(ds => ds.Specialization)
            .ToDictionaryAsync(d => d.Id, cancellationToken);
        var services = await _context.MedicalServices.ToDictionaryAsync(s => s.Id, cancellationToken);

        var alreadyBooked = await _context.Appointments
            .Where(a => a.PatientId == patientId && (a.Status == Model.Common.AppointmentStatus.Pending || a.Status == Model.Common.AppointmentStatus.Confirmed))
            .Select(a => new { a.DoctorId, a.MedicalServiceId })
            .ToListAsync(cancellationToken);

        // Same batch load as the main path - this fallback looped
        // GetAvailableSlotsAsync too (review item C19).
        var schedule = await LoadSlotWindowAsync(doctors.Keys.ToList(), nowUtc, cancellationToken);

        var result = new List<AppointmentRecommendationDto>();
        foreach (var row in popular)
        {
            if (alreadyBooked.Any(b => b.DoctorId == row.DoctorId && b.MedicalServiceId == row.MedicalServiceId)) continue;
            if (!doctors.TryGetValue(row.DoctorId, out var doctor) || !services.TryGetValue(row.MedicalServiceId, out var service)) continue;
            // Popular historically, but the pairing must still be one the booking
            // endpoint would accept today (review item C2).
            if (!Appointments.DoctorCompatibility.CanPerform(doctor, service)) continue;

            var suggestedStart = EarliestFreeSlot(doctor, service, schedule, nowUtc);
            if (suggestedStart is null) continue;

            result.Add(new AppointmentRecommendationDto
            {
                DoctorId = doctor.Id,
                DoctorName = $"{doctor.User.FirstName} {doctor.User.LastName}",
                DoctorSpecializations = doctor.DoctorSpecializations.Select(ds => ds.Specialization.Name).ToList(),
                MedicalServiceId = service.Id,
                MedicalServiceName = service.Name,
                MedicalServicePrice = service.Price,
                LocationId = doctor.LocationId,
                LocationName = doctor.Location.Name,
                SuggestedStartUtc = suggestedStart.Value,
                Score = 0,
                Reason = $"Popularno u posljednje vrijeme: {service.Name} je jedna od najčešće zakazivanih usluga.",
                IsPopularityFallback = true
            });
        }
        return result;
    }

    public async Task<List<AppointmentRecommendationDto>> GetRecommendationsAsync(CancellationToken cancellationToken = default)
    {
        var patientId = await GetOwnPatientIdAsync(CurrentUserId(CurrentUser()), cancellationToken);
        var nowUtc = DateTime.UtcNow;

        var appointmentHistory = await BuildAppointmentHistoryAsync(patientId, nowUtc, cancellationToken);
        var interactionHistory = await BuildInteractionHistoryRows(CurrentUserId(CurrentUser()), nowUtc, cancellationToken);
        var history = appointmentHistory.Concat(interactionHistory).Where(h => h.Weight > 0).ToList();

        // Cold start (doc §2.2): no usable history at all -> popularity fallback outright.
        if (history.Count == 0)
        {
            return await GetPopularityFallbackAsync(patientId, nowUtc, cancellationToken);
        }

        var doctors = await _context.Doctors.Include(d => d.User).Include(d => d.Location)
            .Include(d => d.DoctorSpecializations).ThenInclude(ds => ds.Specialization).ToListAsync(cancellationToken);
        var services = await _context.MedicalServices.ToListAsync(cancellationToken);

        var candidates = await BuildCandidatesAsync(patientId, doctors, services, nowUtc, cancellationToken);
        if (candidates.Count == 0)
        {
            // Edge case (doc §9): every candidate already booked by this patient -> fall back to popularity.
            return await GetPopularityFallbackAsync(patientId, nowUtc, cancellationToken);
        }

        var model = await GetOrTrainModelAsync(cancellationToken);

        // MLContext is not thread-safe for concurrent operations, so this
        // request gets its own throwaway instance to drive featurization
        // rather than sharing the cached model's originating context with
        // every other in-flight request. Constructing one is cheap.
        var mlContext = new MLContext(seed: 0);

        var historyRows = history.Select(h => h.Features).ToList();
        var candidateRows = candidates.Select(c => BuildFeatureRow(c.Doctor, c.MedicalService, c.StartUtc)).ToList();

        var historyVectors = Featurize(mlContext, model, historyRows);
        var candidateVectors = Featurize(mlContext, model, candidateRows);

        var scored = new List<AppointmentRecommendationDto>();
        for (var c = 0; c < candidates.Count; c++)
        {
            double weightedSum = 0, weightTotal = 0;
            var dominantIndex = 0;
            var dominantContribution = double.NegativeInfinity;

            for (var h = 0; h < history.Count; h++)
            {
                var similarity = CosineSimilarity(candidateVectors[c], historyVectors[h]);
                var contribution = history[h].Weight * similarity;
                weightedSum += contribution;
                weightTotal += history[h].Weight;
                if (contribution > dominantContribution)
                {
                    dominantContribution = contribution;
                    dominantIndex = h;
                }
            }

            var score = weightTotal == 0 ? 0 : weightedSum / weightTotal;
            if (score <= 0) continue;

            var (doctor, service, startUtc) = candidates[c];
            scored.Add(new AppointmentRecommendationDto
            {
                DoctorId = doctor.Id,
                DoctorName = $"{doctor.User.FirstName} {doctor.User.LastName}",
                DoctorSpecializations = doctor.DoctorSpecializations.Select(ds => ds.Specialization.Name).ToList(),
                MedicalServiceId = service.Id,
                MedicalServiceName = service.Name,
                MedicalServicePrice = service.Price,
                LocationId = doctor.LocationId,
                LocationName = doctor.Location.Name,
                SuggestedStartUtc = startUtc,
                Score = score,
                Reason = BuildReason(history[dominantIndex], doctor, service),
                IsPopularityFallback = false
            });
        }

        var ranked = scored.OrderByDescending(r => r.Score).Take(_options.TopN).ToList();

        // Edge case (doc §9): candidates existed but none scored above 0 (e.g. a
        // brand-new doctor/service with no textual overlap with this patient's
        // history at all) -> popularity fallback rather than an empty list.
        return ranked.Count > 0 ? ranked : await GetPopularityFallbackAsync(patientId, nowUtc, cancellationToken);
    }

    /// <summary>Builds the Bosnian explanation from the single history item that contributed most to a candidate's score (doc §6 - grounded in the real dominant signal, never generic).</summary>
    private static string BuildReason(HistoryItem dominant, Doctor candidateDoctor, ClinicNow.Services.Database.Entities.MedicalService candidateService)
    {
        // The explanation is shown to the patient, so it must describe the clinic's
        // clock - telling someone they book "ponedjeljkom ujutro" off a UTC instant
        // names the wrong day for anything after 22:00 local (review item C1).
        var dayBosnian = ClinicTimeZone.LocalDayOfWeekOf(dominant.DateTimeUtc) switch
        {
            DayOfWeek.Monday => "ponedjeljkom", DayOfWeek.Tuesday => "utorkom", DayOfWeek.Wednesday => "srijedom",
            DayOfWeek.Thursday => "četvrtkom", DayOfWeek.Friday => "petkom", DayOfWeek.Saturday => "subotom",
            _ => "nedjeljom"
        };
        var timeOfDayBosnian = dominant.DateTimeUtc.ToClinicTimeOfDayBucket().ToDisplayName();

        if (dominant.DoctorId == candidateDoctor.Id && dominant.DoctorLastName is not null)
        {
            return dominant.FromAppointment
                ? $"Predlažemo ovaj termin jer ste ranije više puta birali dr. {dominant.DoctorLastName} {dayBosnian} {timeOfDayBosnian}."
                : $"Preporučeno jer ste nedavno pregledali profil dr. {dominant.DoctorLastName}.";
        }

        if (dominant.MedicalServiceId == candidateService.Id && dominant.MedicalServiceName is not null)
        {
            return dominant.FromAppointment
                ? $"Preporučeno na osnovu vaših prethodnih pregleda tipa „{dominant.MedicalServiceName}“."
                : $"Preporučeno jer ste nedavno pregledali uslugu „{dominant.MedicalServiceName}“.";
        }

        return $"Preporučeno na osnovu vaših ranijih navika zakazivanja, {dayBosnian} {timeOfDayBosnian}.";
    }
}
