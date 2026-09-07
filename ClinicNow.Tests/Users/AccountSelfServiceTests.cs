using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Security;
using ClinicNow.Services.Users;
using ClinicNow.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Tests.Users;

/// <summary>
/// Review item C7: the mobile profile screen and the password-reset flow.
///
/// The security-relevant claims are the ones worth pinning down - the edited
/// user comes from the JWT and not the request, a reset code is single-use and
/// expires, and the "forgot password" endpoint cannot be used to find out which
/// email addresses have accounts here. The seeded patient account is user 4.
/// </summary>
public class AccountSelfServiceTests
{
    private const int PatientUserId = 4;
    private const string PatientEmail = "patient@clinicnow.test";
    private const string SeededPassword = "test";

    private static (UserService Service, RecordingEmailPublisher Email) Build(ClinicNowContext context, int actingUserId = PatientUserId)
    {
        var email = new RecordingEmailPublisher();
        var service = new UserService(
            context,
            TestContextFactory.CreateMapper(),
            new PasswordHasher(),
            new UnusedTokenService(),
            new UnusedTokenBlocklistService(),
            TestContextFactory.CreateHttpContextAccessor(actingUserId, Roles.Patient),
            email);

        return (service, email);
    }

    /// <summary>Pulls the code out of the message the patient would have received.</summary>
    private static string CodeFrom(RecordingEmailPublisher email)
    {
        var body = Assert.Single(email.Published).Body;
        var line = body.Split('\n').First(l => l.Contains("kod za resetovanje", StringComparison.OrdinalIgnoreCase));
        return line[(line.IndexOf(':') + 1)..].Trim();
    }

