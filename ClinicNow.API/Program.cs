using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using ClinicNow.API;
using ClinicNow.API.Filters;
using ClinicNow.Model.Configuration;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Resilience;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services;
using ClinicNow.Services.Appointments;
using ClinicNow.Services.Appointments.AppointmentStateMachine;
using ClinicNow.Services.Codebooks;
using ClinicNow.Services.Database;
using ClinicNow.Services.Documents;
using ClinicNow.Services.Messaging;
using ClinicNow.Services.News;
using ClinicNow.Services.Notifications;
using ClinicNow.Services.Payments;
using ClinicNow.Services.People;
using ClinicNow.Services.Records;
using ClinicNow.Services.Recommender;
using ClinicNow.Services.Referrals;
using ClinicNow.Services.Reports;
using ClinicNow.Services.Security;
using ClinicNow.Services.Users;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using QuestPDF.Infrastructure;
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
var recommenderOptions = new RecommenderOptions();

builder.Services.AddSingleton(databaseOptions);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(rabbitMqOptions);
builder.Services.AddSingleton(smtpOptions);
builder.Services.AddSingleton(payPalOptions);
builder.Services.AddSingleton(corsOptions);
builder.Services.AddSingleton(recommenderOptions);

// --- Database -----------------------------------------------------------------
// ClinicNowContext (and every service built on top of it) is Scoped by default via
// AddDbContext - never Transient/Singleton, per rulebook Part II §D.
builder.Services.AddDbContext<ClinicNowContext>(options =>
    options.UseSqlServer(databaseOptions.ConnectionString, sqlOptions =>
        // Booking runs under SERIALIZABLE isolation (InitialAppointmentState), which
        // makes deadlock victims and serialization conflicts an expected outcome
        // rather than an exceptional one - and every one of them surfaced to the
        // patient as a 500. Retrying transient SQL errors turns those into a
        // successful second attempt.
        //
        // Note for anyone adding a manual BeginTransactionAsync: with a retrying
        // execution strategy EF requires it to be wrapped in
        // context.Database.CreateExecutionStrategy().ExecuteAsync(...), otherwise it
        // throws at runtime. The existing state-machine transactions are wrapped for
        // exactly this reason.
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(2),
            errorNumbersToAdd: null)));

// --- Mapster (entity <-> DTO mapping) ------------------------------------------
var mapperConfig = TypeAdapterConfig.GlobalSettings;
// Discovers every IRegister in ClinicNow.Services (e.g. UserMappingConfig) instead
// of hand-wiring each one here - new entity-specific mapping rules just need to add
// an IRegister class, nothing in Program.cs changes.
mapperConfig.Scan(typeof(ClinicNowContext).Assembly);
builder.Services.AddSingleton(mapperConfig);
builder.Services.AddScoped<IMapper, ServiceMapper>();

// --- Identity / Auth (Phase 1) ---------------------------------------------------
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ITokenBlocklistService, TokenBlocklistService>();
builder.Services.AddScoped<IUserService, UserService>();

// --- Codebooks (Phase 2) ---------------------------------------------------------
// Each registered directly against the generic ICRUDService<...> - no per-entity
// service interface needed since none of these have behaviour beyond plain CRUD
// (rulebook Part II §D: reuse the generic base classes).
builder.Services.AddScoped<ICRUDService<CityDto, CitySearchObject, CityInsertRequest, CityUpdateRequest>, CityService>();
builder.Services.AddScoped<ICRUDService<SpecializationDto, SpecializationSearchObject, SpecializationInsertRequest, SpecializationUpdateRequest>, SpecializationService>();
builder.Services.AddScoped<ICRUDService<LocationDto, LocationSearchObject, LocationInsertRequest, LocationUpdateRequest>, LocationService>();
builder.Services.AddScoped<ICRUDService<MedicalServiceDto, MedicalServiceSearchObject, MedicalServiceInsertRequest, MedicalServiceUpdateRequest>, MedicalServiceService>();

