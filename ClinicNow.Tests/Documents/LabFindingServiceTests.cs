using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services.Database;
using ClinicNow.Services.Documents;
using ClinicNow.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Tests.Documents;

/// <summary>
/// Review item C4: a lab finding is tied to both a patient and a specific
/// appointment, with the patient link derived server-side from the
/// appointment (never a separately-trusted client input), and reuses
/// <see cref="FileValidation"/> - the same MIME/magic-byte check
/// <c>MedicalDocumentService</c> uses - rather than a second implementation.
///
/// Uses the seeded Appointments (Id 1-5, see AppointmentConfiguration) rather
/// than hand-building the Doctor/MedicalService/Location FK graph a fresh
/// Appointment would need.
/// </summary>
public class LabFindingServiceTests
{
    // A genuine minimal PDF - same bytes LabFindingConfiguration's seed uses,
    // so a real signature check is exercised, not a placeholder.
    private const string ValidPdfBase64 =
        "JVBERi0xLjQKMSAwIG9iajw8L1R5cGUvQ2F0YWxvZy9QYWdlcyAyIDAgUj4+ZW5kb2JqCjIgMCBvYmo8PC9UeXBlL1BhZ2VzL0tpZHNbMyAwIFJdL0NvdW50IDE+PmVuZG9iagozIDAgb2JqPDwvVHlwZS9QYWdlL1BhcmVudCAyIDAgUi9NZWRpYUJveFswIDAgMjAwIDIwMF0+PmVuZG9iagp4cmVmCjAgNAowMDAwMDAwMDAwIDY1NTM1IGYgCnRyYWlsZXI8PC9TaXplIDQvUm9vdCAxIDAgUj4+CnN0YXJ0eHJlZgowCiUlRU9G";

    private static ClinicNowContext ContextWithAccessor(int userId, string role, out LabFindingService service)
    {
        var context = TestContextFactory.CreateContext();
        var accessor = TestContextFactory.CreateHttpContextAccessor(userId, role);
        service = new LabFindingService(context, TestContextFactory.CreateMapper(), accessor);
        return context;
    }

    [Fact]
    public async Task CreateAsync_DerivesPatientIdFromAppointment()
    {
        // Seeded Appointment 2 is Patient 2 / Doctor 1 - not the seeded
        // LabFinding's own Appointment 1 / Patient 1, so this proves the
        // patient link is actually read from the appointment, not hardcoded.
        await using var context = ContextWithAccessor(3, Roles.Doctor, out var service);

        var dto = await service.CreateAsync(new LabFindingInsertRequest
        {
            AppointmentId = 2,
            Result = "Uredan nalaz urina.",
            FileName = "nalaz-urin.pdf",
            ContentType = "application/pdf",
            FileBase64 = ValidPdfBase64
        });

        Assert.Equal(2, dto.PatientId);
        Assert.Equal(2, dto.AppointmentId);
        Assert.Equal("Uredan nalaz urina.", dto.Result);

        var entity = await context.LabFindings.SingleAsync(f => f.Id == dto.Id);
        Assert.Equal(2, entity.PatientId);
        Assert.Equal(3, entity.EnteredByUserId);
    }

    [Fact]
    public async Task CreateAsync_MissingResult_Throws()
    {
        await using var context = ContextWithAccessor(3, Roles.Doctor, out var service);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(new LabFindingInsertRequest
        {
            AppointmentId = 2,
            Result = "",
            FileName = "nalaz.pdf",
            ContentType = "application/pdf",
            FileBase64 = ValidPdfBase64
        }));

