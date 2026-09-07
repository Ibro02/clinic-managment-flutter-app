using ClinicNow.Model.Exceptions;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Documents;
using ClinicNow.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Tests.Documents;

/// <summary>
/// Minimum-necessary access to patient documentation.
///
/// Holding the Doctor role used to be sufficient to read - and download - the
/// medical documents of every patient in the clinic, with no treating
/// relationship of any kind required. For medical records that is precisely the
/// access-control gap the principle exists to close, so a doctor is now narrowed
/// to patients they have actually seen. Administrator and Staff are deliberately
/// not narrowed: running the clinic's records is their job.
///
/// Seeded fixtures this relies on (see the *Configuration classes):
///   Doctor 1 = User 3, has appointments with Patient 1 and Patient 2
///   Doctor 2 = User 5, has appointments with Patient 1 only
///   MedicalDocument 1 belongs to Patient 1
/// So Doctor 2 + a Patient 2 document is the "never treated them" case.
/// </summary>
public class MedicalDocumentAccessTests
{
    private const int DoctorOneUserId = 3;
    private const int DoctorTwoUserId = 5;
    private const int StaffUserId = 2;

    private static MedicalDocumentService ServiceFor(ClinicNowContext context, int userId, string role) =>
        new(context, TestContextFactory.CreateMapper(), TestContextFactory.CreateHttpContextAccessor(userId, role));

    /// <summary>A document for Patient 2, who Doctor 2 has never had an appointment with.</summary>
    private static async Task<int> AddPatientTwoDocumentAsync(ClinicNowContext context)
    {
        var document = new MedicalDocument
        {
            PatientId = 2,
            FileName = "nalaz-pacijent-2.pdf",
            ContentType = "application/pdf",
            FileData = [0x25, 0x50, 0x44, 0x46],
            FileSizeBytes = 4,
            UploadedByUserId = StaffUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.MedicalDocuments.Add(document);
        await context.SaveChangesAsync();
        return document.Id;
    }

    [Fact]
    public async Task A_doctor_cannot_download_a_document_for_a_patient_they_never_treated()
    {
        await using var context = TestContextFactory.CreateContext();
        var documentId = await AddPatientTwoDocumentAsync(context);

        var service = ServiceFor(context, DoctorTwoUserId, Roles.Doctor);

        var refused = await Assert.ThrowsAsync<ForbiddenException>(
            () => service.GetFileForDownloadAsync(documentId));

        Assert.Contains("niste liječili", refused.Message);
    }

    [Fact]
    public async Task A_doctor_can_download_a_document_for_a_patient_they_have_treated()
    {
        await using var context = TestContextFactory.CreateContext();
        var documentId = await AddPatientTwoDocumentAsync(context);

        // Doctor 1 has a non-cancelled appointment with Patient 2.
        var service = ServiceFor(context, DoctorOneUserId, Roles.Doctor);

        var document = await service.GetFileForDownloadAsync(documentId);

        Assert.Equal(documentId, document.Id);
    }

    [Fact]
    public async Task Staff_are_not_narrowed_to_a_treating_relationship()
    {
        await using var context = TestContextFactory.CreateContext();
        var documentId = await AddPatientTwoDocumentAsync(context);

        var service = ServiceFor(context, StaffUserId, Roles.Staff);

        var document = await service.GetFileForDownloadAsync(documentId);

        Assert.Equal(documentId, document.Id);
    }

    [Fact]
    public async Task The_document_list_hides_what_the_download_would_refuse()
    {
        await using var context = TestContextFactory.CreateContext();
        var offLimitsId = await AddPatientTwoDocumentAsync(context);

        var doctorTwo = ServiceFor(context, DoctorTwoUserId, Roles.Doctor);
        var visible = await doctorTwo.GetPagedAsync(new MedicalDocumentSearchObject { PageSize = 100 });

        // The list and the download have to agree, or the UI offers a row that
        // errors on click.
        Assert.DoesNotContain(visible.ResultList, d => d.Id == offLimitsId);

        var staff = ServiceFor(context, StaffUserId, Roles.Staff);
        var allDocuments = await staff.GetPagedAsync(new MedicalDocumentSearchObject { PageSize = 100 });
        Assert.Contains(allDocuments.ResultList, d => d.Id == offLimitsId);
    }

    [Fact]
    public async Task A_patient_id_in_the_query_string_cannot_widen_a_doctors_access()
    {
        await using var context = TestContextFactory.CreateContext();
        var offLimitsId = await AddPatientTwoDocumentAsync(context);

        var doctorTwo = ServiceFor(context, DoctorTwoUserId, Roles.Doctor);

        // Explicitly asking for Patient 2 must not bypass the treating filter.
        var result = await doctorTwo.GetPagedAsync(
            new MedicalDocumentSearchObject { PatientId = 2, PageSize = 100 });

        Assert.DoesNotContain(result.ResultList, d => d.Id == offLimitsId);
    }
}
