using ClinicNow.API.Filters;
using ClinicNow.Model.Configuration;
using ClinicNow.Model.Resilience;
using ClinicNow.Services.Database;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;

// Load .env before anything else reads configuration. TraversePath() walks up from
// the current working directory to find .env regardless of how/where the process
// was launched from (plain `dotnet run`, an IDE, or `dotnet ef` design-time tooling
// - which all use different working directories). In Docker, config instead arrives
// as real container environment variables set by docker-compose.yml, no .env file
// is present, and this silently no-ops - safe in every environment.
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// --- Centralized configuration (rulebook Part II §C/§D) ---------------------------
// Every environment variable is read exactly once, right here, into a strongly
// typed *Options instance registered as a singleton. No other class calls
// Environment.GetEnvironmentVariable directly - see EnvOptionsBase for why.
var databaseOptions = new DatabaseOptions();
var jwtOptions = new JwtOptions();
var rabbitMqOptions = new RabbitMqOptions();
var smtpOptions = new SmtpOptions();
var payPalOptions = new PayPalOptions();
var corsOptions = new CorsOptions();

builder.Services.AddSingleton(databaseOptions);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(rabbitMqOptions);
builder.Services.AddSingleton(smtpOptions);
builder.Services.AddSingleton(payPalOptions);
builder.Services.AddSingleton(corsOptions);

// --- Database -----------------------------------------------------------------
// ClinicNowContext (and every service built on top of it) is Scoped by default via
// AddDbContext - never Transient/Singleton, per rulebook Part II §D.
builder.Services.AddDbContext<ClinicNowContext>(options =>
    options.UseSqlServer(databaseOptions.ConnectionString));

// --- Mapster (entity <-> DTO mapping) ------------------------------------------
var mapperConfig = TypeAdapterConfig.GlobalSettings;
builder.Services.AddSingleton(mapperConfig);
builder.Services.AddScoped<IMapper, ServiceMapper>();

// --- Controllers + centralized exception handling ------------------------------
builder.Services.AddControllers(options =>
{
    options.Filters.Add<ExceptionFilter>();
});

// --- Swagger / OpenAPI ----------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ClinicNow API", Version = "v1" });

    // Bearer scheme is described now so Swagger's "Authorize" button is ready the
    // moment JWT auth is wired in Phase 1 - this adds no runtime behaviour today.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\""
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// --- CORS: defined exactly once, explicit origins only, never "*" --------------
const string CorsPolicyName = "ClinicNowClients";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        if (corsOptions.AllowedOrigins.Length > 0)
        {
            policy.WithOrigins(corsOptions.AllowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

// --- Cross-cutting infrastructure ------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

// NOTE: JWT authentication/authorization (AddAuthentication + AddJwtBearer) and the
// concrete I<Entity>Service registrations are added starting Phase 1, once the
// User/Role identity model exists. This host is intentionally "empty but
// correct" for Phase 0 - see PLAN.md.

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Swashbuckle still generates the OpenAPI JSON (it already knows about the
    // Bearer scheme configured above); Scalar replaces SwaggerUI as the actual
    // interactive testing surface - one UI instead of two pointing at the same doc.
    app.UseSwagger();
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("ClinicNow API")
            .WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json");
    });
}

app.UseCors(CorsPolicyName);

// Plain HTTP is preferred for this project end-to-end (dev, Docker, and grading) -
// self-signed HTTPS certificates can expire or fail validation on a reviewer's
// machine (rulebook §9.2). See CLAUDE.md §3 "Locked Tech Stack".
// app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// Health endpoint required for every service (CLAUDE.md "Observability"). Verifies
// real DB connectivity rather than just returning a static 200, so it's useful for
// docker-compose healthchecks and manual smoke-testing alike.
app.MapGet("/health", async (ClinicNowContext context, CancellationToken cancellationToken) =>
{
    var canConnect = await context.Database.CanConnectAsync(cancellationToken);
    return Results.Ok(new
    {
        status = canConnect ? "healthy" : "degraded",
        database = canConnect ? "connected" : "unreachable",
        utcTimestamp = DateTime.UtcNow
    });
}).ExcludeFromDescription();

// Apply pending EF Core migrations on startup, as required for the app to run on a
// clean database with zero manual intervention (rulebook §9.1). Guarded by
// EF.IsDesignTime so this doesn't also fire as a side effect of design-time
// tooling (e.g. `dotnet ef migrations add`, which builds this same Program.cs to
// discover the DbContext but must not touch a real database while doing so).
//
// Retries with the same exponential backoff as the Worker's RabbitMQ connection
// (rulebook Appendix A.1's resilience expectation applies just as much to SQL
// Server still starting up under docker-compose as it does to RabbitMQ).
if (!EF.IsDesignTime)
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<ClinicNowContext>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    await RetryHelper.RunWithRetryAsync(
        action: () => context.Database.MigrateAsync(),
        onRetry: (ex, attempt, delay) => startupLogger.LogWarning(ex,
            "Database not ready yet (attempt {Attempt}/{Max}). Retrying in {Delay}...",
            attempt, RetryHelper.DefaultBackoffDelays.Length, delay),
        cancellationToken: CancellationToken.None);
}

app.Run();
