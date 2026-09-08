using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.People;
using ClinicNow.Services.Security;
using ClinicNow.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Tests.People;

/// <summary>
/// Correction letter item 3, doctor side: <see cref="DoctorService.BeforeDeleteAsync"/>
/// already deactivated the linked login (<c>user.IsActive = false</c>) - that only
/// blocks a *new* login. A JWT already in the archived doctor's hands stayed valid
/// until it naturally expired (JWT_EXPIRY_MINUTES). This covers the same
/// <see cref="User.TokensValidFromUtc"/> cutoff + cache-eviction fix as
/// PatientServiceTests' equivalent coverage. Doctor.UserId is non-nullable (unlike
/// Patient's), so there is no "no linked account" case to cover here.
/// </summary>
public class DoctorServiceTests
{
    private static DoctorService NewService(ClinicNowContext context, ITokenBlocklistService? tokenBlocklistService = null) =>
        new(context, TestContextFactory.CreateMapper(), new PasswordHasher(),
            TestContextFactory.CreateHttpContextAccessor(1), tokenBlocklistService ?? new RecordingTokenBlocklistService());

    [Fact]
    public async Task DeleteAsync_RevokesTheLinkedUserAccountsOutstandingTokens()
    {
        await using var context = TestContextFactory.CreateContext();
        var user = new User
        {
            Id = 600, Email = "token-cutoff-test-doctor@clinicnow.test", PasswordHash = "x",
            FirstName = "Token", LastName = "Doctor", IsActive = true, CreatedAtUtc = DateTime.UtcNow
        };
        context.Users.Add(user);
        context.Doctors.Add(new Doctor
        {
            Id = 600, UserId = 600, LocationId = 1, CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var blocklist = new RecordingTokenBlocklistService();
        var service = NewService(context, blocklist);

        var before = DateTime.UtcNow.AddSeconds(-1);
        await service.DeleteAsync(600);

        var reloadedUser = await context.Users.SingleAsync(u => u.Id == 600);

        Assert.False(reloadedUser.IsActive);
        Assert.NotNull(reloadedUser.TokensValidFromUtc);
        Assert.True(reloadedUser.TokensValidFromUtc >= before);

        // The cached cutoff has to be dropped too, or JWT validation keeps reading
        // the stale (null) value for up to the positive-cache lifetime.
        Assert.Contains(600, blocklist.InvalidatedUserIds);
    }

    private sealed class RecordingTokenBlocklistService : ITokenBlocklistService
    {
        public List<int> InvalidatedUserIds { get; } = [];

        public Task RevokeAsync(string jti, DateTime tokenExpiresAtUtc, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Archiving a doctor must not revoke an individual token.");

        public Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Archiving a doctor must not check the blocklist.");

        public Task<DateTime?> GetTokensValidFromUtcAsync(int userId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Archiving a doctor must not read the cutoff.");

        public void InvalidateTokensValidFrom(int userId) => InvalidatedUserIds.Add(userId);

        public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Archiving a doctor must not purge the blocklist.");
    }
}