    [Fact]
    public async Task Profile_edit_updates_the_token_holder_and_keeps_the_patient_chart_in_step()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, _) = Build(context);

        var updated = await service.UpdateCurrentUserAsync(new UpdateProfileRequest
        {
            FirstName = "Hana",
            LastName = "Pacijentić-Novi",
            PhoneNumber = "+38761999999",
            EmailRemindersEnabled = false
        });

        Assert.Equal(PatientUserId, updated.Id);
        Assert.Equal("Pacijentić-Novi", updated.LastName);
        Assert.False(updated.EmailRemindersEnabled);

        // The chart carries its own copy of these columns; leaving it behind
        // would show staff one name and the patient another.
        var patient = await context.Patients.IgnoreQueryFilters().SingleAsync(p => p.UserId == PatientUserId);
        Assert.Equal("Pacijentić-Novi", patient.LastName);
        Assert.Equal("+38761999999", patient.PhoneNumber);

        // Nobody else was touched - the id came from the token, not the body.
        var otherUser = await context.Users.SingleAsync(u => u.Id == 1);
        Assert.Equal("Administratorović", otherUser.LastName);
    }

    [Fact]
    public async Task Changing_your_own_password_requires_the_current_one()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, _) = Build(context);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.ChangeCurrentUserPasswordAsync(new ChangePasswordRequest
            {
                CurrentPassword = "not-the-password",
                NewPassword = "novaLozinka123",
                ConfirmNewPassword = "novaLozinka123"
            }));

        Assert.Contains("currentPassword", exception.Errors.Keys);

        var user = await context.Users.SingleAsync(u => u.Id == PatientUserId);
        Assert.True(new PasswordHasher().Verify(SeededPassword, user.PasswordHash));
    }

    [Fact]
    public async Task Changing_your_own_password_replaces_the_hash_when_the_current_one_matches()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, _) = Build(context);

        await service.ChangeCurrentUserPasswordAsync(new ChangePasswordRequest
        {
            CurrentPassword = SeededPassword,
            NewPassword = "novaLozinka123",
            ConfirmNewPassword = "novaLozinka123"
        });

        var user = await context.Users.SingleAsync(u => u.Id == PatientUserId);
        var hasher = new PasswordHasher();
        Assert.True(hasher.Verify("novaLozinka123", user.PasswordHash));
        Assert.False(hasher.Verify(SeededPassword, user.PasswordHash));
    }

    [Fact]
    public async Task Forgot_password_says_nothing_about_whether_the_email_is_registered()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, email) = Build(context);

        // No throw, no different behaviour a caller could observe - and, of
        // course, no mail to an address that isn't ours.
        await service.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = "nobody@example.test" });

        Assert.Empty(email.Published);
    }

    [Fact]
    public async Task A_reset_code_is_emailed_hashed_at_rest_and_redeemable_once()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, email) = Build(context);

        await service.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = PatientEmail });
        var code = CodeFrom(email);

        var stored = await context.Users.SingleAsync(u => u.Id == PatientUserId);
        Assert.NotNull(stored.PasswordResetTokenHash);
        // Never the code itself: a leaked database must not hand over live
        // reset codes (rulebook Part II §F).
        Assert.DoesNotContain(code, stored.PasswordResetTokenHash);
        Assert.NotNull(stored.PasswordResetTokenExpiresAtUtc);

        await service.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = PatientEmail,
            Code = code,
            NewPassword = "resetovana123",
            ConfirmNewPassword = "resetovana123"
        });

        var afterReset = await context.Users.SingleAsync(u => u.Id == PatientUserId);
        Assert.True(new PasswordHasher().Verify("resetovana123", afterReset.PasswordHash));
        Assert.Null(afterReset.PasswordResetTokenHash);
        Assert.Null(afterReset.PasswordResetTokenExpiresAtUtc);

        // Replaying the same email a second time must not work.
        var replay = await Assert.ThrowsAsync<ValidationException>(() =>
            service.ResetPasswordAsync(new ResetPasswordRequest
            {
                Email = PatientEmail,
                Code = code,
                NewPassword = "drugaLozinka123",
                ConfirmNewPassword = "drugaLozinka123"
            }));
        Assert.Contains("code", replay.Errors.Keys);
    }

    [Fact]
    public async Task An_expired_reset_code_is_refused()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, email) = Build(context);

        await service.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = PatientEmail });
        var code = CodeFrom(email);

        // Wind the expiry back rather than waiting half an hour.
        var user = await context.Users.SingleAsync(u => u.Id == PatientUserId);
        user.PasswordResetTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ResetPasswordAsync(new ResetPasswordRequest
            {
                Email = PatientEmail,
                Code = code,
                NewPassword = "resetovana123",
                ConfirmNewPassword = "resetovana123"
            }));

        var unchanged = await context.Users.SingleAsync(u => u.Id == PatientUserId);
        Assert.True(new PasswordHasher().Verify(SeededPassword, unchanged.PasswordHash));
    }

    [Fact]
    public async Task Changing_the_password_invalidates_any_outstanding_reset_code()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, email) = Build(context);

        await service.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = PatientEmail });
        var code = CodeFrom(email);

        await service.ChangeCurrentUserPasswordAsync(new ChangePasswordRequest
        {
            CurrentPassword = SeededPassword,
            NewPassword = "novaLozinka123",
            ConfirmNewPassword = "novaLozinka123"
        });

        // Someone who secured their account must not be reachable through a
        // code that was in flight at the time.
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ResetPasswordAsync(new ResetPasswordRequest
            {
                Email = PatientEmail,
                Code = code,
                NewPassword = "napadacevaLozinka123",
                ConfirmNewPassword = "napadacevaLozinka123"
            }));
    }

    [Fact]
    public async Task Two_reset_codes_are_not_the_same()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, email) = Build(context);

        await service.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = PatientEmail });
        var first = CodeFrom(email);
        email.Published.Clear();

        await service.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = PatientEmail });
        var second = CodeFrom(email);

        Assert.NotEqual(first, second);
        // Formatted for typing, from the reduced alphabet - not a raw GUID.
        Assert.Matches("^[A-Z2-9#]{4}-[A-Z2-9#]{4}-[A-Z2-9#]{4}$", second);
    }

    // UserService's constructor needs these; nothing under test here issues or
    // revokes a token, so they fail loudly if that ever changes.
    private sealed class UnusedTokenService : ITokenService
    {
        public CreatedToken CreateAccessToken(User user, IEnumerable<string> roles) =>
            throw new InvalidOperationException("Account self-service must not issue a token.");
    }

    private sealed class UnusedTokenBlocklistService : ITokenBlocklistService
    {
        public Task RevokeAsync(string jti, DateTime tokenExpiresAtUtc, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Account self-service must not revoke a token.");

        public Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Account self-service must not check the blocklist.");
    }
}
