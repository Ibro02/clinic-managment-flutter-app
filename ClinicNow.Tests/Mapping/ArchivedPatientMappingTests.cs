using ClinicNow.Model.Dto;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Tests.TestSupport;

namespace ClinicNow.Tests.Mapping;

/// <summary>
/// Review item C3: once a patient is archived (soft-deleted), the global
/// query filter makes any Include()-d Patient navigation come back null on
/// the entities that still reference it - the karton and its documents are
/// never themselves deleted, so they must still map to a DTO instead of
/// throwing a NullReferenceException on <c>src.Patient.FirstName</c> etc.
/// </summary>
public class ArchivedPatientMappingTests
{
    [Fact]
    public void MedicalRecord_WithNullPatient_MapsWithoutThrowing()
    {
        var mapper = TestContextFactory.CreateMapper();
        var record = new MedicalRecord
        {
            Id = 1,
            PatientId = 1,
            Patient = null!,
            Allergies = "Penicilin",
            Entries = [],
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var dto = mapper.Map<MedicalRecordDto>(record);

        Assert.Equal("Obrisani pacijent", dto.PatientFirstName);
        Assert.Null(dto.PatientAge);
        Assert.Null(dto.PatientGender);
        Assert.Equal("Penicilin", dto.Allergies);
    }

    [Fact]
    public void MedicalDocument_WithNullPatient_MapsWithoutThrowing()
    {
        var mapper = TestContextFactory.CreateMapper();
        var document = new MedicalDocument
        {
            Id = 1,
            PatientId = 1,
            Patient = null!,
            FileName = "nalaz.pdf",
            ContentType = "application/pdf",
            FileData = [1, 2, 3],
            FileSizeBytes = 3,
            UploadedByUser = new User { Id = 2, FirstName = "Staff", LastName = "Person" },
            CreatedAtUtc = DateTime.UtcNow
        };

        var dto = mapper.Map<MedicalDocumentDto>(document);

        Assert.Equal("Obrisani pacijent", dto.PatientName);
        Assert.Equal("Staff Person", dto.UploadedByName);
    }
}