        Assert.Contains("result", ex.Errors.Keys);
    }

    [Fact]
    public async Task CreateAsync_UnknownAppointment_Throws()
    {
        await using var context = ContextWithAccessor(3, Roles.Doctor, out var service);

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(new LabFindingInsertRequest
        {
            AppointmentId = 999_999,
            Result = "Nalaz",
            FileName = "nalaz.pdf",
            ContentType = "application/pdf",
            FileBase64 = ValidPdfBase64
        }));
    }

    [Fact]
    public async Task CreateAsync_FileContentDoesNotMatchDeclaredType_Throws()
    {
        await using var context = ContextWithAccessor(3, Roles.Doctor, out var service);
        var notActuallyPdf = Convert.ToBase64String("not a real pdf"u8.ToArray());

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(new LabFindingInsertRequest
        {
            AppointmentId = 2,
            Result = "Nalaz",
            FileName = "nalaz.pdf",
            ContentType = "application/pdf",
            FileBase64 = notActuallyPdf
        }));
    }

    [Fact]
    public async Task GetPagedAsync_FilteredByAppointmentId_ReturnsOnlyThatAppointmentsFindings()
    {
        await using var context = ContextWithAccessor(3, Roles.Doctor, out var service);
        await service.CreateAsync(new LabFindingInsertRequest
        {
            AppointmentId = 2,
            Result = "Nalaz za termin 2",
            FileName = "nalaz.pdf",
            ContentType = "application/pdf",
            FileBase64 = ValidPdfBase64
        });

        // Seeded LabFinding Id=1 belongs to Appointment 1 - must not appear here.
        var result = await service.GetPagedAsync(new LabFindingSearchObject { AppointmentId = 2 });

        Assert.Single(result.ResultList);
        Assert.Equal(2, result.ResultList[0].AppointmentId);
    }

    [Fact]
    public async Task GetPagedAsync_FilteredBySearch_MatchesResultOrFileName()
    {
        await using var context = ContextWithAccessor(3, Roles.Doctor, out var service);
        await service.CreateAsync(new LabFindingInsertRequest
        {
            AppointmentId = 2,
            Result = "Povišen šećer u krvi.",
            FileName = "glukoza.pdf",
            ContentType = "application/pdf",
            FileBase64 = ValidPdfBase64
        });

        // Seeded LabFinding Id=1 ("Kompletna krvna slika...", nalaz-kks.pdf)
        // must not match either search term below.
        var byResult = await service.GetPagedAsync(new LabFindingSearchObject { Search = "šećer" });
        Assert.Single(byResult.ResultList);
        Assert.Equal("glukoza.pdf", byResult.ResultList[0].FileName);

        var byFileName = await service.GetPagedAsync(new LabFindingSearchObject { Search = "glukoza" });
        Assert.Single(byFileName.ResultList);
        Assert.Equal("glukoza.pdf", byFileName.ResultList[0].FileName);
    }

    [Fact]
    public async Task GetPagedAsync_PatientRole_OnlySeesOwnFindings()
    {
        // Patient 1's User is Id=4 (Hana, per PatientConfiguration/AddIdentity seed).
        await using var context = ContextWithAccessor(4, Roles.Patient, out var service);

        var result = await service.GetPagedAsync(new LabFindingSearchObject { PatientId = 2 }); // attempted spoof

        // Only ever the caller's own (Patient 1's) findings, regardless of the
        // PatientId filter the client sent.
        Assert.All(result.ResultList, f => Assert.Equal(1, f.PatientId));
    }

    [Fact]
    public async Task GetFileForDownloadAsync_PatientCannotAccessAnotherPatientsFinding()
    {
        await using var context = ContextWithAccessor(4, Roles.Patient, out var service); // Patient 1's user

        // Seeded LabFinding Id=1 belongs to Patient 1 - so create one for Patient 2 first.
        var adminAccessor = TestContextFactory.CreateHttpContextAccessor(1, Roles.Administrator);
        var adminService = new LabFindingService(context, TestContextFactory.CreateMapper(), adminAccessor);
        var created = await adminService.CreateAsync(new LabFindingInsertRequest
        {
            AppointmentId = 2, // Patient 2
            Result = "Tuđi nalaz",
            FileName = "nalaz.pdf",
            ContentType = "application/pdf",
            FileBase64 = ValidPdfBase64
        });

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetFileForDownloadAsync(created.Id));
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes()
    {
        await using var context = ContextWithAccessor(3, Roles.Doctor, out var service);
        var created = await service.CreateAsync(new LabFindingInsertRequest
        {
            AppointmentId = 2,
            Result = "Nalaz",
            FileName = "nalaz.pdf",
            ContentType = "application/pdf",
            FileBase64 = ValidPdfBase64
        });

        await service.DeleteAsync(created.Id);

        var directQuery = await context.LabFindings.Where(f => f.Id == created.Id).ToListAsync();
        Assert.Empty(directQuery); // global soft-delete filter excludes it

        var stillInDb = await context.LabFindings.IgnoreQueryFilters().SingleAsync(f => f.Id == created.Id);
        Assert.True(stillInDb.IsDeleted);
    }
}
