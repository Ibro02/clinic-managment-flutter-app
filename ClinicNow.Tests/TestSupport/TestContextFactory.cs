using System.Security.Claims;
using ClinicNow.Services.Database;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNow.Tests.TestSupport;

/// <summary>
/// Builds an isolated, seeded <see cref="ClinicNowContext"/> per test via EF Core's
/// InMemory provider. The model's <c>HasData</c> seed (Users/Doctors/
/// Specializations/WorkingHours/... - the same rows `docker-compose up` seeds into
/// SQL Server) is applied automatically by <c>EnsureCreated</c>, so tests can build
/// on the real seeded Doctor 1 (Opća medicina + Dermatologija, Mon-Fri 08:00-16:00)
/// instead of re-constructing the whole FK graph by hand.
///
/// Each context gets a unique database name so tests never see each other's writes.
/// </summary>
public static class TestContextFactory
{
    private static readonly TypeAdapterConfig MapperConfig = BuildMapperConfig();

    private static TypeAdapterConfig BuildMapperConfig()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(ClinicNowContext).Assembly);
        return config;
    }

    public static ClinicNowContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ClinicNowContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new ClinicNowContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    // None of the current IRegister mapping configs resolve anything via DI - an
    // empty provider is enough to satisfy ServiceMapper's constructor.
    private static readonly IServiceProvider EmptyProvider = new ServiceCollection().BuildServiceProvider();

    public static IMapper CreateMapper() => new ServiceMapper(EmptyProvider, MapperConfig);

    /// <summary>
    /// A minimal <see cref="IHttpContextAccessor"/> carrying a signed-in user, for
    /// services that read the acting user via <c>ClaimTypes.NameIdentifier</c>
    /// (e.g. <c>MedicalRecordService</c>'s audit trail, review item C11) rather
    /// than accepting it as a parameter. Roles are optional - most such services
    /// re-check role membership themselves via <c>principal.IsInRole</c>.
    ///
    /// Deliberately <em>not</em> the real <c>Microsoft.AspNetCore.Http.HttpContextAccessor</c>:
    /// that implementation stores <c>HttpContext</c> in a static <c>AsyncLocal</c>,
    /// which only flows <em>downward</em> into calls made after it's set - a value
    /// set inside an awaited helper method (as every test here does, to build the
    /// service alongside its seed data) reverts to null the moment that helper
    /// returns to its caller, before the service under test ever runs. A plain
    /// settable property has ordinary object semantics and needs none of that.
    /// </summary>
    public static IHttpContextAccessor CreateHttpContextAccessor(int userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, authenticationType: "Test");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        return new FakeHttpContextAccessor { HttpContext = httpContext };
    }

    private sealed class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
