using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Database;
using ClinicNow.Services.People;
using ClinicNow.Services.Security;
using ClinicNow.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Tests.People;

/// <summary>
/// Review item 1: the admin user list was missing Staff accounts entirely -
/// there was no endpoint, screen, or way to create one short of inserting into
/// the database directly. Staff has no separate profile entity (unlike Doctor/
/// Patient), so <see cref="StaffService"/> operates on <see cref="ClinicNow.Services.Database.Entities.User"/>
/// rows directly, filtered to the Staff role.
/// </summary>
public class StaffServiceTests
{
    private const int SeededStaffUserId = 2; // staff@clinicnow.test
    private const int SeededDoctorUserId = 3; // doctor@clinicnow.test - must never show up as "Staff"

    private static (StaffService Service, RecordingTokenBlocklistService Blocklist) NewService(ClinicNowContext context)
    {
        var blocklist = new RecordingTokenBlocklistService();
        return (new StaffService(context, TestContextFactory.CreateMapper(), new PasswordHasher(), blocklist), blocklist);
    }

    [Fact]
    public async Task InsertAsync_CreatesALoginAccountWithTheStaffRole()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, _) = NewService(context);

        var created = await service.InsertAsync(new StaffInsertRequest
        {
            Email = "Novo.Osoblje@clinicnow.test",
            Password = "lozinka123",
            FirstName = "Lejla",
            LastName = "Novović",
            PhoneNumber = "+38761555555"
        });

        Assert.Equal("novo.osoblje@clinicnow.test", created.Email);
        Assert.Contains(Roles.Staff, created.Roles);

        var user = await context.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .SingleAsync(u => u.Email == "novo.osoblje@clinicnow.test");
        Assert.Single(user.UserRoles);
        Assert.Equal(Roles.Staff, user.UserRoles.Single().Role.Name);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task InsertAsync_RejectsADuplicateEmail()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, _) = NewService(context);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.InsertAsync(new StaffInsertRequest
            {
                Email = "staff@clinicnow.test", // already seeded
                Password = "lozinka123",
                FirstName = "Duplikat",
                LastName = "Osoblje"
            }));

        Assert.Contains("email", exception.Errors.Keys);
    }

    [Fact]
    public async Task GetPagedAsync_OnlyReturnsUsersWithTheStaffRole()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, _) = NewService(context);

        var result = await service.GetPagedAsync(new Model.SearchObjects.StaffSearchObject { PageSize = 100 });

        Assert.Contains(result.ResultList, u => u.Id == SeededStaffUserId);
        Assert.DoesNotContain(result.ResultList, u => u.Id == SeededDoctorUserId);
    }

    [Fact]
    public async Task DeleteAsync_DeactivatesRatherThanRemovingTheRow()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, blocklist) = NewService(context);

        var before = DateTime.UtcNow.AddSeconds(-1);
        await service.DeleteAsync(SeededStaffUserId);

        // Still there - Staff has no separate profile row for the base class
        // to Remove instead, and other tables hold real FKs to this User.
        var user = await context.Users.SingleAsync(u => u.Id == SeededStaffUserId);
        Assert.False(user.IsActive);
        Assert.NotNull(user.TokensValidFromUtc);
        Assert.True(user.TokensValidFromUtc >= before);
        Assert.Contains(SeededStaffUserId, blocklist.InvalidatedUserIds);
    }

    [Fact]
    public async Task DeleteAsync_RejectsAnIdThatIsNotStaff()
    {
        using var context = TestContextFactory.CreateContext();
        var (service, _) = NewService(context);

        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(SeededDoctorUserId));

        var doctorUser = await context.Users.SingleAsync(u => u.Id == SeededDoctorUserId);
        Assert.True(doctorUser.IsActive);
    }

    private sealed class RecordingTokenBlocklistService : ITokenBlocklistService
    {
        public List<int> InvalidatedUserIds { get; } = [];

        public Task RevokeAsync(string jti, DateTime tokenExpiresAtUtc, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Deactivating a staff account must not revoke an individual token.");

        public Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Deactivating a staff account must not check the blocklist.");

        public Task<DateTime?> GetTokensValidFromUtcAsync(int userId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Deactivating a staff account must not read the cutoff.");

        public void InvalidateTokensValidFrom(int userId) => InvalidatedUserIds.Add(userId);

        public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Deactivating a staff account must not purge the blocklist.");
    }
}
