using ClinicNow.Model.Common;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Records;
using ClinicNow.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Tests.Records;

/// <summary>
/// Review item C11: <c>UpdateEntryAsync</c>/<c>DeleteEntryAsync</c> must bump
/// the karton's <c>UpdatedAtUtc</c> (only <c>AddEntryAsync</c> did before),
/// <c>DeleteEntryAsync</c> must archive an entry rather than physically
/// removing it, every mutation must leave a who/when/what audit row, and
/// <c>Diagnosis</c> is now its own required structured field.
///
/// Uses a dedicated patient/karton per test rather than the seeded Patient 1/
/// MedicalRecord 1 - same reasoning as PatientServiceTests, so tests don't
/// collide with the seeded MedicalRecordEntry/MedicalRecordAuditLog rows.
/// </summary>
public class MedicalRecordServiceTests
{
    private const int DoctorUserId = 3; // doctor@clinicnow.test, matches the identity seed

    private static async Task<(ClinicNowContext Context, MedicalRecordService Service, MedicalRecord Record)> SetUpAsync(int patientId, int recordId)
    {
        var context = TestContextFactory.CreateContext();
        context.Patients.Add(new Patient
        {
            Id = patientId,
            FirstName = "Test",
            LastName = $"Patient{patientId}",
            Gender = Gender.Female,
            CreatedAtUtc = DateTime.UtcNow
        });
        var record = new MedicalRecord
        {
            Id = recordId,
            PatientId = patientId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        context.MedicalRecords.Add(record);
        await context.SaveChangesAsync();

        var accessor = TestContextFactory.CreateHttpContextAccessor(DoctorUserId, Roles.Doctor);
        var service = new MedicalRecordService(context, TestContextFactory.CreateMapper(), accessor);
        return (context, service, record);
    }

    [Fact]
    public async Task AddEntryAsync_StoresDiagnosisAndBumpsParentTimestampAndLogsAudit()
    {
        var (context, service, record) = await SetUpAsync(600, 600);
        var originalUpdatedAt = record.UpdatedAtUtc;

        var dto = await service.AddEntryAsync(600, new MedicalRecordEntryInsertRequest
        {
            EntryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Diagnosis = "J06.9 - Akutna infekcija gornjih disajnih puteva",
            Treatment = "Antibiotska terapija",
            Description = "Propisan antibiotik na 7 dana."
        });

        Assert.Single(dto.Entries);
        Assert.Equal("J06.9 - Akutna infekcija gornjih disajnih puteva", dto.Entries[0].Diagnosis);

        var reloadedRecord = await context.MedicalRecords.SingleAsync(r => r.Id == 600);
        Assert.True(reloadedRecord.UpdatedAtUtc > originalUpdatedAt);

        var auditLogs = await context.Set<MedicalRecordAuditLog>().Where(l => l.MedicalRecordId == 600).ToListAsync();
        Assert.Single(auditLogs);
        Assert.Equal(MedicalRecordAuditAction.EntryAdded, auditLogs[0].Action);
        Assert.Equal(DoctorUserId, auditLogs[0].ActingUserId);
    }

    [Fact]
    public async Task AddEntryAsync_MissingDiagnosis_Throws()
    {
        var (_, service, _) = await SetUpAsync(601, 601);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.AddEntryAsync(601, new MedicalRecordEntryInsertRequest
        {
            EntryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Diagnosis = "",
            Treatment = "Tretman",
            Description = "Opis"
        }));

        Assert.Contains("diagnosis", ex.Errors.Keys);
    }

    /// <summary>The reviewer's literal complaint: UpdateEntryAsync did not refresh the parent's UpdatedAtUtc.</summary>
    [Fact]
    public async Task UpdateEntryAsync_BumpsParentTimestampAndLogsAudit()
    {
        var (context, service, record) = await SetUpAsync(602, 602);
        var created = await service.AddEntryAsync(602, new MedicalRecordEntryInsertRequest
        {
            EntryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Diagnosis = "Prvobitna dijagnoza",
            Treatment = "Prvobitni tretman",
            Description = "Prvobitni opis"
        });
        var entryId = created.Entries[0].Id;

        var afterCreateUpdatedAt = (await context.MedicalRecords.SingleAsync(r => r.Id == 602)).UpdatedAtUtc;
        await Task.Delay(10); // ensure a measurable timestamp difference

        var dto = await service.UpdateEntryAsync(entryId, new MedicalRecordEntryUpdateRequest
        {
            EntryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Diagnosis = "Ispravljena dijagnoza",
            Treatment = "Ispravljeni tretman",
            Description = "Ispravljeni opis"
        });

        Assert.Equal("Ispravljena dijagnoza", dto.Entries[0].Diagnosis);

        var reloadedRecord = await context.MedicalRecords.SingleAsync(r => r.Id == 602);
        Assert.True(reloadedRecord.UpdatedAtUtc > afterCreateUpdatedAt);

        var auditLogs = await context.Set<MedicalRecordAuditLog>()
            .Where(l => l.MedicalRecordId == 602 && l.Action == MedicalRecordAuditAction.EntryUpdated)
            .ToListAsync();
        Assert.Single(auditLogs);
    }

