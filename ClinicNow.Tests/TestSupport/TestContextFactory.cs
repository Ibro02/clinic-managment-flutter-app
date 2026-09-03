using ClinicNow.Services.Database;
using Mapster;
using MapsterMapper;
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
}