// --- People: Patients & Doctors (Phase 3) -----------------------------------------
// Patient is registered by its bespoke IPatientService (adds RestoreAsync on top of
// the generic CRUD shape) rather than the plain ICRUDService<...>, same pattern as
// IAppointmentService below.
builder.Services.AddScoped<IPatientService, PatientService>();
// Doctor, likewise, by its bespoke IDoctorService (adds GetOwnAsync).
builder.Services.AddScoped<IDoctorService, DoctorService>();
// Staff has no separate profile entity - the generic CRUD shape over User rows,
// filtered to the Staff role. Registered by its own interface only for
// consistency with the rest of this block; it adds nothing beyond ICRUDService.
builder.Services.AddScoped<IStaffService, StaffService>();
builder.Services.AddScoped<ICRUDService<WorkingHoursDto, WorkingHoursSearchObject, WorkingHoursInsertRequest, WorkingHoursUpdateRequest>, WorkingHoursService>();
builder.Services.AddScoped<ICRUDService<ScheduleBlockDto, ScheduleBlockSearchObject, ScheduleBlockInsertRequest, ScheduleBlockUpdateRequest>, ScheduleBlockService>();

// --- Appointments + state machine (Phase 4) ---------------------------------------
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<InitialAppointmentState>();
builder.Services.AddScoped<ScheduledAppointmentState>();
builder.Services.AddScoped<ConfirmedAppointmentState>();
builder.Services.AddScoped<CompletedAppointmentState>();
builder.Services.AddScoped<CancelledAppointmentState>();

// --- Notifications, News & async email (Phase 5) -----------------------------------
builder.Services.AddSingleton<RabbitMqPublisherConnectionProvider>();
builder.Services.AddScoped<IEmailPublisher, RabbitMqEmailPublisher>();
// Device push rides the same broker connection as mail: the API only ever
// queues, the Worker does the FCM call.
builder.Services.AddScoped<IPushPublisher, RabbitMqPushPublisher>();
builder.Services.AddScoped<IDeviceTokenService, DeviceTokenService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<INewsItemService, NewsItemService>();
builder.Services.AddScoped<ICRUDService<NewsItemDto, NewsItemSearchObject, NewsItemInsertRequest, NewsItemUpdateRequest>>(sp => sp.GetRequiredService<INewsItemService>());
builder.Services.AddHostedService<PreAppointmentReminderHostedService>();

// --- Medical documentation (Phase 6) -----------------------------------------------
builder.Services.AddScoped<IMedicalDocumentService, MedicalDocumentService>();
builder.Services.AddScoped<ILabFindingService, LabFindingService>();
builder.Services.AddScoped<IReferralService, ReferralService>();

// --- Medical record ("medicinski karton") ---------------------------------------
builder.Services.AddScoped<IMedicalRecordService, MedicalRecordService>();

// --- Recommender (Phase 7) --------------------------------------------------------
builder.Services.AddScoped<IRecommenderService, RecommenderService>();

// --- Payments (Phase 8) -----------------------------------------------------------
builder.Services.AddScoped<IPayPalClient, PayPalClient>();
builder.Services.AddScoped<IPaymentService, PaymentService>();

// --- Reports & Dashboard (Phase 9) ------------------------------------------------
// QuestPDF requires its license accepted exactly once per process before the
// first GeneratePdf() call - Community is the free tier and fits this project.
QuestPDF.Settings.License = LicenseType.Community;
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IReportService, ReportService>();

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

// --- JWT authentication -----------------------------------------------------------
// Signature, issuer, audience and expiry are all validated - a token signed with a
// different key, for a different audience, or past its exp claim, is rejected
// outright before OnTokenValidated even runs (rulebook §5: "JWT potpis mora biti
// validiran"). OnTokenValidated adds the one thing the library can't do on its own:
// checking the still-otherwise-valid token's jti against RevokedToken, so a logged
// out token stops working immediately instead of merely expiring naturally later.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var jti = context.Principal?.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti);
                if (string.IsNullOrEmpty(jti))
                {
                    context.Fail("Token nema jti claim.");
                    return;
                }

                var blocklist = context.HttpContext.RequestServices.GetRequiredService<ITokenBlocklistService>();
                if (await blocklist.IsRevokedAsync(jti, context.HttpContext.RequestAborted))
                {
                    context.Fail("Token je opozvan (izvršena je odjava).");
                    return;
                }

                // Second, coarser revocation check: the jti blocklist can only reject
                // tokens someone explicitly logged out, but a password change has to
                // end *every* session at once - including the attacker's, which is
                // usually the whole reason the password is being changed. Tokens
                // issued before the account's cutoff are refused regardless of their
                // own expiry.
                var subject = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var issuedAt = context.Principal?.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Iat);

                if (int.TryParse(subject, out var userId)
                    && long.TryParse(issuedAt, out var issuedAtUnixSeconds))
                {
                    var validFrom = await blocklist.GetTokensValidFromUtcAsync(
                        userId, context.HttpContext.RequestAborted);

                    var issuedAtUtc = DateTimeOffset.FromUnixTimeSeconds(issuedAtUnixSeconds).UtcDateTime;
                    if (validFrom is DateTime cutoffUtc && issuedAtUtc < cutoffUtc)
                    {
                        context.Fail("Lozinka je promijenjena - potrebna je ponovna prijava.");
                    }
                }
            }
        };
    });