    /// <summary>The reviewer's literal complaint: DeleteEntryAsync physically removed the row with no history.</summary>
    [Fact]
    public async Task DeleteEntryAsync_SoftDeletesAndBumpsParentTimestampAndLogsAudit()
    {
        var (context, service, record) = await SetUpAsync(603, 603);
        var created = await service.AddEntryAsync(603, new MedicalRecordEntryInsertRequest
        {
            EntryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Diagnosis = "Dijagnoza",
            Treatment = "Tretman",
            Description = "Opis"
        });
        var entryId = created.Entries[0].Id;

        var afterCreateUpdatedAt = (await context.MedicalRecords.SingleAsync(r => r.Id == 603)).UpdatedAtUtc;
        await Task.Delay(10);

        await service.DeleteEntryAsync(entryId);

        // Row still physically exists in the database - not a hard delete.
        var stillInDb = await context.MedicalRecordEntries.IgnoreQueryFilters().SingleOrDefaultAsync(e => e.Id == entryId);
        Assert.NotNull(stillInDb);
        Assert.True(stillInDb!.IsDeleted);
        Assert.NotNull(stillInDb.DeletedAtUtc);

        // A direct query already proves the global filter is registered and
        // excludes it (confirmed separately). Whether it also disappears from
        // MedicalRecord.Entries specifically via GetByPatientIdAsync's
        // Include(r => r.Entries) is *not* asserted here: EF Core's InMemory
        // provider does not apply a child entity's query filter through an
        // Include()'d collection navigation the way a real relational provider
        // does (confirmed by direct comparison - a plain MedicalRecordEntries
        // query correctly excludes the row, the same query reached via
        // Include() from MedicalRecord does not). Same class of InMemory
        // limitation as C1's Serializable-transaction gap - verified live
        // against SQL Server instead (see GOALS.md).
        var directQuery = await context.MedicalRecordEntries.Where(e => e.MedicalRecordId == 603).ToListAsync();
        Assert.Empty(directQuery);

        var reloadedRecord = await context.MedicalRecords.SingleAsync(r => r.Id == 603);
        Assert.True(reloadedRecord.UpdatedAtUtc > afterCreateUpdatedAt);

        var auditLogs = await context.Set<MedicalRecordAuditLog>()
            .Where(l => l.MedicalRecordId == 603 && l.Action == MedicalRecordAuditAction.EntryDeleted)
            .ToListAsync();
        Assert.Single(auditLogs);
    }

    [Fact]
    public async Task AppendNotesAsync_LogsAudit()
    {
        var (context, service, _) = await SetUpAsync(604, 604);

        await service.AppendNotesAsync(604, new MedicalRecordAppendNotesRequest { AllergiesToAppend = "Penicilin" });

        var auditLogs = await context.Set<MedicalRecordAuditLog>()
            .Where(l => l.MedicalRecordId == 604 && l.Action == MedicalRecordAuditAction.NotesAppended)
            .ToListAsync();
        Assert.Single(auditLogs);
    }

    [Fact]
    public async Task ReplaceNotesAsync_LogsAudit()
    {
        var (context, service, _) = await SetUpAsync(605, 605);

        await service.ReplaceNotesAsync(605, new MedicalRecordUpdateNotesRequest { Allergies = "Penicilin" });

        var auditLogs = await context.Set<MedicalRecordAuditLog>()
            .Where(l => l.MedicalRecordId == 605 && l.Action == MedicalRecordAuditAction.NotesReplaced)
            .ToListAsync();
        Assert.Single(auditLogs);
    }
}
