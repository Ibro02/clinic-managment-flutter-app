# Phase 7 — Recommender Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the explainable content-based appointment recommender (PLAN.md Phase 7): a real ML.NET cosine-similarity pipeline over the patient's own history, a popularity fallback for cold start, a genuinely-written-and-used `RecommenderInteraction` signal table, backend API surface, and mobile UI showing recommendations with a reason on the "Preporuke" tab and inside the booking flow.

**Architecture:** New `RecommenderInteraction` entity + EF migration; a bespoke `IRecommenderService`/`RecommenderService` in `ClinicNow.Services` (not `BaseCRUDService` — this is a computed/aggregated read, not CRUD) that trains/caches an ML.NET `FeaturizeText` + `OneHotEncoding` + `Concatenate` pipeline, extracts feature vectors, and ranks server-computed candidate slots by a recency+frequency-weighted cosine similarity against the patient's appointment history **and** their `RecommenderInteraction` history (both signals actually scored, per rulebook §2.4 / doc §3). A thin `RecommendationController` exposes `GET api/Recommendation/appointments` and `POST api/Recommendation/interaction`. Mobile gets a new `RecommendationsScreen` (replaces the Phase 0 "Profil" placeholder tab) plus interaction logging + a "Preporučeno" banner wired into the existing `BookAppointmentScreen`.

**Tech Stack:** ASP.NET Core / EF Core / SQL Server (existing), **Microsoft.ML** (new package), Flutter/`provider`/`http` (existing, mirrored patterns).

**Spec:** [recommender-dokumentacija.md](../../../recommender-dokumentacija.md) (already fully written and is the authoritative, binding spec per its own preamble — implementation MUST match it). Also: [CLAUDE.md §10](../../../CLAUDE.md), [GOALS.md](../../../GOALS.md) row 10, [PLAN.md Phase 7](../../../PLAN.md).

## Global Constraints

