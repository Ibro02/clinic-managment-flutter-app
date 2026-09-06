using System.Security.Claims;
using System.Text;
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
    options.UseSqlServer(databaseOptions.ConnectionString));

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
builder.Services.AddScoped<ICRUDService<DoctorDto, DoctorSearchObject, DoctorInsertRequest, DoctorUpdateRequest>, DoctorService>();
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
builder.Services.AddScoped<IReportPdfService, ReportPdfService>();

// SignalR for real-time notification auto-refresh (rulebook Part II §G) - the JWT
// is delivered via the `access_token` query string since browsers/WebSockets can't
// set an Authorization header on the initial handshake (wired below, OnMessageReceived).
builder.Services.AddSignalR();
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, NotificationUserIdProvider>();

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
            // SignalR/WebSocket clients can't set an Authorization header on the
            // initial handshake, so the JWT arrives via the `access_token` query
            // string instead for hub requests specifically (never for regular API
            // calls - AccessTokenProvider on the client only sets this for the hub
            // connection URL).
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
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
                }
            }
        };
    });
builder.Services.AddAuthorization();

// --- Rate limiting on auth endpoints (global rule: "Add rate limiting on auth and
// write operations") - a fixed window keeps this simple while still meaningfully
// slowing down credential-stuffing/brute-force attempts against /api/auth/login.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter(RateLimiterPolicies.Auth, limiterOptions =>
    {
        limiterOptions.PermitLimit = 10;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
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

app.UseCors(CorsPolicyName);

// Plain HTTP is preferred for this project end-to-end (dev, Docker, and grading) -
// self-signed HTTPS certificates can expire or fail validation on a reviewer's
// machine (rulebook §9.2). See CLAUDE.md §3 "Locked Tech Stack".
// app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ClinicNow.Services.Notifications.NotificationsHub>("/hubs/notifications");

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
