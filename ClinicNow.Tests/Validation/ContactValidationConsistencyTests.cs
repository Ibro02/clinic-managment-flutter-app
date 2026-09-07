using ClinicNow.Model.Common;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.People;
using ClinicNow.Services.Security;
using ClinicNow.Services.Users;
using ClinicNow.Services.Validation;
using ClinicNow.Tests.TestSupport;

namespace ClinicNow.Tests.Validation;

/// <summary>
/// Review item C17: the three flows that fill the same email/phone/name columns
/// disagreed about what they accept. Registration matched a real email pattern;
/// PatientService accepted anything containing an "@"; DoctorService checked
/// email *uniqueness* with no format check at all. Over-long values passed all
/// three and failed later as a SQL truncation error.
///
/// The point of these tests is not that a regex works - it is that the three
/// flows now answer the same question the same way, which is the only thing
/// that stops them drifting apart again.
/// </summary>
public class ContactValidationConsistencyTests
{
    // "user@" contains an at-sign - the entire old PatientService rule - but is
    // not a deliverable address, and registration always rejected it.
    private const string MalformedEmail = "user@";
    private const string MalformedPhone = "not-a-number";

    private static PatientService NewPatientService(ClinicNowContext context) =>
        new(context, TestContextFactory.CreateMapper(), TestContextFactory.CreateHttpContextAccessor(1));

    private static DoctorService NewDoctorService(ClinicNowContext context) =>
        new(context, TestContextFactory.CreateMapper(), new PasswordHasher(),
            TestContextFactory.CreateHttpContextAccessor(1));

    private static UserService NewUserService(ClinicNowContext context) =>
        new(context, TestContextFactory.CreateMapper(), new PasswordHasher(),
            new UnusedTokenService(), new UnusedTokenBlocklistService(),
            TestContextFactory.CreateHttpContextAccessor(1), new RecordingEmailPublisher());

    private static PatientInsertRequest PatientRequest(
        string? email = null, string? phone = null, string? personalId = null,
        string? address = null, string firstName = "Test") => new()
    {
        FirstName = firstName,
        LastName = "Pacijent",
        Gender = Gender.Female,
        Email = email,
        PhoneNumber = phone,
        PersonalIdNumber = personalId,
        Address = address
    };

    private static DoctorInsertRequest DoctorRequest(
        string email = "novi.doktor@clinicnow.test", string? phone = null) => new()
    {
        Email = email,
        Password = "test1234",
        FirstName = "Novi",
        LastName = "Doktor",
        PhoneNumber = phone,
        LocationId = 1,
        SpecializationIds = [1]
    };

    private static RegisterRequest NewRegisterRequest(
        string email = "novi@clinicnow.test", string? phone = null) => new()
    {
        Email = email,
        Password = "test1234",
        FirstName = "Novi",
        LastName = "Korisnik",
        PhoneNumber = phone
    };

    private static string TooLong(int maxLength) => new('x', maxLength + 1);

    /// <summary>
    /// The core claim of C17b: one malformed address, three flows, same verdict.
    /// Before this, only registration rejected it.
    /// </summary>
    [Fact]
    public async Task AllThreeFlowsRejectTheSameMalformedEmail()
    {
        await using var context = TestContextFactory.CreateContext();

        var patientError = await Assert.ThrowsAsync<ValidationException>(
            () => NewPatientService(context).InsertAsync(PatientRequest(email: MalformedEmail)));
        var doctorError = await Assert.ThrowsAsync<ValidationException>(
            () => NewDoctorService(context).InsertAsync(DoctorRequest(email: MalformedEmail)));
        var registerError = await Assert.ThrowsAsync<ValidationException>(
            () => NewUserService(context).RegisterAsync(NewRegisterRequest(email: MalformedEmail)));

        foreach (var error in new[] { patientError, doctorError, registerError })
        {
            Assert.True(error.Errors.ContainsKey("email"));
            Assert.Equal(ContactRules.EmailMessage, error.Errors["email"][0]);
        }
    }