- Every entity: DB entity + consistent DTO + Insert/Update/Search where relevant — never expose EF entities, never `dynamic`.
- `DateTime.UtcNow` in storage; day-of-week/time-of-day bucketing computed over UTC values (doc §3 explicit requirement — Docker-safe, timezone-independent).
- Config only in `.env`/`.env.example`, read once in the options class constructor (`EnvOptionsBase`); nothing hardcoded, nothing in `appsettings.json`.
- Services touching `DbContext` are `Scoped`. `userId`/`patientId` always resolved from the JWT via `IHttpContextAccessor`, never from the request body/route.
- Pagination cap (`BaseSearchObject.MaxPageSize = 100`) applies to any new list endpoint; the recommendation list itself is a small bounded Top-N (not paged — it's not a CRUD list).
- Custom exceptions (`ValidationException`, `NotFoundException`, `ForbiddenException`) → `ExceptionFilter`; no raw exceptions, no stack traces to the client, `ILogger<T>` for anything worth knowing (model train/load, retrain).
- No dead code, no `NotImplementedException`, DRY. No `Console.WriteLine`/`Thread.Sleep`/`.Result`/`.Wait()`.
- **Every signal this phase collects must actually be scored** — this is an explicit, named requirement in both the rulebook (§2.4) and `recommender-dokumentacija.md` §3 ("nije dozvoljeno prikupljati npr. prosječnu ocjenu, a zatim je ignorisati"). `RecommenderInteraction` rows are not allowed to be write-only.
- **Verification convention for this codebase:** there is no backend automated test project (every prior phase, per `GOALS.md`'s progress log, was verified live against the real Docker stack: `dotnet build` clean, migration applied to the containerized SQL Server, endpoints exercised with curl/the running app, `flutter analyze`/`flutter test` clean on both Flutter apps). This plan follows that established pattern instead of inventing a parallel unit-test harness — each task's "Verify" step is a live check, matching how Phases 0–6 were actually done.

---

## File Structure

**Backend — new files:**
- `ClinicNow.Model/Common/InteractionType.cs` — enum (`DoctorView`, `MedicalServiceView`, `Search`).
- `ClinicNow.Model/Common/TimeOfDayBucket.cs` — enum + `DateTime.ToTimeOfDayBucket()` extension (UTC-based).
- `ClinicNow.Model/Configuration/RecommenderOptions.cs` — `.env`-driven `TopN`/`PopularityWindowDays`/`CandidateLookaheadDays`/`ModelPath`/`RetrainIntervalMinutes`.
- `ClinicNow.Model/Dto/AppointmentRecommendationDto.cs` — one ranked suggestion + its `Reason`.
- `ClinicNow.Model/Requests/RecommenderInteractionRequest.cs` — logs one view/search interaction.
- `ClinicNow.Services/Database/Entities/RecommenderInteraction.cs` — the signal table (doc §3).
- `ClinicNow.Services/Database/Configurations/RecommenderInteractionConfiguration.cs` — FKs, indexes, seed rows.
- `ClinicNow.Services/Recommender/IRecommenderService.cs`
- `ClinicNow.Services/Recommender/RecommenderService.cs` — the ML.NET pipeline, scoring, popularity fallback, interaction logging.
- `ClinicNow.API/Controllers/RecommendationController.cs`
- `ClinicNow.Services/Database/Migrations/<timestamp>_AddRecommenderInteractions.cs` — generated by `dotnet ef migrations add`.

**Backend — modified files:**
- `ClinicNow.Services/Database/ClinicNowContext.cs` — add `DbSet<RecommenderInteraction>`.
- `ClinicNow.Services/ClinicNow.Services.csproj` — add `Microsoft.ML` package reference.
- `ClinicNow.API/Program.cs` — register `RecommenderOptions`, `AddMemoryCache()`, `AddScoped<IRecommenderService, RecommenderService>()`.
- `.env`, `.env.example` — new `RECOMMENDER_*` keys.
- `recommender-dokumentacija.md` — precise addendum for `frequencyFactor`/interaction-boost weight/candidate-lookahead bound (the doc currently names these without a formula; this closes that gap so doc == code, per the doc's own compliance rule).
- `GOALS.md` — flip row 10 to ☑, append a progress-log entry once verified live.

**Mobile (`clinicnow_mobile`) — new files:**
- `lib/models/recommendation.dart` — mirrors `AppointmentRecommendationDto`.
- `lib/providers/recommendation_provider.dart` — `GET api/Recommendation/appointments`, `POST api/Recommendation/interaction`.
- `lib/screens/recommendations/recommendations_screen.dart` — ranked list + reason + a doctor/service search box (this is the genuine "search" interaction source).

**Mobile — modified files:**
- `lib/layouts/app_shell.dart` — tab 3 (currently a placeholder) becomes `RecommendationsScreen`, labeled "Preporuke".
- `lib/screens/appointments/book_appointment_screen.dart` — accepts optional pre-fill (`initialDoctor`/`initialService`), shows a "Preporučeno" banner from the same recommendation endpoint, logs a view interaction on doctor/service selection.

---

## Task 1: Model-layer types (enums, options, DTO, request)

**Files:**
- Create: `ClinicNow.Model/Common/InteractionType.cs`
- Create: `ClinicNow.Model/Common/TimeOfDayBucket.cs`
- Create: `ClinicNow.Model/Configuration/RecommenderOptions.cs`
- Create: `ClinicNow.Model/Dto/AppointmentRecommendationDto.cs`
- Create: `ClinicNow.Model/Requests/RecommenderInteractionRequest.cs`

**Interfaces:**
- Produces: `InteractionType` (`DoctorView=0`, `MedicalServiceView=1`, `Search=2`), `TimeOfDayBucket` (`Morning=0`, `Afternoon=1`, `Evening=2`), `DateTime.ToTimeOfDayBucket() : TimeOfDayBucket`, `RecommenderOptions{TopN,PopularityWindowDays,CandidateLookaheadDays,ModelPath,RetrainIntervalMinutes}`, `AppointmentRecommendationDto`, `RecommenderInteractionRequest{InteractionType Type, int? DoctorId, int? MedicalServiceId}` — all consumed by Task 2/3/4.

- [ ] **Step 1: `InteractionType.cs`**

```csharp
namespace ClinicNow.Model.Common;

/// <summary>
/// The kind of real user action a <c>RecommenderInteraction</c> row records
/// (doc §3: "otvaranje detalja doktora ili usluge, pretraga"). Serialized as
/// an int on the wire, same convention as <see cref="AppointmentStatus"/>.
/// </summary>
public enum InteractionType
{
    DoctorView = 0,
    MedicalServiceView = 1,
    Search = 2
}
```

- [ ] **Step 2: `TimeOfDayBucket.cs`**

```csharp
namespace ClinicNow.Model.Common;

/// <summary>
/// Categorical "doba dana" feature used by the recommender's content-based
/// pipeline (recommender-dokumentacija.md §4). Bucketed over the UTC hour -
/// never local time, so the result is identical regardless of server/Docker
/// timezone (doc §3: "izvođenje ... doba dana radi se nad UTC vrijednostima").
/// </summary>
public enum TimeOfDayBucket
{
    Morning = 0,   // [06:00, 12:00) UTC
    Afternoon = 1, // [12:00, 18:00) UTC
    Evening = 2    // [18:00, 06:00) UTC
}

public static class TimeOfDayBucketExtensions
{
    public static TimeOfDayBucket ToTimeOfDayBucket(this DateTime utcDateTime) => utcDateTime.Hour switch
    {
        >= 6 and < 12 => TimeOfDayBucket.Morning,
        >= 12 and < 18 => TimeOfDayBucket.Afternoon,
        _ => TimeOfDayBucket.Evening
    };

    /// <summary>Bosnian display label, mirroring <c>AppointmentStatusExtensions.ToDisplayName</c>.</summary>
    public static string ToDisplayName(this TimeOfDayBucket bucket) => bucket switch
    {
        TimeOfDayBucket.Morning => "ujutro",
        TimeOfDayBucket.Afternoon => "poslijepodne",
        TimeOfDayBucket.Evening => "uveče",
        _ => bucket.ToString()
    };
}
```

- [ ] **Step 3: `RecommenderOptions.cs`**

```csharp
namespace ClinicNow.Model.Configuration;

/// <summary>
/// Recommender tuning, sourced from `.env` (doc §7: "putanje i parametri ...
/// čitaju se iz konfiguracije, a ne hardkodiraju u kodu"). Used only by
/// <c>ClinicNow.Services.Recommender.RecommenderService</c>.
/// </summary>
public class RecommenderOptions : EnvOptionsBase
{
    /// <summary>Top-N suggestions returned per request (doc §5.2, default 5).</summary>
    public int TopN { get; }

    /// <summary>Lookback window (days) for the popularity-based cold-start fallback (doc §2.2, default 90).</summary>
    public int PopularityWindowDays { get; }

    /// <summary>How many days ahead of "today" candidate free slots are searched for each (Doctor, MedicalService) pair.</summary>
    public int CandidateLookaheadDays { get; }

    /// <summary>Filesystem path the trained ML.NET model is serialized to/loaded from (doc §7).</summary>
    public string ModelPath { get; }

    /// <summary>How long a cached trained model is reused before a background retrain is triggered on next use (doc §7: "model se osvježava periodično").</summary>
    public int RetrainIntervalMinutes { get; }

    public RecommenderOptions()
    {
        TopN = GetOrDefault("RECOMMENDER_TOP_N", 5);
        PopularityWindowDays = GetOrDefault("RECOMMENDER_POPULARITY_WINDOW_DAYS", 90);
        CandidateLookaheadDays = GetOrDefault("RECOMMENDER_CANDIDATE_LOOKAHEAD_DAYS", 14);
        ModelPath = GetOrDefault("RECOMMENDER_MODEL_PATH", "recommender-model.zip");
        RetrainIntervalMinutes = GetOrDefault("RECOMMENDER_RETRAIN_INTERVAL_MINUTES", 30);
    }
}
```

- [ ] **Step 4: `AppointmentRecommendationDto.cs`**

```csharp
namespace ClinicNow.Model.Dto;

/// <summary>
/// One ranked, explainable suggestion (recommender-dokumentacija.md §6) - a
/// real free slot, never a client-side guess, always paired with a
/// human-readable <see cref="Reason"/> (rulebook Part II §K: never a raw ID -
/// every field here is a display name/value, not a bare FK).
/// </summary>
public class AppointmentRecommendationDto
{
    public int DoctorId { get; set; }

    public string DoctorName { get; set; } = string.Empty;

    public List<string> DoctorSpecializations { get; set; } = [];

    public int MedicalServiceId { get; set; }

    public string MedicalServiceName { get; set; } = string.Empty;

    public decimal MedicalServicePrice { get; set; }

    public int LocationId { get; set; }

    public string LocationName { get; set; } = string.Empty;

    /// <summary>The earliest real free slot for this doctor+service combination within the configured lookahead window.</summary>
    public DateTime SuggestedStartUtc { get; set; }

    /// <summary>Weighted cosine-similarity score in [0, 1] (0 for a pure popularity-fallback row).</summary>
    public double Score { get; set; }

    /// <summary>Short, human-readable explanation grounded in the real signals from doc §3 (never generic "Recommended for you").</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>True when this row came from the popularity fallback (doc §2.2) rather than the content-based model - lets the UI badge it differently if desired.</summary>
    public bool IsPopularityFallback { get; set; }
}
```

- [ ] **Step 5: `RecommenderInteractionRequest.cs`**

```csharp
using ClinicNow.Model.Common;

namespace ClinicNow.Model.Requests;

/// <summary>
/// Logs one real interaction (doc §3). At least one of <see cref="DoctorId"/>/
/// <see cref="MedicalServiceId"/> is required - a row with neither can never
/// contribute to scoring (see RecommenderService.BuildInteractionHistoryRows),
/// so the service rejects it up front rather than silently storing a useless
/// signal (the exact anti-pattern doc §3 warns against).
/// </summary>
public class RecommenderInteractionRequest
{
    public InteractionType Type { get; set; }

    public int? DoctorId { get; set; }

    public int? MedicalServiceId { get; set; }
}
```

- [ ] **Step 6: Build & verify**

Run:
```bash
cd ClinicNow && dotnet build ClinicNow.Model/ClinicNow.Model.csproj
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add ClinicNow.Model/Common/InteractionType.cs ClinicNow.Model/Common/TimeOfDayBucket.cs ClinicNow.Model/Configuration/RecommenderOptions.cs ClinicNow.Model/Dto/AppointmentRecommendationDto.cs ClinicNow.Model/Requests/RecommenderInteractionRequest.cs
git commit -m "feat(recommender): add model-layer types (InteractionType, TimeOfDayBucket, RecommenderOptions, DTO, request)"
```

---

## Task 2: `RecommenderInteraction` entity + EF configuration + migration

**Files:**
- Create: `ClinicNow.Services/Database/Entities/RecommenderInteraction.cs`
- Create: `ClinicNow.Services/Database/Configurations/RecommenderInteractionConfiguration.cs`
- Modify: `ClinicNow.Services/Database/ClinicNowContext.cs`
- Create (generated): `ClinicNow.Services/Database/Migrations/<timestamp>_AddRecommenderInteractions.cs`

**Interfaces:**
- Consumes: `InteractionType` (Task 1).
- Produces: `RecommenderInteraction{Id,UserId,User,InteractionType,DoctorId?,Doctor?,MedicalServiceId?,MedicalService?,DateTimeUtc}`, `ClinicNowContext.RecommenderInteractions : DbSet<RecommenderInteraction>` — consumed by Task 3.

- [ ] **Step 1: `RecommenderInteraction.cs`**

```csharp
using ClinicNow.Model.Common;

namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A real, app-written signal (doc §3): "otvaranje detalja doktora ili
/// usluge, pretraga". Not a reference/codebook table (CLAUDE.md §6) and not
/// a pure M:N join - this is a genuine, growing event log, so it counts
/// toward the ≥10 non-reference tables (rulebook Part II §A).
///
/// <see cref="DoctorId"/>/<see cref="MedicalServiceId"/> are both nullable
/// because a single interaction only ever concerns one of the two (a
/// <see cref="Model.Common.InteractionType.DoctorView"/> row has no
/// service; a <see cref="Model.Common.InteractionType.MedicalServiceView"/>
/// row has no doctor) - but never both null, enforced in
/// <c>RecommenderService.LogInteractionAsync</c>, since a row with neither
/// can never feed into scoring (see RecommenderService.BuildInteractionHistoryRows).
/// </summary>
public class RecommenderInteraction
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public InteractionType InteractionType { get; set; }

    public int? DoctorId { get; set; }

    public Doctor? Doctor { get; set; }

    public int? MedicalServiceId { get; set; }

    public MedicalService? MedicalService { get; set; }

    public DateTime DateTimeUtc { get; set; }
}
```

- [ ] **Step 2: `RecommenderInteractionConfiguration.cs`**

Seed data: 6 interactions across the two demo patients (Patient Id=1/2, matching the seeded `Appointment`/`MedicalRecord` rows from earlier phases), spanning all three `InteractionType` values, with dates recent enough to carry real weight under the `recencyFactor` formula relative to this seed's fixed "today" of **2026-08-25** (same fixed-seed convention as `AppointmentConfiguration`).

```csharp
using ClinicNow.Model.Common;
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class RecommenderInteractionConfiguration : IEntityTypeConfiguration<RecommenderInteraction>
{
    public void Configure(EntityTypeBuilder<RecommenderInteraction> builder)
    {
        // Restrict, not Cascade: an interaction log row is a historical fact
        // about what a user did - it must never be silently wiped out as a
        // side effect of deleting an unrelated row (same reasoning as every
        // other FK off Appointment - see AppointmentConfiguration).
        builder.HasOne(i => i.User).WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Doctor).WithMany().HasForeignKey(i => i.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.MedicalService).WithMany().HasForeignKey(i => i.MedicalServiceId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.UserId);
        builder.HasIndex(i => i.DateTimeUtc);

        // All 6 rows use UserId=4 (patient@clinicnow.test / Patient Id=1) -
        // confirmed directly against ClinicNow.Services/Database/Configurations/UserConfiguration.cs's
        // HasData (Id=1 Administrator, 2 Staff, 3 Doctor, 4 Patient, 5 Doctor2)
        // and the AddPatientsAndDoctors migration, where Patient Id=2 (Amar
        // Šehić) has a NULL UserId - he's a staff-entered walk-in with no
        // login (CLAUDE.md §6), so he can never authenticate and can never
        // produce a RecommenderInteraction row (they're only ever written by
        // RecommenderService.LogInteractionAsync off an authenticated JWT).
        // Spans all three InteractionType values for the one patient who can
        // actually generate them, so the "signals actually used" requirement
        // is demonstrable on a clean DB without needing the app to be used
        // first.
        builder.HasData(
            new RecommenderInteraction { Id = 1, UserId = 4, InteractionType = InteractionType.DoctorView, DoctorId = 1, DateTimeUtc = new DateTime(2026, 8, 22, 9, 15, 0, DateTimeKind.Utc) },
            new RecommenderInteraction { Id = 2, UserId = 4, InteractionType = InteractionType.MedicalServiceView, MedicalServiceId = 1, DateTimeUtc = new DateTime(2026, 8, 22, 9, 16, 0, DateTimeKind.Utc) },
            new RecommenderInteraction { Id = 3, UserId = 4, InteractionType = InteractionType.Search, DoctorId = 2, DateTimeUtc = new DateTime(2026, 8, 23, 18, 40, 0, DateTimeKind.Utc) },
            new RecommenderInteraction { Id = 4, UserId = 4, InteractionType = InteractionType.DoctorView, DoctorId = 1, DateTimeUtc = new DateTime(2026, 8, 24, 8, 5, 0, DateTimeKind.Utc) },
            new RecommenderInteraction { Id = 5, UserId = 4, InteractionType = InteractionType.MedicalServiceView, MedicalServiceId = 2, DateTimeUtc = new DateTime(2026, 8, 21, 14, 0, 0, DateTimeKind.Utc) },
            new RecommenderInteraction { Id = 6, UserId = 4, InteractionType = InteractionType.Search, MedicalServiceId = 5, DateTimeUtc = new DateTime(2026, 8, 24, 19, 30, 0, DateTimeKind.Utc) });
    }
}
```

- [ ] **Step 3: Register the `DbSet` in `ClinicNowContext.cs`**

Add after the `MedicalRecordEntries` line (~line 57):

```csharp
    public DbSet<RecommenderInteraction> RecommenderInteractions => Set<RecommenderInteraction>();
```

- [ ] **Step 4: Generate the migration**

Run (from `ClinicNow.API`, matching how every prior phase's migration was generated - check the exact working directory/flags against e.g. how `AddMedicalRecords` was generated if this differs):
```bash
cd ClinicNow && dotnet ef migrations add AddRecommenderInteractions --project ClinicNow.Services --startup-project ClinicNow.API
```
Expected: a new `<timestamp>_AddRecommenderInteractions.cs` + `.Designer.cs` under `ClinicNow.Services/Database/Migrations/`, and `ClinicNowContextModelSnapshot.cs` updated. Read the generated `Up()` to confirm: `CreateTable("RecommenderInteractions", ...)` with `UserId` (not null), `DoctorId`/`MedicalServiceId` (nullable), `InteractionType` (int, not null), `DateTimeUtc` (datetime2, not null), three `Restrict` FKs, the two indexes, and the 6 `InsertData` seed rows.

- [ ] **Step 5: Build & verify**

```bash
cd ClinicNow && dotnet build ClinicNow.Services/ClinicNow.Services.csproj
```
Expected: 0 warnings/errors.

- [ ] **Step 6: Commit**

```bash
git add ClinicNow.Services/Database/Entities/RecommenderInteraction.cs ClinicNow.Services/Database/Configurations/RecommenderInteractionConfiguration.cs ClinicNow.Services/Database/ClinicNowContext.cs "ClinicNow.Services/Database/Migrations/*AddRecommenderInteractions*"
git commit -m "feat(recommender): add RecommenderInteraction entity, EF config, and migration"
```

---

## Task 3: `IRecommenderService` / `RecommenderService` (ML.NET pipeline + scoring + popularity fallback + interaction logging)

This is the core of the phase. Read it against `recommender-dokumentacija.md` §4/§5 side by side while implementing - every named piece (FeaturizeText columns, Concatenate, cosine formula, recency/frequency weight) must appear in the code with the same name/shape the doc describes.

**Files:**
- Create: `ClinicNow.Services/Recommender/IRecommenderService.cs`
- Create: `ClinicNow.Services/Recommender/RecommenderService.cs`
- Modify: `ClinicNow.Services/ClinicNow.Services.csproj`

**Interfaces:**
- Consumes: `RecommenderOptions`, `AppointmentRecommendationDto`, `RecommenderInteractionRequest`, `InteractionType`, `TimeOfDayBucket`/`ToTimeOfDayBucket()` (Task 1); `RecommenderInteraction` entity + `ClinicNowContext.RecommenderInteractions` (Task 2); `IAppointmentService.GetAvailableSlotsAsync(int doctorId, int medicalServiceId, DateOnly date, CancellationToken)` (existing, `ClinicNow.Services/Appointments/IAppointmentService.cs`); `ClinicNowContext.{Patients,Doctors,MedicalServices,Appointments}`, `IHttpContextAccessor`, `IMemoryCache`, `ILogger<RecommenderService>`.
- Produces: `IRecommenderService.GetRecommendationsAsync(CancellationToken) : Task<List<AppointmentRecommendationDto>>`, `IRecommenderService.LogInteractionAsync(RecommenderInteractionRequest, CancellationToken) : Task` — consumed by Task 4 (`RecommendationController`).

- [ ] **Step 1: Add the `Microsoft.ML` package**

```bash
cd ClinicNow && dotnet add ClinicNow.Services/ClinicNow.Services.csproj package Microsoft.ML
```
This resolves and pins the current latest stable version into `ClinicNow.Services.csproj` (don't hand-type a version number - let `dotnet add package` pick the real published one, the same way every other package in this repo was added).

- [ ] **Step 2: Verify the package restores**

```bash
cd ClinicNow && dotnet restore ClinicNow.Services/ClinicNow.Services.csproj
```
Expected: restore succeeds, no NU1xxx errors.

- [ ] **Step 3: `IRecommenderService.cs`**

```csharp
using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;

namespace ClinicNow.Services.Recommender;

/// <summary>
/// Content-based (+ popularity fallback) appointment recommender
/// (recommender-dokumentacija.md). Bespoke, like <c>IAppointmentService</c> -
/// this isn't CRUD, and ownership (the caller's own Patient row) needs an
/// async JWT-driven lookup the generic <c>ICRUDService</c> shape doesn't fit.
/// </summary>
public interface IRecommenderService
{
    /// <summary>Top-N ranked, explainable suggestions for the calling patient (resolved from the JWT - never a route/body id).</summary>
    Task<List<AppointmentRecommendationDto>> GetRecommendationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Records one real view/search interaction for the calling patient (doc §3) - this is the only way a <c>RecommenderInteraction</c> row is ever created.</summary>
    Task LogInteractionAsync(RecommenderInteractionRequest request, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: `RecommenderService.cs` — skeleton, DI, and interaction logging**

Start the file with usings, the class, constructor, and `LogInteractionAsync` (the simpler of the two public methods) plus the `CurrentUser`/`CurrentUserId` helpers copied from the same pattern in `MedicalRecordService`/`AppointmentService`:

```csharp
using System.Security.Claims;
using ClinicNow.Model.Common;
using ClinicNow.Model.Configuration;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Services.Appointments;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ClinicNow.Services.Recommender;

public class RecommenderService : IRecommenderService
{
    private const string ModelCacheKey = "recommender:model";

    private readonly ClinicNowContext _context;
    private readonly IAppointmentService _appointmentService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMemoryCache _cache;
    private readonly RecommenderOptions _options;
    private readonly ILogger<RecommenderService> _logger;

    public RecommenderService(
        ClinicNowContext context,
        IAppointmentService appointmentService,
        IHttpContextAccessor httpContextAccessor,
        IMemoryCache cache,
        RecommenderOptions options,
        ILogger<RecommenderService> logger)
    {
        _context = context;
        _appointmentService = appointmentService;
        _httpContextAccessor = httpContextAccessor;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task LogInteractionAsync(RecommenderInteractionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.DoctorId is null && request.MedicalServiceId is null)
        {
            // A row with neither can never be turned into a feature row (see
            // BuildInteractionHistoryRows below) - rejecting it here is what
            // makes "every collected signal is actually used" true rather
            // than aspirational (recommender-dokumentacija.md §3).
            throw new ValidationException("doctorId", "Interakcija mora biti vezana za doktora ili uslugu.");
        }

        if (request.DoctorId.HasValue && !await _context.Doctors.AnyAsync(d => d.Id == request.DoctorId.Value, cancellationToken))
        {
            throw new ValidationException("doctorId", "Odabrani doktor ne postoji.");
        }

        if (request.MedicalServiceId.HasValue && !await _context.MedicalServices.AnyAsync(s => s.Id == request.MedicalServiceId.Value, cancellationToken))
        {
            throw new ValidationException("medicalServiceId", "Odabrana usluga ne postoji.");
        }

        _context.RecommenderInteractions.Add(new RecommenderInteraction
        {
            UserId = CurrentUserId(CurrentUser()),
            InteractionType = request.Type,
            DoctorId = request.DoctorId,
            MedicalServiceId = request.MedicalServiceId,
            DateTimeUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    private ClaimsPrincipal CurrentUser() =>
        _httpContextAccessor.HttpContext?.User ?? throw new AuthenticationException("Nema aktivne sesije.");

    private static int CurrentUserId(ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    private async Task<int> GetOwnPatientIdAsync(int userId, CancellationToken cancellationToken)
    {
        var patient = await _context.Patients.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Nije pronađen medicinski karton za ovaj nalog.");
        return patient.Id;
    }
}
```

- [ ] **Step 5: Verify it builds with just Step 4 in place**

```bash
cd ClinicNow && dotnet build ClinicNow.Services/ClinicNow.Services.csproj
```
Expected: 0 errors (an "unused field" warning for `_appointmentService`/`_cache`/`_options` at this point is fine - Step 6+ uses them).

- [ ] **Step 6: Add the ML.NET row types and the pipeline builder**

Append to `RecommenderService.cs` (still inside the class):

```csharp
    // --- ML.NET feature rows -----------------------------------------------------

    /// <summary>
    /// One "content" row fed to the pipeline - either a training-catalog row,
    /// a patient history row, or a scoring candidate. Column names here are
    /// exactly what recommender-dokumentacija.md §4's pipeline diagram names.
    /// </summary>
    private class RecommenderFeatureRow
    {
        public string MedicalServiceText { get; set; } = string.Empty;
        public string SpecializationText { get; set; } = string.Empty;
        public string DoctorText { get; set; } = string.Empty;
        public string DayOfWeek { get; set; } = string.Empty;
        public string TimeOfDay { get; set; } = string.Empty;
    }

    private class RecommenderFeaturizedRow
    {
        [VectorType]
        public float[] Features { get; set; } = [];
    }

    private static RecommenderFeatureRow BuildFeatureRow(
        Doctor doctor, ClinicNow.Services.Database.Entities.MedicalService? medicalService, DateTime pointInTimeUtc)
    {
        var specializationText = string.Join(' ', doctor.DoctorSpecializations.Select(ds => ds.Specialization.Name));
        var doctorText = $"{doctor.User.FirstName} {doctor.User.LastName}";
        var medicalServiceText = medicalService is null
            ? string.Empty
            : $"{medicalService.Name} {medicalService.Description}".Trim();

        return new RecommenderFeatureRow
        {
            MedicalServiceText = medicalServiceText,
            SpecializationText = specializationText,
            DoctorText = doctorText,
            DayOfWeek = pointInTimeUtc.DayOfWeek.ToString(),
            TimeOfDay = pointInTimeUtc.ToTimeOfDayBucket().ToString()
        };
    }

    private static IEstimator<ITransformer> BuildPipeline(MLContext mlContext) =>
        mlContext.Transforms.Text.FeaturizeText("MedicalServiceFeat", nameof(RecommenderFeatureRow.MedicalServiceText))
            .Append(mlContext.Transforms.Text.FeaturizeText("SpecializationFeat", nameof(RecommenderFeatureRow.SpecializationText)))
            .Append(mlContext.Transforms.Text.FeaturizeText("DoctorFeat", nameof(RecommenderFeatureRow.DoctorText)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("DayOfWeekFeat", nameof(RecommenderFeatureRow.DayOfWeek)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("TimeOfDayFeat", nameof(RecommenderFeatureRow.TimeOfDay)))
            .Append(mlContext.Transforms.Concatenate("Features", "MedicalServiceFeat", "SpecializationFeat", "DoctorFeat", "DayOfWeekFeat", "TimeOfDayFeat"));

    /// <summary>
    /// Builds/loads the trained transformer, cached at the application level
    /// (doc §7: "teški resursi ... dijele se na nivou aplikacije uz
    /// odgovarajuće keširanje") for <see cref="RecommenderOptions.RetrainIntervalMinutes"/>
    /// before the next call retrains (doc §7: "model se osvježava periodično").
    /// </summary>
    private async Task<(MLContext MlContext, ITransformer Model)> GetOrTrainModelAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(ModelCacheKey, out (MLContext MlContext, ITransformer Model) cached))
        {
            return cached;
        }

        var mlContext = new MLContext(seed: 0);
        ITransformer model;

        if (File.Exists(_options.ModelPath))
        {
            _logger.LogInformation("Loading recommender model from {Path}.", _options.ModelPath);
            model = mlContext.Model.Load(_options.ModelPath, out _);
        }
        else
        {
            _logger.LogInformation("No recommender model found at {Path} - training a new one.", _options.ModelPath);
            model = await TrainAndSaveAsync(mlContext, cancellationToken);
        }

        _cache.Set(ModelCacheKey, (mlContext, model), TimeSpan.FromMinutes(_options.RetrainIntervalMinutes));
        return (mlContext, model);
    }

    private async Task<ITransformer> TrainAndSaveAsync(MLContext mlContext, CancellationToken cancellationToken)
    {
        var doctors = await _context.Doctors
            .Include(d => d.User)
            .Include(d => d.DoctorSpecializations).ThenInclude(ds => ds.Specialization)
            .ToListAsync(cancellationToken);
        var services = await _context.MedicalServices.ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        // The real catalog: every (Doctor, MedicalService) combination "at
        // this moment" - gives the text featurizers real vocabulary to learn
        // from (doc §7: "nad skupom svih usluga/termina").
        var catalogRows = doctors.SelectMany(d => services.Select(s => BuildFeatureRow(d, s, now))).ToList();

        // OneHotEncoding only learns categories it has actually seen during
        // Fit. With only 2 seeded doctors, the real catalog above might not
        // exercise all 7 DayOfWeek / 3 TimeOfDay values, which would make
        // the encoder silently zero out any candidate/history row that later
        // lands on an unseen day/time bucket - a real correctness bug, not a
        // hypothetical one. These 10 extra rows exist purely to guarantee
        // every category value is present in the training data at least
        // once; their text fields are intentionally empty so they don't
        // skew the TF-IDF vocabulary.
        var vocabularySeedRows = Enum.GetValues<DayOfWeek>()
            .Select(day => new RecommenderFeatureRow { DayOfWeek = day.ToString(), TimeOfDay = TimeOfDayBucket.Morning.ToString() })
            .Concat(Enum.GetValues<TimeOfDayBucket>().Select(bucket => new RecommenderFeatureRow { DayOfWeek = DayOfWeek.Monday.ToString(), TimeOfDay = bucket.ToString() }))
            .ToList();

        var trainingData = mlContext.Data.LoadFromEnumerable(catalogRows.Concat(vocabularySeedRows));
        var pipeline = BuildPipeline(mlContext);
        var model = pipeline.Fit(trainingData);

        var directory = Path.GetDirectoryName(Path.GetFullPath(_options.ModelPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        mlContext.Model.Save(model, trainingData.Schema, _options.ModelPath);
        _logger.LogInformation("Recommender model trained on {Count} catalog rows and saved to {Path}.", catalogRows.Count, _options.ModelPath);

        return model;
    }

    private static float[][] Featurize(MLContext mlContext, ITransformer model, List<RecommenderFeatureRow> rows)
    {
        if (rows.Count == 0) return [];
        var dataView = mlContext.Data.LoadFromEnumerable(rows);
        var transformed = model.Transform(dataView);
        return mlContext.Data.CreateEnumerable<RecommenderFeaturizedRow>(transformed, reuseRowObject: false)
            .Select(r => r.Features)
            .ToArray();
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        var length = Math.Min(a.Length, b.Length);
        for (var i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
```

- [ ] **Step 7: Verify it builds with Step 6 in place**

```bash
cd ClinicNow && dotnet build ClinicNow.Services/ClinicNow.Services.csproj
```
Expected: 0 errors.

- [ ] **Step 8: Add the weighted history model (completed appointments + interactions) and candidate generation**

Append to the class:

```csharp
    /// <summary>One weighted item in the patient's "taste profile" (doc §5.2's `H`) - either a past completed/confirmed appointment or a logged interaction.</summary>
    private record HistoryItem(RecommenderFeatureRow Features, double Weight, int? DoctorId, int? MedicalServiceId, bool FromAppointment, string? DoctorLastName, string? MedicalServiceName, DateTime DateTimeUtc);

    private static double RecencyFactor(DateTime pointInTimeUtc, DateTime nowUtc) =>
        1.0 / (1.0 + (nowUtc - pointInTimeUtc).TotalDays / 30.0);

    /// <summary>Builds `H` from real `Appointment` rows (doc §3 row 1: status Completed/Confirmed) - `frequencyFactor(h)` is the count of history items sharing the same (Doctor, MedicalService) pair (repeat visits weigh more).</summary>
    private async Task<List<HistoryItem>> BuildAppointmentHistoryAsync(int patientId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var appointments = await _context.Appointments
            .Include(a => a.Doctor).ThenInclude(d => d.User)
            .Include(a => a.Doctor).ThenInclude(d => d.DoctorSpecializations).ThenInclude(ds => ds.Specialization)
            .Include(a => a.MedicalService)
            .Where(a => a.PatientId == patientId && (a.Status == Model.Common.AppointmentStatus.Completed || a.Status == Model.Common.AppointmentStatus.Confirmed))
            .ToListAsync(cancellationToken);

        return appointments.Select(a =>
        {
            var frequencyFactor = appointments.Count(x => x.DoctorId == a.DoctorId && x.MedicalServiceId == a.MedicalServiceId);
            var weight = frequencyFactor * RecencyFactor(a.StartUtc, nowUtc);
            return new HistoryItem(
                BuildFeatureRow(a.Doctor, a.MedicalService, a.StartUtc), weight,
                a.DoctorId, a.MedicalServiceId, FromAppointment: true,
                a.Doctor.User.LastName, a.MedicalService.Name, a.StartUtc);
        }).ToList();
    }

    /// <summary>
    /// Extends `H` with real interaction rows (doc §3 row 7 - "pojačanje
    /// (boost) profila"): every `RecommenderInteraction` this patient's user
    /// account produced in the last 180 days becomes its own weighted
    /// pseudo-history item, so the signal is never merely stored - it is
    /// scored exactly like an appointment, just at a smaller base weight
    /// (0.4x - a view/search is a weaker taste signal than a completed
    /// visit). A DoctorView row has no service (MedicalServiceText stays
    /// empty in its feature row) and vice versa for MedicalServiceView -
    /// FeaturizeText handles an empty string as a valid, near-zero vector,
    /// so this doesn't break the concatenated feature dimensionality.
    /// </summary>
    private async Task<List<HistoryItem>> BuildInteractionHistoryRows(int userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        const double InteractionBaseWeight = 0.4;
        var cutoff = nowUtc.AddDays(-180);

        var interactions = await _context.RecommenderInteractions
            .Include(i => i.Doctor).ThenInclude(d => d!.User)
            .Include(i => i.Doctor).ThenInclude(d => d!.DoctorSpecializations).ThenInclude(ds => ds.Specialization)
            .Include(i => i.MedicalService)
            .Where(i => i.UserId == userId && i.DateTimeUtc >= cutoff)
            .ToListAsync(cancellationToken);

        var result = new List<HistoryItem>();
        foreach (var interaction in interactions)
        {
            if (interaction.Doctor is null && interaction.MedicalService is null) continue; // defensive - LogInteractionAsync already forbids this combination

            var frequencyFactor = interactions.Count(x =>
                x.InteractionType == interaction.InteractionType &&
                x.DoctorId == interaction.DoctorId &&
                x.MedicalServiceId == interaction.MedicalServiceId);
            var weight = InteractionBaseWeight * frequencyFactor * RecencyFactor(interaction.DateTimeUtc, nowUtc);

            var doctor = interaction.Doctor;
            var featureRow = doctor is not null
                ? BuildFeatureRow(doctor, interaction.MedicalService, interaction.DateTimeUtc)
                : new RecommenderFeatureRow
                {
                    MedicalServiceText = $"{interaction.MedicalService!.Name} {interaction.MedicalService.Description}".Trim(),
                    DayOfWeek = interaction.DateTimeUtc.DayOfWeek.ToString(),
                    TimeOfDay = interaction.DateTimeUtc.ToTimeOfDayBucket().ToString()
                };

            result.Add(new HistoryItem(
                featureRow, weight, interaction.DoctorId, interaction.MedicalServiceId, FromAppointment: false,
                doctor?.User.LastName, interaction.MedicalService?.Name, interaction.DateTimeUtc));
        }
        return result;
    }

    /// <summary>
    /// Real candidate free slots (doc §5.2's `C`): for every (Doctor,
    /// MedicalService) pair not already actively booked by this patient, the
    /// earliest genuinely free slot within <see cref="RecommenderOptions.CandidateLookaheadDays"/>,
    /// reusing <see cref="IAppointmentService.GetAvailableSlotsAsync"/> - the
    /// same real-slot computation the booking flow itself uses (rulebook §7:
    /// only real free slots offered, never a synthetic guess). Bounded to a
    /// demo-scale catalog (a handful of doctors/services) by design - a
    /// larger clinic would need a batched slot query instead of one
    /// call per day per pair, which is out of scope for this seminar project.
    /// </summary>
    private async Task<List<(Doctor Doctor, ClinicNow.Services.Database.Entities.MedicalService MedicalService, DateTime StartUtc)>> BuildCandidatesAsync(
        int patientId, List<Doctor> doctors, List<ClinicNow.Services.Database.Entities.MedicalService> services, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var alreadyBooked = await _context.Appointments
            .Where(a => a.PatientId == patientId && (a.Status == Model.Common.AppointmentStatus.Pending || a.Status == Model.Common.AppointmentStatus.Confirmed))
            .Select(a => new { a.DoctorId, a.MedicalServiceId })
            .ToListAsync(cancellationToken);

        var candidates = new List<(Doctor, ClinicNow.Services.Database.Entities.MedicalService, DateTime)>();

        foreach (var doctor in doctors)
        {
            foreach (var service in services)
            {
                if (alreadyBooked.Any(b => b.DoctorId == doctor.Id && b.MedicalServiceId == service.Id)) continue;

                for (var offset = 0; offset < _options.CandidateLookaheadDays; offset++)
                {
                    var date = DateOnly.FromDateTime(nowUtc.AddDays(offset));
                    var slots = await _appointmentService.GetAvailableSlotsAsync(doctor.Id, service.Id, date, cancellationToken);
                    if (slots.Count > 0)
                    {
                        candidates.Add((doctor, service, slots[0]));
                        break;
                    }
                }
            }
        }

        return candidates;
    }
```

- [ ] **Step 9: Verify it builds with Step 8 in place**

```bash
cd ClinicNow && dotnet build ClinicNow.Services/ClinicNow.Services.csproj
```
Expected: 0 errors.

- [ ] **Step 10: Add popularity fallback + the public `GetRecommendationsAsync` orchestration + Bosnian reason text**

Append to the class:

```csharp
    /// <summary>Cold-start fallback (doc §2.2): most-booked (Doctor, MedicalService) pairs in the last <see cref="RecommenderOptions.PopularityWindowDays"/> days, via one GROUP BY - no per-user history required.</summary>
    private async Task<List<AppointmentRecommendationDto>> GetPopularityFallbackAsync(int patientId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var windowStart = nowUtc.AddDays(-_options.PopularityWindowDays);

        var popular = await _context.Appointments
            .Where(a => a.CreatedAtUtc >= windowStart && a.Status != Model.Common.AppointmentStatus.Cancelled)
            .GroupBy(a => new { a.DoctorId, a.MedicalServiceId })
            .Select(g => new { g.Key.DoctorId, g.Key.MedicalServiceId, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(_options.TopN)
            .ToListAsync(cancellationToken);

        var doctors = await _context.Doctors.Include(d => d.User).Include(d => d.Location)
            .Include(d => d.DoctorSpecializations).ThenInclude(ds => ds.Specialization)
            .ToDictionaryAsync(d => d.Id, cancellationToken);
        var services = await _context.MedicalServices.ToDictionaryAsync(s => s.Id, cancellationToken);

        var alreadyBooked = await _context.Appointments
            .Where(a => a.PatientId == patientId && (a.Status == Model.Common.AppointmentStatus.Pending || a.Status == Model.Common.AppointmentStatus.Confirmed))
            .Select(a => new { a.DoctorId, a.MedicalServiceId })
            .ToListAsync(cancellationToken);

        var result = new List<AppointmentRecommendationDto>();
        foreach (var row in popular)
        {
            if (alreadyBooked.Any(b => b.DoctorId == row.DoctorId && b.MedicalServiceId == row.MedicalServiceId)) continue;
            if (!doctors.TryGetValue(row.DoctorId, out var doctor) || !services.TryGetValue(row.MedicalServiceId, out var service)) continue;

            DateTime? suggestedStart = null;
            for (var offset = 0; offset < _options.CandidateLookaheadDays; offset++)
            {
                var slots = await _appointmentService.GetAvailableSlotsAsync(doctor.Id, service.Id, DateOnly.FromDateTime(nowUtc.AddDays(offset)), cancellationToken);
                if (slots.Count > 0) { suggestedStart = slots[0]; break; }
            }
            if (suggestedStart is null) continue;

            result.Add(new AppointmentRecommendationDto
            {
                DoctorId = doctor.Id,
                DoctorName = $"{doctor.User.FirstName} {doctor.User.LastName}",
                DoctorSpecializations = doctor.DoctorSpecializations.Select(ds => ds.Specialization.Name).ToList(),
                MedicalServiceId = service.Id,
                MedicalServiceName = service.Name,
                MedicalServicePrice = service.Price,
                LocationId = doctor.LocationId,
                LocationName = doctor.Location.Name,
                SuggestedStartUtc = suggestedStart.Value,
                Score = 0,
                Reason = $"Popularno ove sedmice: {service.Name} je jedan od najčešće zakazivanih usluga.",
                IsPopularityFallback = true
            });
        }
        return result;
    }

    public async Task<List<AppointmentRecommendationDto>> GetRecommendationsAsync(CancellationToken cancellationToken = default)
    {
        var patientId = await GetOwnPatientIdAsync(CurrentUserId(CurrentUser()), cancellationToken);
        var nowUtc = DateTime.UtcNow;

        var appointmentHistory = await BuildAppointmentHistoryAsync(patientId, nowUtc, cancellationToken);
        var interactionHistory = await BuildInteractionHistoryRows(CurrentUserId(CurrentUser()), nowUtc, cancellationToken);
        var history = appointmentHistory.Concat(interactionHistory).Where(h => h.Weight > 0).ToList();

        // Cold start (doc §2.2): no usable history at all -> popularity fallback outright.
        if (history.Count == 0)
        {
            return await GetPopularityFallbackAsync(patientId, nowUtc, cancellationToken);
        }

        var doctors = await _context.Doctors.Include(d => d.User).Include(d => d.Location)
            .Include(d => d.DoctorSpecializations).ThenInclude(ds => ds.Specialization).ToListAsync(cancellationToken);
        var services = await _context.MedicalServices.ToListAsync(cancellationToken);

        var candidates = await BuildCandidatesAsync(patientId, doctors, services, nowUtc, cancellationToken);
        if (candidates.Count == 0)
        {
            // Edge case (doc §9): every candidate already booked by this patient -> fall back to popularity.
            return await GetPopularityFallbackAsync(patientId, nowUtc, cancellationToken);
        }

        var (mlContext, model) = await GetOrTrainModelAsync(cancellationToken);

        var historyRows = history.Select(h => h.Features).ToList();
        var candidateRows = candidates.Select(c => BuildFeatureRow(c.Doctor, c.MedicalService, c.StartUtc)).ToList();

        var historyVectors = Featurize(mlContext, model, historyRows);
        var candidateVectors = Featurize(mlContext, model, candidateRows);

        var scored = new List<AppointmentRecommendationDto>();
        for (var c = 0; c < candidates.Count; c++)
        {
            double weightedSum = 0, weightTotal = 0;
            var dominantIndex = 0;
            var dominantContribution = double.NegativeInfinity;

            for (var h = 0; h < history.Count; h++)
            {
                var similarity = CosineSimilarity(candidateVectors[c], historyVectors[h]);
                var contribution = history[h].Weight * similarity;
                weightedSum += contribution;
                weightTotal += history[h].Weight;
                if (contribution > dominantContribution)
                {
                    dominantContribution = contribution;
                    dominantIndex = h;
                }
            }

            var score = weightTotal == 0 ? 0 : weightedSum / weightTotal;
            if (score <= 0) continue;

            var (doctor, service, startUtc) = candidates[c];
            scored.Add(new AppointmentRecommendationDto
            {
                DoctorId = doctor.Id,
                DoctorName = $"{doctor.User.FirstName} {doctor.User.LastName}",
                DoctorSpecializations = doctor.DoctorSpecializations.Select(ds => ds.Specialization.Name).ToList(),
                MedicalServiceId = service.Id,
                MedicalServiceName = service.Name,
                MedicalServicePrice = service.Price,
                LocationId = doctor.LocationId,
                LocationName = doctor.Location.Name,
                SuggestedStartUtc = startUtc,
                Score = score,
                Reason = BuildReason(history[dominantIndex], doctor, service),
                IsPopularityFallback = false
            });
        }

        var ranked = scored.OrderByDescending(r => r.Score).Take(_options.TopN).ToList();

        // Edge case (doc §9): candidates existed but none scored above 0 (e.g. a
        // brand-new doctor/service with no textual overlap with this patient's
        // history at all) -> popularity fallback rather than an empty list.
        return ranked.Count > 0 ? ranked : await GetPopularityFallbackAsync(patientId, nowUtc, cancellationToken);
    }

    /// <summary>Builds the Bosnian explanation from the single history item that contributed most to a candidate's score (doc §6 - grounded in the real dominant signal, never generic).</summary>
    private static string BuildReason(HistoryItem dominant, Doctor candidateDoctor, ClinicNow.Services.Database.Entities.MedicalService candidateService)
    {
        var dayBosnian = dominant.DateTimeUtc.DayOfWeek switch
        {
            DayOfWeek.Monday => "ponedjeljkom", DayOfWeek.Tuesday => "utorkom", DayOfWeek.Wednesday => "srijedom",
            DayOfWeek.Thursday => "četvrtkom", DayOfWeek.Friday => "petkom", DayOfWeek.Saturday => "subotom",
            _ => "nedjeljom"
        };
        var timeOfDayBosnian = dominant.DateTimeUtc.ToTimeOfDayBucket().ToDisplayName();

        if (dominant.DoctorId == candidateDoctor.Id && dominant.DoctorLastName is not null)
        {
            return dominant.FromAppointment
                ? $"Predlažemo ovaj termin jer ste ranije više puta birali dr. {dominant.DoctorLastName} {dayBosnian} {timeOfDayBosnian}."
                : $"Preporučeno jer ste nedavno pregledali profil dr. {dominant.DoctorLastName}.";
        }

        if (dominant.MedicalServiceId == candidateService.Id && dominant.MedicalServiceName is not null)
        {
            return dominant.FromAppointment
                ? $"Preporučeno na osnovu vaših prethodnih pregleda tipa „{dominant.MedicalServiceName}“."
                : $"Preporučeno jer ste nedavno pregledali uslugu „{dominant.MedicalServiceName}“.";
        }

        return $"Preporučeno na osnovu vaših ranijih navika zakazivanja, {dayBosnian} {timeOfDayBosnian}.";
    }
}
```

- [ ] **Step 11: Full build**

```bash
cd ClinicNow && dotnet build
```
Expected: `Build succeeded` across the whole solution, 0 warnings/errors.

- [ ] **Step 12: Commit**

```bash
git add ClinicNow.Services/Recommender/ ClinicNow.Services/ClinicNow.Services.csproj
git commit -m "feat(recommender): implement RecommenderService (ML.NET content-based pipeline + popularity fallback + interaction logging)"
```

---

## Task 4: `RecommendationController` + DI wiring

**Files:**
- Create: `ClinicNow.API/Controllers/RecommendationController.cs`
- Modify: `ClinicNow.API/Program.cs`

**Interfaces:**
- Consumes: `IRecommenderService` (Task 3), `RecommenderOptions` (Task 1), `Roles.Patient` (existing `ClinicNow.Model.Security.Roles`).
- Produces: `GET api/Recommendation/appointments`, `POST api/Recommendation/interaction` — consumed by the mobile app (Task 6/7).

- [ ] **Step 1: `RecommendationController.cs`**

```csharp
using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Recommender;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// Explainable appointment recommendations (recommender-dokumentacija.md).
/// Patient-only: this is a self-service, per-patient feature - staff/doctor/
/// admin have no equivalent use for it in this seminar's scope. `UserId` is
/// always resolved from the JWT inside <see cref="IRecommenderService"/>,
/// never from the route or body (rulebook §5).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Patient)]
public class RecommendationController : ControllerBase
{
    private readonly IRecommenderService _service;

    public RecommendationController(IRecommenderService service)
    {
        _service = service;
    }

    [HttpGet("appointments")]
    public async Task<ActionResult<List<AppointmentRecommendationDto>>> GetAppointmentRecommendations(CancellationToken cancellationToken) =>
        Ok(await _service.GetRecommendationsAsync(cancellationToken));

    [HttpPost("interaction")]
    public async Task<IActionResult> LogInteraction(RecommenderInteractionRequest request, CancellationToken cancellationToken)
    {
        await _service.LogInteractionAsync(request, cancellationToken);
        return NoContent();
    }
}
```

- [ ] **Step 2: Wire DI in `Program.cs`**

Add the `using`:
```csharp
using ClinicNow.Services.Recommender;
```

Add a new options instance next to the other `*Options` (near line 51, after `corsOptions`):
```csharp
var recommenderOptions = new RecommenderOptions();
```
...and register it alongside the other singletons (near line 58):
```csharp
builder.Services.AddSingleton(recommenderOptions);
```

Add `AddMemoryCache()` next to `AddHttpContextAccessor()`/`AddHttpClient()` (near line 247-248):
```csharp
builder.Services.AddMemoryCache();
```

Add the service registration in its own labeled section, after the "Medical record" block (after line 115):
```csharp
// --- Recommender (Phase 7) --------------------------------------------------------
builder.Services.AddScoped<IRecommenderService, RecommenderService>();
```

- [ ] **Step 3: Build**

```bash
cd ClinicNow && dotnet build
```
Expected: 0 errors.

- [ ] **Step 4: Live verify against the real Docker stack**

```bash
docker compose up -d --build clinicnow-api
```
Then, once healthy, log in as the seeded patient and hit both endpoints:
```bash
curl -s -X POST http://localhost:5203/api/auth/login -H "Content-Type: application/json" -d '{"email":"patient@clinicnow.test","password":"test"}'
```
Take the `accessToken` from the response, then:
```bash
curl -s http://localhost:5203/api/Recommendation/appointments -H "Authorization: Bearer <token>"
curl -s -X POST http://localhost:5203/api/Recommendation/interaction -H "Authorization: Bearer <token>" -H "Content-Type: application/json" -d '{"type":0,"doctorId":1}'
```
Expected: the GET returns a JSON array of up to `RECOMMENDER_TOP_N` objects, each with a non-empty `reason`; the POST returns `204 No Content`; re-running the GET a second time is fast (cached model) and a fresh `dotnet ef` / `docker logs clinicnow-api` shows the "training a new one" log line only on the *first* call, not the second. Also confirm a non-Patient token (e.g. staff) gets `403` on both endpoints, and an anonymous request gets `401`.

- [ ] **Step 5: Commit**

```bash
git add ClinicNow.API/Controllers/RecommendationController.cs ClinicNow.API/Program.cs
git commit -m "feat(recommender): add RecommendationController and wire DI"
```

---

## Task 5: `.env` config + `recommender-dokumentacija.md` addendum + `GOALS.md`

**Files:**
- Modify: `.env`, `.env.example`
- Modify: `recommender-dokumentacija.md`
- Modify: `GOALS.md`

- [ ] **Step 1: Add config to `.env.example`**

Insert a new section after the `# --- PayPal sandbox ...` block:
```
# --- Recommender (consumed by ClinicNow.API only - wired in Phase 7) -----------
RECOMMENDER_TOP_N=5
RECOMMENDER_POPULARITY_WINDOW_DAYS=90
RECOMMENDER_CANDIDATE_LOOKAHEAD_DAYS=14
RECOMMENDER_MODEL_PATH=recommender-model.zip
RECOMMENDER_RETRAIN_INTERVAL_MINUTES=30
```

- [ ] **Step 2: Mirror the same block into the real `.env`** (git-ignored, not committed) with the same values - the API container's working directory is writable, so the relative `recommender-model.zip` path resolves fine both bare (`dotnet run`) and under `docker compose` without any `docker-compose.yml` change (it already does `env_file: .env`, passing every new key through automatically).

- [ ] **Step 3: Add the addendum to `recommender-dokumentacija.md`**

Insert a new `## 5.3. Preciziranje `frequencyFactor` i doprinosa interakcija` section right after the existing `## 5. Algoritam sličnosti i bodovanje` section's `### 5.2. Bodovanje kandidata` (i.e. after line 141, before the `---` that starts `## 6`):

```markdown
### 5.3. Preciziranje `frequencyFactor` i doprinosa interakcija

`frequencyFactor(h)` iz poglavlja 5.2 definisan je kao broj ponavljanja iste
kombinacije **(doktor, usluga)** unutar historije pacijenta `H` - pacijent
koji je istom doktoru/usluzi dolazio više puta dobija veći uticaj tog
obrasca na rangiranje.

Stavke iz `RecommenderInteraction` (poglavlje 3, red "Interakcije") ulaze u
`H` kao dodatne, slabije ponderisane stavke - **baznom težinom 0.4** u
odnosu na obavljen termin (`w(i) = 0.4 · frequencyFactor(i) · recencyFactor(i)`,
ista formula skorašnjosti kao za termine), gdje je `frequencyFactor(i)` broj
ponavljanja iste kombinacije (`InteractionType`, doktor, usluga) unutar
posljednjih 180 dana. Time je zadovoljen zahtjev da se **svaki** prikupljeni
signal zaista koristi u bodovanju (poglavlje 3), a ne samo evidentira.

Kandidati (poglavlje 5.2's `C`) traže se kao najraniji stvarno slobodan
termin za svaki par (doktor, usluga) unutar konfigurabilnog prozora
(`RECOMMENDER_CANDIDATE_LOOKAHEAD_DAYS`, podrazumijevano 14 dana) - ograničeno
po dizajnu na obim demonstracionog kataloga (nekoliko doktora/usluga), u
skladu sa obimom seminarskog rada.
```

- [ ] **Step 4: Update `GOALS.md`**

Change row 10 of the requirement matrix (currently `| ☐ |`) to:
```
| 10 | Recommender (explainable) + `recommender-dokumentacija.md` | ML.NET content-based, reasons, signals actually written & used | ☑ |
```

Append a new progress-log entry (after the 2026-08-26 medical-record-feature entry) once Tasks 1-7 are verified live end-to-end, following the same voice/detail level as the existing entries (real verification commands run, real gotchas hit, not aspirational).

- [ ] **Step 5: Commit**

```bash
git add .env.example recommender-dokumentacija.md GOALS.md
git commit -m "docs(recommender): add .env keys, precise frequencyFactor/interaction-weight addendum, update GOALS matrix"
```

---

## Task 6: Mobile — model, provider, `RecommendationsScreen`

**Files:**
- Create: `UI/clinicnow_mobile/lib/models/recommendation.dart`
- Create: `UI/clinicnow_mobile/lib/providers/recommendation_provider.dart`
- Create: `UI/clinicnow_mobile/lib/screens/recommendations/recommendations_screen.dart`
- Modify: `UI/clinicnow_mobile/lib/layouts/app_shell.dart`

**Interfaces:**
- Consumes: `AppointmentRecommendationDto` shape (Task 1) over `GET api/Recommendation/appointments`/`POST api/Recommendation/interaction` (Task 4); `Doctor`/`MedicalService` models + `DoctorProvider`/`MedicalServiceProvider` (existing, `lib/models/doctor.dart`, `lib/models/medical_service.dart`, `lib/providers/doctor_provider.dart`, `lib/providers/medical_service_provider.dart`); `BookAppointmentScreen` (Task 7 adds the pre-fill constructor params this screen calls into).
- Produces: `AppointmentRecommendation.fromJson`, `RecommendationProvider.getRecommendations()`/`.logInteraction(...)`, `RecommendationsScreen` widget — consumed by `app_shell.dart` and (indirectly) by Task 7.

- [ ] **Step 1: `lib/models/recommendation.dart`**

```dart
/// Mirrors the backend's `AppointmentRecommendationDto`.
class AppointmentRecommendation {
  final int doctorId;
  final String doctorName;
  final List<String> doctorSpecializations;
  final int medicalServiceId;
  final String medicalServiceName;
  final double medicalServicePrice;
  final int locationId;
  final String locationName;
  final DateTime suggestedStartUtc;
  final double score;
  final String reason;
  final bool isPopularityFallback;

  AppointmentRecommendation({
    required this.doctorId,
    required this.doctorName,
    required this.doctorSpecializations,
    required this.medicalServiceId,
    required this.medicalServiceName,
    required this.medicalServicePrice,
    required this.locationId,
    required this.locationName,
    required this.suggestedStartUtc,
    required this.score,
    required this.reason,
    required this.isPopularityFallback,
  });

  factory AppointmentRecommendation.fromJson(Map<String, dynamic> json) => AppointmentRecommendation(
        doctorId: json['doctorId'] as int,
        doctorName: json['doctorName'] as String,
        doctorSpecializations: (json['doctorSpecializations'] as List<dynamic>? ?? []).map((e) => '$e').toList(),
        medicalServiceId: json['medicalServiceId'] as int,
        medicalServiceName: json['medicalServiceName'] as String,
        medicalServicePrice: (json['medicalServicePrice'] as num).toDouble(),
        locationId: json['locationId'] as int,
        locationName: json['locationName'] as String,
        suggestedStartUtc: DateTime.parse(json['suggestedStartUtc'] as String),
        score: (json['score'] as num).toDouble(),
        reason: json['reason'] as String,
        isPopularityFallback: json['isPopularityFallback'] as bool,
      );
}

/// Matches the backend's `InteractionType` enum (0=DoctorView, 1=MedicalServiceView, 2=Search) - sent as an int on the wire, same convention as `Appointment.status`.
enum InteractionType { doctorView, medicalServiceView, search }
```

- [ ] **Step 2: `lib/providers/recommendation_provider.dart`**

```dart
import 'dart:convert';

import 'package:http/http.dart' as http;

import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/recommendation.dart';

class RecommendationProvider extends BaseProvider<AppointmentRecommendation> {
  RecommendationProvider(AuthSession authSession) : super('Recommendation', authSession);

  @override
  AppointmentRecommendation fromJson(Map<String, dynamic> json) => AppointmentRecommendation.fromJson(json);

  Future<List<AppointmentRecommendation>> getRecommendations() async {
    final response = await http.get(buildUri('api/Recommendation/appointments'), headers: authHeaders());
    final list = decode(response) as List<dynamic>;
    return list.map((e) => fromJson(e as Map<String, dynamic>)).toList();
  }

  /// Fire-and-forget from the UI's point of view (view/search logging must
  /// never block or fail the action the user actually cares about) - callers
  /// wrap this in a try/catch that swallows errors, same pattern as
  /// `AppShell._refreshUnreadCount`'s background poll.
  Future<void> logInteraction({required InteractionType type, int? doctorId, int? medicalServiceId}) async {
    final response = await http.post(
      buildUri('api/Recommendation/interaction'),
      headers: authHeaders(),
      body: jsonEncode({
        'type': type.index,
        'doctorId': doctorId,
        'medicalServiceId': medicalServiceId,
      }),
    );
    decode(response, allowEmptyBody: true);
  }
}
```

- [ ] **Step 3: `lib/screens/recommendations/recommendations_screen.dart`**

```dart
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../models/doctor.dart';
import '../../models/medical_service.dart';
import '../../models/recommendation.dart';
import '../../providers/doctor_provider.dart';
import '../../providers/medical_service_provider.dart';
import '../../providers/recommendation_provider.dart';
import '../appointments/book_appointment_screen.dart';

/// "Preporuke" tab (PLAN.md Phase 7 item 4): the explainable, content-based
/// suggestion list (recommender-dokumentacija.md §6 - every card carries a
/// real `Reason`, never a generic "recommended for you"), plus a real
/// doctor/service search box - the genuine source of `InteractionType.search`
/// signals (doc §3), not a synthetic one.
class RecommendationsScreen extends StatefulWidget {
  const RecommendationsScreen({super.key});

  @override
  State<RecommendationsScreen> createState() => _RecommendationsScreenState();
}

class _RecommendationsScreenState extends State<RecommendationsScreen> {
  late final RecommendationProvider _recommendationProvider;
  late final DoctorProvider _doctorProvider;
  late final MedicalServiceProvider _serviceProvider;
  final _dateFormat = DateFormat('EEEE, dd.MM.yyyy HH:mm');
  final _searchController = TextEditingController();

  List<AppointmentRecommendation>? _recommendations;
  String? _error;

  bool _isSearching = false;
  List<Doctor> _doctorResults = [];
  List<MedicalService> _serviceResults = [];

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _recommendationProvider = RecommendationProvider(authSession);
    _doctorProvider = DoctorProvider(authSession);
    _serviceProvider = MedicalServiceProvider(authSession);
    _load();
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() => _error = null);
    try {
      final items = await _recommendationProvider.getRecommendations();
      if (mounted) setState(() => _recommendations = items);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    }
  }

  Future<void> _search(String query) async {
    if (query.trim().isEmpty) {
      setState(() {
        _doctorResults = [];
        _serviceResults = [];
      });
      return;
    }

    setState(() => _isSearching = true);
    try {
      final doctors = await _doctorProvider.getPaged({'name': query, 'pageSize': 10});
      final services = await _serviceProvider.getPaged({'name': query, 'pageSize': 10});
      if (!mounted) return;
      setState(() {
        _doctorResults = doctors.resultList;
        _serviceResults = services.resultList;
      });

      // A real search interaction - genuinely written by a real user action,
      // not synthesized. Best-effort: a failed log must never block search.
      try {
        await _recommendationProvider.logInteraction(
          type: InteractionType.search,
          doctorId: doctors.resultList.isNotEmpty ? doctors.resultList.first.id : null,
          medicalServiceId: services.resultList.isNotEmpty ? services.resultList.first.id : null,
        );
      } catch (_) {
        // best-effort logging - never surfaces to the user
      }
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _isSearching = false);
    }
  }

  Future<void> _openBookingFor({Doctor? doctor, MedicalService? service}) async {
    if (doctor != null) {
      try {
        await _recommendationProvider.logInteraction(type: InteractionType.doctorView, doctorId: doctor.id);
      } catch (_) {}
    }
    if (service != null) {
      try {
        await _recommendationProvider.logInteraction(type: InteractionType.medicalServiceView, medicalServiceId: service.id);
      } catch (_) {}
    }

    if (!mounted) return;
    final booked = await Navigator.of(context).push<bool>(MaterialPageRoute(
      builder: (_) => BookAppointmentScreen(initialDoctorId: doctor?.id, initialMedicalServiceId: service?.id),
    ));
    if (booked == true) _load();
  }

  @override
  Widget build(BuildContext context) {
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.all(12),
        children: [
          TextField(
            controller: _searchController,
            decoration: InputDecoration(
              border: const OutlineInputBorder(),
              labelText: 'Pretražite doktore i usluge',
              suffixIcon: _isSearching
                  ? const Padding(padding: EdgeInsets.all(12), child: SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2)))
                  : const Icon(Icons.search),
            ),
            onSubmitted: _search,
          ),
          if (_doctorResults.isNotEmpty || _serviceResults.isNotEmpty) ...[
            const SizedBox(height: 12),
            ..._doctorResults.map((d) => Card(
                  child: ListTile(
                    leading: const Icon(Icons.medical_services_outlined),
                    title: Text(d.fullName),
                    subtitle: Text(d.specializations.join(', ')),
                    onTap: () => _openBookingFor(doctor: d),
                  ),
                )),
            ..._serviceResults.map((s) => Card(
                  child: ListTile(
                    leading: const Icon(Icons.event_note_outlined),
                    title: Text(s.name),
                    subtitle: Text('${s.durationMinutes} min, ${s.price.toStringAsFixed(2)} KM'),
                    onTap: () => _openBookingFor(service: s),
                  ),
                )),
            const Divider(height: 32),
          ],
          Text('Preporučeno za vas', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          if (_error != null)
            Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error))
          else if (_recommendations == null)
            const Center(child: Padding(padding: EdgeInsets.all(24), child: CircularProgressIndicator()))
          else if (_recommendations!.isEmpty)
            const Text('Trenutno nema preporuka - zakažite prvi termin da bismo mogli personalizovati prijedloge.')
          else
            ..._recommendations!.map((r) => Card(
                  margin: const EdgeInsets.only(bottom: 12),
                  child: ListTile(
                    contentPadding: const EdgeInsets.all(12),
                    title: Text('${r.doctorName} — ${r.medicalServiceName}'),
                    subtitle: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('${r.locationName} · ${_dateFormat.format(r.suggestedStartUtc.toLocal())}'),
                        const SizedBox(height: 4),
                        Text(r.reason, style: const TextStyle(fontStyle: FontStyle.italic)),
                      ],
                    ),
                    isThreeLine: true,
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => Navigator.of(context).push<bool>(MaterialPageRoute(
                      builder: (_) => BookAppointmentScreen(initialDoctorId: r.doctorId, initialMedicalServiceId: r.medicalServiceId),
                    )).then((booked) { if (booked == true) _load(); }),
                  ),
                )),
        ],
      ),
    );
  }
}
```

- [ ] **Step 4: Wire the tab in `app_shell.dart`**

Replace the import block (add):
```dart
import '../screens/recommendations/recommendations_screen.dart';
```

Replace the placeholder body (currently `3 => const Center(child: Padding(... 'ClinicNow — preporuke i profil dolaze u narednim fazama.' ...))`) with:
```dart
        3 => const RecommendationsScreen(),
```

Replace the tab 3 `NavigationDestination` label/icons:
```dart
          NavigationDestination(
            icon: Icon(Icons.recommend_outlined),
            selectedIcon: Icon(Icons.recommend),
            label: 'Preporuke',
          ),
```

- [ ] **Step 5: `flutter analyze`**

```bash
cd ClinicNow/UI/clinicnow_mobile && flutter analyze
```
Expected: `No issues found!` (Task 7 must land first for this to actually pass, since `BookAppointmentScreen` doesn't yet accept `initialDoctorId`/`initialMedicalServiceId` - if running Task 6 in isolation, expect exactly those two "undefined named parameter" errors and no others; resolve by doing Task 7 next before considering Task 6 done).

- [ ] **Step 6: Commit** (fold into Task 7's commit if done back-to-back, since `flutter analyze` can't pass on Task 6 alone)

---

## Task 7: Mobile — booking-flow integration (pre-fill + "Preporučeno" banner + view logging)

**Files:**
- Modify: `UI/clinicnow_mobile/lib/screens/appointments/book_appointment_screen.dart`

**Interfaces:**
- Consumes: `RecommendationProvider`/`AppointmentRecommendation`/`InteractionType` (Task 6).
- Produces: `BookAppointmentScreen({int? initialDoctorId, int? initialMedicalServiceId})` — consumed by `RecommendationsScreen` (Task 6).

- [ ] **Step 1: Accept and apply the pre-fill constructor params**

Change the class declaration and constructor:
```dart
class BookAppointmentScreen extends StatefulWidget {
  final int? initialDoctorId;
  final int? initialMedicalServiceId;

  const BookAppointmentScreen({super.key, this.initialDoctorId, this.initialMedicalServiceId});

  @override
  State<BookAppointmentScreen> createState() => _BookAppointmentScreenState();
}
```

In `_BookAppointmentScreenState`, add a `RecommendationProvider` field and apply the pre-fill once options are loaded:
```dart
  late final RecommendationProvider _recommendationProvider;
```
In `initState`, alongside the other provider constructions:
```dart
    _recommendationProvider = RecommendationProvider(authSession);
```
At the end of `_loadOptions()` (after `setState`), apply the pre-fill and, if both are already known, kick off `_loadSlots()`:
```dart
    if (widget.initialDoctorId != null || widget.initialMedicalServiceId != null) {
      setState(() {
        if (widget.initialDoctorId != null) {
          _doctor = _doctors.where((d) => d.id == widget.initialDoctorId).firstOrNull;
        }
        if (widget.initialMedicalServiceId != null) {
          _service = _services.where((s) => s.id == widget.initialMedicalServiceId).firstOrNull;
        }
      });
      if (_doctor != null && _service != null) {
        await _loadSlots();
      }
    }
```
(`firstOrNull` needs `import 'package:collection/collection.dart';` — check `pubspec.yaml`/`pubspec.lock` first; if the `collection` package isn't already a transitive dependency available to import directly, use `_doctors.cast<Doctor?>().firstWhere((d) => d?.id == widget.initialDoctorId, orElse: () => null)` instead so no new package needs adding.)

- [ ] **Step 2: Log a view interaction on manual doctor/service selection**

In the doctor `DropdownButtonFormField`'s `onChanged`, after the existing `setState`, add (fire-and-forget, errors swallowed - matches the `RecommendationsScreen` pattern from Task 6):
```dart
                      if (value != null) {
                        _recommendationProvider.logInteraction(type: InteractionType.doctorView, doctorId: value.id).catchError((_) {});
                      }
```
Same for the service dropdown's `onChanged`, using `InteractionType.medicalServiceView` and `medicalServiceId: value.id`.

Add the import:
```dart
import '../../models/recommendation.dart';
import '../../providers/recommendation_provider.dart';
```

- [ ] **Step 3: Add the "Preporučeno" banner**

Add state:
```dart
  AppointmentRecommendation? _topRecommendation;
```
In `initState`, after `_loadOptions();`, fire a best-effort load (never blocks the booking flow if it fails):
```dart
    _loadTopRecommendation();
```
```dart
  Future<void> _loadTopRecommendation() async {
    try {
      final recommendations = await _recommendationProvider.getRecommendations();
      if (mounted && recommendations.isNotEmpty) {
        setState(() => _topRecommendation = recommendations.first);
      }
    } catch (_) {
      // Best-effort - a failed recommendation fetch must never block booking.
    }
  }
```
In `build`, right after the `AppBar`'s screen content starts (top of the `Column`'s `children`, before "1. Odaberite doktora"), add:
```dart
                  if (_topRecommendation != null) ...[
                    Card(
                      color: Theme.of(context).colorScheme.secondaryContainer,
                      child: ListTile(
                        leading: const Icon(Icons.recommend_outlined),
                        title: Text('Preporučeno: ${_topRecommendation!.doctorName} — ${_topRecommendation!.medicalServiceName}'),
                        subtitle: Text(_topRecommendation!.reason),
                        trailing: TextButton(
                          child: const Text('Odaberi'),
                          onPressed: () {
                            final recommendation = _topRecommendation!;
                            setState(() {
                              _doctor = _doctors.where((d) => d.id == recommendation.doctorId).firstOrNull;
                              _service = _services.where((s) => s.id == recommendation.medicalServiceId).firstOrNull;
                              _date = recommendation.suggestedStartUtc.toLocal();
                            });
                            _loadSlots();
                          },
                        ),
                      ),
                    ),
                    const SizedBox(height: 16),
                  ],
```
(Use the same `firstOrNull`/`firstWhere` fallback resolved in Step 1 — keep it consistent, don't introduce a second pattern.)

- [ ] **Step 4: `flutter analyze` + `flutter test`**

```bash
cd ClinicNow/UI/clinicnow_mobile && flutter analyze && flutter test
```
Expected: `No issues found!`, all tests passing.

- [ ] **Step 5: Live verify in the browser (dev-only Flutter web target, same pattern as every prior phase)**

```bash
cd ClinicNow/UI/clinicnow_mobile && flutter run -d chrome --web-port=5000 --dart-define=API_BASE_URL=http://localhost:5203/
```
Log in as `patient@clinicnow.test` / `test`. Confirm: the "Preporuke" tab loads a ranked list with real, non-generic reasons; searching a doctor/service name returns real results and tapping one opens booking pre-filled; from `RecommendationsScreen`, tapping a recommendation card opens `BookAppointmentScreen` with doctor+service already selected and slots already loading; inside `BookAppointmentScreen` opened plainly (from the "Termini" tab), a "Preporučeno" banner appears (once a recommendation exists) and "Odaberi" fills the form; changing the doctor dropdown manually doesn't throw (confirms the fire-and-forget interaction POST doesn't block the UI even if it fails).

- [ ] **Step 6: Commit both Task 6 and Task 7 together (Task 6 alone doesn't `flutter analyze` clean)**

```bash
git add UI/clinicnow_mobile/lib/models/recommendation.dart UI/clinicnow_mobile/lib/providers/recommendation_provider.dart UI/clinicnow_mobile/lib/screens/recommendations/recommendations_screen.dart UI/clinicnow_mobile/lib/layouts/app_shell.dart UI/clinicnow_mobile/lib/screens/appointments/book_appointment_screen.dart
git commit -m "feat(recommender): mobile Preporuke tab + booking-flow recommendation banner and interaction logging"
```

---

## Task 8: End-to-end verification + `GOALS.md` progress-log entry

**Files:**
- Modify: `GOALS.md` (progress log only - the matrix row was already flipped in Task 5)

- [ ] **Step 1: Full clean-DB verification**

```bash
cd ClinicNow && docker compose down -v && docker compose up -d --build
```
Wait for all 4 containers healthy. Confirm the `AddRecommenderInteractions` migration applied (check `docker compose logs clinicnow-api` for the migration name, or query `SELECT * FROM __EFMigrationsHistory` in the container).

- [ ] **Step 2: Exercise the two new patients' recommendations to confirm both the appointment-history path and the popularity fallback are reachable**

- `patient@clinicnow.test` (Patient Id=1, has Completed/Confirmed appointment history from the Phase 4 seed) → `GET api/Recommendation/appointments` should return content-based results (`isPopularityFallback: false`, `score > 0`) with a reason naming a real doctor/service from that patient's actual seeded history.
- Temporarily register a brand-new patient via `POST api/auth/register` (no history, no interactions) → `GET api/Recommendation/appointments` should return `isPopularityFallback: true` rows instead of an empty list or an error.

- [ ] **Step 3: Confirm role gating**

A Staff/Doctor/Administrator token on either recommender endpoint → `403`. Anonymous → `401`.

- [ ] **Step 4: Full solution + both Flutter apps clean**

```bash
cd ClinicNow && dotnet build
cd UI/clinicnow_mobile && flutter analyze && flutter test
cd ../clinicnow_desktop && flutter analyze && flutter test
```
Expected: all clean (the desktop app isn't touched by this phase, so this step is a regression check, not new work).

- [ ] **Step 5: Append the `GOALS.md` progress-log entry**

Write a real entry (dated, in the same voice as the existing log) describing what was actually verified in Steps 1-4 - which patient produced content-based results and which triggered the fallback, what the migration name was, and any real gotcha hit while implementing (e.g. if the OneHotEncoding vocabulary-seeding fix in Task 3 Step 8 turned out to matter in practice, say so with the actual evidence, not speculatively).

- [ ] **Step 6: Commit**

```bash
git add GOALS.md
git commit -m "docs: record Phase 7 (recommender) live verification in GOALS.md progress log"
```

---

## Self-Review Notes (for whoever executes this plan)

- **Spec coverage:** doc §1-§2 (purpose, hybrid approach) → Task 3 Steps 10 (fallback + orchestration). §3 (signals) → Task 2 (entity) + Task 3 Step 8 (both appointment and interaction history actually scored). §4 (feature engineering) → Task 3 Step 6 (`BuildFeatureRow`/`BuildPipeline`). §5 (cosine + weighted scoring) → Task 3 Steps 8/10 + the new §5.3 addendum (Task 5 Step 3). §6 (explainability) → `BuildReason` (Task 3 Step 10). §7 (model management/config) → `GetOrTrainModelAsync`/`RecommenderOptions` (Task 1/3). §8 (integration) → `RecommendationController` (Task 4) + mobile (Task 6/7). §9 (edge cases) → explicitly handled in `GetRecommendationsAsync` (empty history, no candidates, all-scored-zero all fall back to popularity; no free slots at all surfaces as an empty popularity list, never a 500).
- **Known simplification to flag to Ibrahim, not silently ship:** candidate generation (Task 3 Step 8) is O(doctors × services × lookaheadDays) real HTTP-free but still per-day service calls - fine at this seed data's scale (2 doctors × 5 services), called out in-line in the code comment and in the new doc §5.3 addendum so it's a documented, intentional scope decision rather than an oversight.
- **Type/name consistency check before starting Task 4:** `IRecommenderService` method names (`GetRecommendationsAsync`, `LogInteractionAsync`) must match exactly what `RecommendationController` (Task 4) calls - both were written together in this plan, but re-verify against the actual `RecommenderService.cs` file once Task 3 is done, since a manual edit mid-implementation is where these drift.
