using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Messaging;
using ClinicNow.Services.Security;
using ClinicNow.Services.Users;
using ClinicNow.Tests.TestSupport;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Tests.Users;

/// <summary>
/// The credential endpoints answer "no such account" and "wrong password" with a
/// deliberately identical message, so that neither reveals whether an address has
/// an account here - which, for a clinic, is itself patient information.
///
/// That message is only half the defence. The work each path performs has to match
/// too: BCrypt at work factor 12 costs a couple of hundred milliseconds, so a code
/// path that skips it for unknown accounts answers roughly a hundred times faster
/// and re-creates the very oracle the wording removes.
///
/// These tests assert on *work performed* rather than wall-clock time - a counting
/// hasher, not a stopwatch - because a timing assertion on a shared CI machine is
/// flaky, while "was the hash actually verified" is exactly the property that makes
/// the durations match and is deterministic to check.
/// </summary>
public class CredentialTimingTests
{
    private const string PatientEmail = "patient@clinicnow.test";

    /// <summary>Counts verifications while behaving exactly like the real hasher.</summary>
    private sealed class CountingPasswordHasher : IPasswordHasher
    {
        private readonly PasswordHasher _inner = new();

        public int VerifyCalls { get; private set; }
        public int HashCalls { get; private set; }

        public string Hash(string password)
        {
            HashCalls++;
            return _inner.Hash(password);
        }

        public bool Verify(string password, string passwordHash)
        {
            VerifyCalls++;
            return _inner.Verify(password, passwordHash);
        }
    }

    private static (UserService Service, CountingPasswordHasher Hasher, RecordingEmailPublisher Email) Build(
        ClinicNowContext context)
    {
        var hasher = new CountingPasswordHasher();
        var email = new RecordingEmailPublisher();
        var service = new UserService(
            context,
            TestContextFactory.CreateMapper(),
            hasher,
            new ThrowawayTokenService(),
            new ThrowawayBlocklist(),
            TestContextFactory.CreateHttpContextAccessor(4, Roles.Patient),
            email);

        return (service, hasher, email);
    }

    [Fact]
    public async Task Login_verifies_a_hash_whether_or_not_the_account_exists()
    {
        using var knownContext = TestContextFactory.CreateContext();
        var (knownService, knownHasher, _) = Build(knownContext);

        await Assert.ThrowsAsync<AuthenticationException>(() => knownService.LoginAsync(
            new LoginRequest { Email = PatientEmail, Password = "the-wrong-password" }));

        using var unknownContext = TestContextFactory.CreateContext();
        var (unknownService, unknownHasher, _) = Build(unknownContext);

        await Assert.ThrowsAsync<AuthenticationException>(() => unknownService.LoginAsync(
            new LoginRequest { Email = "nobody@nowhere.test", Password = "the-wrong-password" }));

        // The unknown-account path used to short-circuit past Verify entirely,
        // returning in ~2ms against ~250ms for a real account.
        Assert.Equal(1, knownHasher.VerifyCalls);
        Assert.Equal(1, unknownHasher.VerifyCalls);
    }

    [Fact]
    public async Task Forgot_password_hashes_a_code_whether_or_not_the_account_exists()
    {
        using var knownContext = TestContextFactory.CreateContext();
        var (knownService, knownHasher, knownEmail) = Build(knownContext);
        await knownService.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = PatientEmail });

        using var unknownContext = TestContextFactory.CreateContext();
        var (unknownService, unknownHasher, unknownEmail) = Build(unknownContext);
        await unknownService.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = "nobody@nowhere.test" });

        // Both paths pay for one BCrypt hash. Only the real account gets the mail.
        Assert.Equal(1, knownHasher.HashCalls);
        Assert.Equal(1, unknownHasher.HashCalls);
        Assert.Single(knownEmail.Published);
        Assert.Empty(unknownEmail.Published);
    }

    [Fact]
    public async Task Reset_password_verifies_a_hash_whether_or_not_a_reset_is_pending()
    {
        // A real account that never asked for a reset: there is no stored code to
        // compare against, which is the branch that used to return early.
        using var noResetContext = TestContextFactory.CreateContext();
        var (noResetService, noResetHasher, _) = Build(noResetContext);

        await Assert.ThrowsAsync<ValidationException>(() => noResetService.ResetPasswordAsync(
            new ResetPasswordRequest
            {
                Email = PatientEmail,
                Code = "AAAA-BBBB-CCCC",
                NewPassword = "novaLozinka123",
                ConfirmNewPassword = "novaLozinka123"
            }));

        using var pendingContext = TestContextFactory.CreateContext();
        var (pendingService, pendingHasher, _) = Build(pendingContext);
        await pendingService.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = PatientEmail });
        var hashCallsAfterRequest = pendingHasher.VerifyCalls;

        await Assert.ThrowsAsync<ValidationException>(() => pendingService.ResetPasswordAsync(
            new ResetPasswordRequest
            {
                Email = PatientEmail,
                Code = "AAAA-BBBB-CCCC",
                NewPassword = "novaLozinka123",
                ConfirmNewPassword = "novaLozinka123"
            }));

        Assert.Equal(1, noResetHasher.VerifyCalls);
        Assert.Equal(hashCallsAfterRequest + 1, pendingHasher.VerifyCalls);
    }

    [Fact]
    public async Task An_absurdly_long_password_is_refused_before_any_hashing_happens()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, hasher, _) = Build(context);

        await Assert.ThrowsAsync<AuthenticationException>(() => service.LoginAsync(new LoginRequest
        {
            Email = PatientEmail,
            Password = new string('x', 200_000)
        }));

        // BCrypt reads only the first 72 bytes, so hashing a 200 KB string buys
        // nothing and just burns work-factor-12 CPU on an anonymous endpoint.
        Assert.Equal(0, hasher.VerifyCalls);
    }

    private sealed class ThrowawayTokenService : ITokenService
    {
        public CreatedToken CreateAccessToken(User user, IEnumerable<string> roles) =>
            new("unused", DateTime.UtcNow.AddHours(1));
    }

    private sealed class ThrowawayBlocklist : ITokenBlocklistService
    {
        public Task RevokeAsync(string jti, DateTime tokenExpiresAtUtc, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<DateTime?> GetTokensValidFromUtcAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DateTime?>(null);

        public void InvalidateTokensValidFrom(int userId) { }

        public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