builder.Services.AddAuthorization();

// --- Rate limiting on auth endpoints (global rule: "Add rate limiting on auth and
// write operations") - a fixed window keeps this simple while still meaningfully
// slowing down credential-stuffing/brute-force attempts against /api/auth/login.
//
// Every policy here is PARTITIONED. AddFixedWindowLimiter without a partition key
// builds a single bucket shared by every caller in the world, which is worse than
// no limit at all: it barely inconveniences an attacker (still 10 tries a minute)
// while letting that one attacker spend the whole clinic's budget and lock every
// legitimate user out of login - a denial of service opened by the control meant to
// prevent one. Partitioning by client address gives each caller its own budget.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Behind a reverse proxy RemoteIpAddress is the proxy unless UseForwardedHeaders
    // runs first, which would collapse every client into one partition. Deployment
    // here is direct (docker-compose publishes the API port), so this is the real
    // client address.
    static string PartitionKey(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    options.AddPolicy(RateLimiterPolicies.Auth, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // The global rule is "rate limiting on auth *and write operations*". Writes were
    // previously unlimited, which let any authenticated patient hammer
    // POST /api/Appointment - and every one of those attempts opens a SERIALIZABLE
    // transaction, so it was a cheap way to put the booking table under sustained
    // lock contention. Limited per user where there is one, per address otherwise,
    // so one noisy account cannot spend another's budget.
    options.AddPolicy(RateLimiterPolicies.Write, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? PartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// --- Response compression -------------------------------------------------------
// The clients are a Windows desktop app and an Android phone, and the phone is the
// one that matters here: list payloads are JSON, which compresses roughly 6-10x, and
// a patient on a metered or 3G connection pays for every uncompressed byte.
builder.Services.AddResponseCompression(options =>
{
    // Compression over TLS reintroduces BREACH-style oracles; this API is
    // deliberately plain HTTP end-to-end (CLAUDE.md §3), so the usual objection
    // does not apply. Left explicit rather than implied.
    options.EnableForHttps = false;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
});

// --- Cross-cutting infrastructure ------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

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

// Before CORS/auth so it also covers responses those short-circuit.
app.UseResponseCompression();

// Downloads serve stored bytes under a stored Content-Type. That type comes from a
// validated allowlist (FileValidation checks MIME *and* magic bytes), so this is
// defence in depth rather than the primary control - but it costs one header and
// removes content-sniffing as a way to turn a stored file into something the
// browser will execute, which matters for the Flutter web build.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});

app.UseCors(CorsPolicyName);

// Plain HTTP is preferred for this project end-to-end (dev, Docker, and grading) -
// self-signed HTTPS certificates can expire or fail validation on a reviewer's
// machine (rulebook §9.2). See CLAUDE.md §3 "Locked Tech Stack".
// app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAuthentication();
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

    // Grows the seed to a demoable size and, unlike the migration HasData
    // rows above (constant by construction), anchors every time-sensitive
    // row it adds to "now" - see DemoDataSeeder's remarks for why HasData
    // alone can't fix a seed that goes stale the moment the calendar moves
    // past it (rulebook §3.1).
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var seederLogger = scope.ServiceProvider.GetRequiredService<ILogger<DemoDataSeeder>>();
    await new DemoDataSeeder(context, passwordHasher, seederLogger).SeedAsync(CancellationToken.None);
}

app.Run();
