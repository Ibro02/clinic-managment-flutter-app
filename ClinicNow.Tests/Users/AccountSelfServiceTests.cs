using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Localization;
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
    private const int AdministratorUserId = 1;
    private const int StaffUserId = 2;
    private const string SeededPassword = "test";

    private static (UserService Service, RecordingEmailPublisher Email) Build(ClinicNowContext context, int actingUserId = PatientUserId)
    {
        var (service, email, _, _) = BuildWithSpies(context, actingUserId);
        return (service, email);
    }

    /// <summary>
    /// Same wiring as <see cref="Build"/>, but hands back the token/blocklist spies
    /// for the tests that assert on session invalidation.
    /// </summary>
    private static (UserService Service, RecordingEmailPublisher Email, StubTokenService Tokens, RecordingTokenBlocklistService Blocklist)
        BuildWithSpies(ClinicNowContext context, int actingUserId = PatientUserId)
    {
        var email = new RecordingEmailPublisher();
        var tokens = new StubTokenService();
        var blocklist = new RecordingTokenBlocklistService();
        var service = new UserService(
            context,
            TestContextFactory.CreateMapper(),
            new PasswordHasher(),
            tokens,
            blocklist,
            TestContextFactory.CreateHttpContextAccessor(actingUserId, Roles.Patient),
            email);

        return (service, email, tokens, blocklist);
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

    /// <summary>
    /// Review item 7: the prijava's "jezik aplikacije" setting must have a real
    /// backend effect, not just persist a flag nobody reads - PreferredLanguage
    /// is what <see cref="ClinicNow.Model.Localization.PatientMessages"/> reads
    /// when rendering every notification/email this account later receives.
    /// </summary>
    [Fact]
    public async Task Profile_edit_persists_the_chosen_language()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, _) = Build(context);

        var updated = await service.UpdateCurrentUserAsync(new UpdateProfileRequest
        {
            FirstName = "Hana",
            LastName = "Pacijentić",
            EmailRemindersEnabled = true,
            PreferredLanguage = PatientLanguage.English
        });

        Assert.Equal(PatientLanguage.English, updated.PreferredLanguage);

        var user = await context.Users.SingleAsync(u => u.Id == PatientUserId);
        Assert.Equal(PatientLanguage.English, user.PreferredLanguage);
    }

    /// <summary>
    /// A stray/unsupported value must be rejected server-side rather than
    /// silently falling back to Bosnian - the client could otherwise think the
    /// change took effect when nothing actually changed.
    /// </summary>
    [Fact]
    public async Task Profile_edit_rejects_an_unsupported_language()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, _) = Build(context);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.UpdateCurrentUserAsync(new UpdateProfileRequest
            {
                FirstName = "Hana",
                LastName = "Pacijentić",
                EmailRemindersEnabled = true,
                PreferredLanguage = "de"
            }));

        Assert.Contains("preferredLanguage", exception.Errors.Keys);

        var user = await context.Users.SingleAsync(u => u.Id == PatientUserId);
        Assert.Equal(PatientLanguage.Bosnian, user.PreferredLanguage);
    }

    /// <summary>
    /// Review item 2: "Administrator treba imati SVE privilegije, pa čak da i
    /// sam sebi promjeni mail" - AdminUpdateEmailAsync is the one place email
    /// becomes editable at all, and it must work on the caller's own account
    /// too, not just other users'.
    /// </summary>
    [Fact]
    public async Task Admin_can_change_their_own_email()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, _) = Build(context, actingUserId: AdministratorUserId);

        var updated = await service.AdminUpdateEmailAsync(
            AdministratorUserId, new AdminUpdateEmailRequest { Email = "Nova.Adresa@clinicnow.test" });

        // Normalized the same way registration/login normalize it.
        Assert.Equal("nova.adresa@clinicnow.test", updated.Email);

        var user = await context.Users.SingleAsync(u => u.Id == AdministratorUserId);
        Assert.Equal("nova.adresa@clinicnow.test", user.Email);
    }

    [Fact]
    public async Task Admin_can_change_another_users_email()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, _) = Build(context, actingUserId: AdministratorUserId);

        await service.AdminUpdateEmailAsync(
            StaffUserId, new AdminUpdateEmailRequest { Email = "staff-novi@clinicnow.test" });

        var staffUser = await context.Users.SingleAsync(u => u.Id == StaffUserId);
        Assert.Equal("staff-novi@clinicnow.test", staffUser.Email);
    }

    [Fact]
    public async Task Admin_email_change_is_rejected_when_the_address_is_already_taken()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, _) = Build(context, actingUserId: AdministratorUserId);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.AdminUpdateEmailAsync(AdministratorUserId, new AdminUpdateEmailRequest { Email = PatientEmail }));

        Assert.Contains("email", exception.Errors.Keys);

        var admin = await context.Users.SingleAsync(u => u.Id == AdministratorUserId);
        Assert.Equal("administrator@clinicnow.test", admin.Email);
    }

    [Fact]
    public async Task Admin_email_change_is_rejected_when_malformed()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, _) = Build(context, actingUserId: AdministratorUserId);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.AdminUpdateEmailAsync(AdministratorUserId, new AdminUpdateEmailRequest { Email = "not-an-email" }));

        Assert.Contains("email", exception.Errors.Keys);
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
    public async Task Changing_your_own_password_ends_every_session_that_predates_it()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, _, tokens, blocklist) = BuildWithSpies(context);

        var before = DateTime.UtcNow.AddSeconds(-1);

        var result = await service.ChangeCurrentUserPasswordAsync(new ChangePasswordRequest
        {
            CurrentPassword = SeededPassword,
            NewPassword = "novaLozinka123",
            ConfirmNewPassword = "novaLozinka123"
        });

        var user = await context.Users.SingleAsync(u => u.Id == PatientUserId);

        // The cutoff is what actually invalidates tokens already in the wild: JWT
        // validation refuses anything issued before it.
        Assert.NotNull(user.TokensValidFromUtc);
        Assert.True(user.TokensValidFromUtc >= before);

        // The cached cutoff has to be dropped, or validation would keep reading the
        // old value and keep honouring the tokens this was meant to kill.
        Assert.Contains(PatientUserId, blocklist.InvalidatedUserIds);

        // ...and the caller gets a replacement, so the password change does not sign
        // them out of the session they just authenticated with.
        Assert.Equal(1, tokens.CallCount);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
    }

    [Fact]
    public async Task Password_reset_ends_every_session_but_issues_no_token()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, email, tokens, blocklist) = BuildWithSpies(context);

        await service.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = PatientEmail });
        var code = CodeFrom(email);

        await service.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = PatientEmail,
            Code = code,
            NewPassword = "resetovana123",
            ConfirmNewPassword = "resetovana123"
        });

        var user = await context.Users.SingleAsync(u => u.Id == PatientUserId);

        // A reset is how a compromised account is recovered, so the attacker's
        // existing token must stop working - not merely their ability to sign in again.
        Assert.NotNull(user.TokensValidFromUtc);
        Assert.Contains(PatientUserId, blocklist.InvalidatedUserIds);

        // Unlike a password change, the caller here was never signed in, so there is
        // nobody to hand a replacement token to.
        Assert.Equal(0, tokens.CallCount);
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
    /// <summary>
    /// Issues a recognizable stand-in token.
    ///
    /// This used to throw on any call, asserting that account self-service never
    /// mints a token. That is no longer the contract: changing your own password now
    /// invalidates every token issued before it, so the operation has to hand back a
    /// replacement or it would sign the user out of the session they just
    /// authenticated with. Password *reset* still issues nothing - the caller is not
    /// signed in - and <see cref="Password_reset_does_not_issue_a_token"/> pins that.
    /// </summary>
    private sealed class StubTokenService : ITokenService
    {
        public int CallCount { get; private set; }

        public CreatedToken CreateAccessToken(User user, IEnumerable<string> roles)
        {
            CallCount++;
            return new CreatedToken($"replacement-token-for-{user.Id}", DateTime.UtcNow.AddHours(1));
        }
    }

    private sealed class RecordingTokenBlocklistService : ITokenBlocklistService
    {
        public List<int> InvalidatedUserIds { get; } = [];

        public Task RevokeAsync(string jti, DateTime tokenExpiresAtUtc, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Account self-service must not revoke an individual token.");

        public Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Account self-service must not check the blocklist.");

        public Task<DateTime?> GetTokensValidFromUtcAsync(int userId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Account self-service must not read the cutoff.");

        public void InvalidateTokensValidFrom(int userId) => InvalidatedUserIds.Add(userId);

        public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Account self-service must not purge the blocklist.");
    }
}