    [Fact]
    public async Task AllThreeFlowsRejectTheSameMalformedPhone()
    {
        await using var context = TestContextFactory.CreateContext();

        var patientError = await Assert.ThrowsAsync<ValidationException>(
            () => NewPatientService(context).InsertAsync(PatientRequest(phone: MalformedPhone)));
        var doctorError = await Assert.ThrowsAsync<ValidationException>(
            () => NewDoctorService(context).InsertAsync(DoctorRequest(phone: MalformedPhone)));
        var registerError = await Assert.ThrowsAsync<ValidationException>(
            () => NewUserService(context).RegisterAsync(NewRegisterRequest(phone: MalformedPhone)));

        foreach (var error in new[] { patientError, doctorError, registerError })
        {
            Assert.True(error.Errors.ContainsKey("phoneNumber"));
            Assert.Equal(ContactRules.PhoneMessage, error.Errors["phoneNumber"][0]);
        }
    }

    /// <summary>
    /// Over-long values used to pass validation and fail in SQL as a truncation
    /// error - a 500 rather than a message under the field (rulebook §4).
    /// </summary>
    [Theory]
    [InlineData("address")]
    [InlineData("personalIdNumber")]
    [InlineData("firstName")]
    public async Task OverlongValuesAreRejectedByTheServiceRatherThanTheDatabase(string field)
    {
        await using var context = TestContextFactory.CreateContext();
        var request = field switch
        {
            "address" => PatientRequest(address: TooLong(ContactRules.MaxAddressLength)),
            "personalIdNumber" => PatientRequest(personalId: TooLong(ContactRules.MaxPersonalIdLength)),
            _ => PatientRequest(firstName: TooLong(ContactRules.MaxNameLength))
        };

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewPatientService(context).InsertAsync(request));

        Assert.True(error.Errors.ContainsKey(field));
    }

    /// <summary>
    /// Verified against the running API before this fix: an over-long
    /// LicenseNumber returned HTTP 500 with a DbUpdateException/SqlException,
    /// because the column is capped in EF but nothing checked it in the service.
    /// </summary>
    [Fact]
    public async Task OverlongLicenseNumberIsAValidationErrorNotADatabaseFailure()
    {
        await using var context = TestContextFactory.CreateContext();
        var request = DoctorRequest();
        request.LicenseNumber = TooLong(ContactRules.MaxLicenseNumberLength);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewDoctorService(context).InsertAsync(request));

        Assert.True(error.Errors.ContainsKey("licenseNumber"));
    }

    /// <summary>The rules tightened; they did not close the door on valid input.</summary>
    [Fact]
    public async Task WellFormedContactDetailsAreStillAccepted()
    {
        await using var context = TestContextFactory.CreateContext();

        var patient = await NewPatientService(context).InsertAsync(
            PatientRequest(email: "ime@primjer.com", phone: "+38761123456"));

        Assert.Equal("ime@primjer.com", patient.Email);
    }

    /// <summary>
    /// The doctor flow gained the most new rules, so it gets an explicit
    /// happy-path check - the negative tests alone would still pass if creation
    /// had been broken outright.
    /// </summary>
    [Fact]
    public async Task DoctorWithValidContactDetailsIsStillCreated()
    {
        await using var context = TestContextFactory.CreateContext();
        var request = DoctorRequest(phone: "+38761123456");
        request.LicenseNumber = "LIC-12345";
        request.Bio = "Specijalista sa deset godina iskustva.";

        var doctor = await NewDoctorService(context).InsertAsync(request);

        Assert.Equal("Novi Doktor", $"{doctor.FirstName} {doctor.LastName}");
    }

    /// <summary>A blank optional field is not the same as a malformed one.</summary>
    [Fact]
    public async Task BlankOptionalContactDetailsAreAccepted()
    {
        await using var context = TestContextFactory.CreateContext();

        var patient = await NewPatientService(context).InsertAsync(PatientRequest());

        Assert.Null(patient.Email);
    }

    // RegisterAsync never issues or revokes a token, so these exist only to
    // satisfy the constructor - and fail loudly if that ever stops being true.
    private sealed class UnusedTokenService : ITokenService
    {
        public CreatedToken CreateAccessToken(User user, IEnumerable<string> roles) =>
            throw new InvalidOperationException("Registration must not issue a token.");
    }

    private sealed class UnusedTokenBlocklistService : ITokenBlocklistService
    {
        public Task RevokeAsync(string jti, DateTime tokenExpiresAtUtc, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Registration must not revoke a token.");

        public Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Registration must not check the blocklist.");
    }
}
