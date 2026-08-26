# Phase 8 Payments Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A patient pays for a service at booking time via a real PayPal sandbox integration finalized server-side; staff/admin can refund (full or partial), and cancelling a paid appointment auto-refunds the remaining balance.

**Architecture:** New `Payment`/`PaymentItem`/`PaymentRefund` entities decoupled from the appointment state machine; a thin `IPayPalClient` wraps PayPal's REST Orders v2 API (OAuth2 client-credentials, create/capture/refund) via `IHttpClientFactory`; `PaymentService` (bespoke, like `AppointmentService`) owns ownership checks, the EUR conversion, and idempotency; the mobile app drives approval through an in-app `webview_flutter` screen watching for a sentinel return URL; the desktop app gets a Refund row-action on the existing `AppointmentScreen`.

**Tech Stack:** ASP.NET Core / EF Core / SQL Server (existing), PayPal REST API v2 (Orders, Captures, Refunds) via raw `HttpClient` — no PayPal SDK package, matching the project's "wrap external APIs in a clean service layer" rule — Flutter `webview_flutter` (new mobile dependency).

**Spec:** [docs/superpowers/specs/2026-08-26-phase8-payments-design.md](../specs/2026-08-26-phase8-payments-design.md) (validated via brainstorming with Ibrahim, 2026-08-26). Also: `CLAUDE.md` Part II §J, `PLAN.md` Phase 8.

## Global Constraints

- Server owns the price catalog: `AmountEur` is always computed server-side from `MedicalService.Price` via the fixed peg (`1 EUR = 1.95583 KM`) — never trusted from the client.
- Server-side finalization only: the backend calls PayPal's own Capture API and trusts only that response; the client's claim of "I approved it" is never sufficient on its own.
- Capture is idempotent — a repeated `capture` call on an already-`Paid` payment must not call PayPal again or double-notify.
- At most one `Paid` `Payment` per `Appointment` (enforced both in the service and via a DB filtered unique index).
- Refund amount is always validated against the *actual* remaining balance (`AmountEur - sum(refunds)`), computed server-side.
- `userId`/`patientId` for ownership always resolved from the JWT (`IHttpContextAccessor` + `ClaimTypes.NameIdentifier`), never from route/body — same pattern as `AppointmentService`/`RecommenderService`.
- `[Authorize]` role gating: creating/capturing a payment is `Roles.Patient` only; manual refund is `Roles.Administrator`/`Roles.Staff` only.
- No dead code, no `Console.WriteLine`/`.Result`/`.Wait()`, `ILogger<T>` for every PayPal call failure, custom exceptions (`BusinessException`/`ValidationException`/`ForbiddenException`) mapped by the existing `ExceptionFilter` — never a raw exception or leaked PayPal error detail to the client.
- **Verification convention for this codebase:** no backend automated test project; every phase is verified live (build clean, migration applied, endpoints exercised against a real running instance). This plan follows that pattern. **One added wrinkle specific to this phase:** PayPal's sandbox approval page requires an interactive login a curl-driven subagent cannot complete — Task 8 verifies everything that curl *can* prove (order creation, capture-before-approval correctly failing, role gating, refund math) and flags the one step (a full approve→capture→refund cycle) that needs either the user completing one payment through the running app, or the controller session using its own browser tools with a PayPal sandbox buyer login.

---

## File Structure

**Backend — new files:**
- `ClinicNow.Model/Common/PaymentStatus.cs` — enum (`Pending`/`Paid`/`PartiallyRefunded`/`Refunded`) + `ToDisplayName()`.
- `ClinicNow.Model/Common/CurrencyConverter.cs` — the fixed KM→EUR peg conversion.
- `ClinicNow.Model/Configuration/PayPalOptions.cs` — `.env`-backed (`PAYPAL_CLIENT_ID`/`PAYPAL_CLIENT_SECRET`/`PAYPAL_MODE`).
- `ClinicNow.Model/Dto/PaymentDto.cs`
- `ClinicNow.Model/Requests/PaymentRequests.cs` — `PaymentCreateRequest`, `PaymentRefundRequest`.
- `ClinicNow.Services/Database/Entities/Payment.cs`, `PaymentItem.cs`, `PaymentRefund.cs`.
- `ClinicNow.Services/Database/Configurations/PaymentConfiguration.cs`, `PaymentItemConfiguration.cs`, `PaymentRefundConfiguration.cs`.
- `ClinicNow.Services/Payments/IPayPalClient.cs`, `PayPalClient.cs`, `PayPalModels.cs` (internal PayPal JSON shapes).
- `ClinicNow.Services/Payments/IPaymentService.cs`, `PaymentService.cs`.
- `ClinicNow.Services/Mapping/PaymentMappingConfig.cs`.
- `ClinicNow.API/Controllers/PaymentController.cs`.
- Generated migration under `ClinicNow.Services/Database/Migrations/`.

**Backend — modified files:**
- `ClinicNow.Services/Database/Entities/Appointment.cs` — new `Payments` navigation collection.
- `ClinicNow.Services/Database/ClinicNowContext.cs` — 3 new `DbSet`s.
- `ClinicNow.Model/Dto/AppointmentDto.cs` — `IsPaid`, `PaymentStatus`, `PaymentId`, `CanRefund`.
- `ClinicNow.Services/Mapping/AppointmentMappingConfig.cs` — `Ignore` the 4 new fields (computed in the service, same pattern as `AllowedActions`).
- `ClinicNow.Services/Appointments/AppointmentService.cs` — load `Payments` in `IncludeAll`, compute the 4 new DTO fields in `MapToDto`, call the auto-refund hook from `CancelAsync`, new `IPaymentService` constructor dependency.
- `ClinicNow.API/Program.cs` — `PayPalOptions` singleton, `IPayPalClient`/`IPaymentService` DI.

**Mobile (`clinicnow_mobile`) — new files:**
- `lib/models/payment.dart`, `lib/providers/payment_provider.dart`, `lib/screens/payments/payment_webview_screen.dart`.

**Mobile — modified files:**
- `lib/models/appointment.dart` — mirror the 4 new DTO fields.
- `lib/screens/appointments/book_appointment_screen.dart` — confirm dialog + payment step after a successful booking.
- `lib/screens/appointments/appointment_detail_screen.dart` — "Plaćeno" badge / "Plati" button.
- `pubspec.yaml` — add `webview_flutter`.

**Desktop (`clinicnow_desktop`) — new files:**
- `lib/providers/payment_provider.dart`.

**Desktop — modified files:**
- `lib/models/appointment.dart` — mirror the 4 new DTO fields.
- `lib/screens/appointments/appointment_screen.dart` — Refund row-action + dialog.

---

## Task 1: Model-layer types (enum, currency converter, PayPalOptions, DTOs, requests)

**Files:**
- Create: `ClinicNow.Model/Common/PaymentStatus.cs`
- Create: `ClinicNow.Model/Common/CurrencyConverter.cs`
- Create: `ClinicNow.Model/Configuration/PayPalOptions.cs`
- Create: `ClinicNow.Model/Dto/PaymentDto.cs`
- Create: `ClinicNow.Model/Requests/PaymentRequests.cs`

**Interfaces:**
- Produces: `PaymentStatus` enum + `PaymentStatusExtensions.ToDisplayName()`, `CurrencyConverter.ConvertKmToEur(decimal) : decimal`, `PayPalOptions{ClientId,ClientSecret,Mode,BaseUrl}`, `PaymentDto`, `PaymentCreateRequest{AppointmentId}`, `PaymentRefundRequest{Amount,Reason}` — consumed by every later task.

- [ ] **Step 1: `PaymentStatus.cs`**

```csharp
namespace ClinicNow.Model.Common;

/// <summary>
/// A `Payment`'s lifecycle. Fully decoupled from `AppointmentStatus` - paying
/// or refunding never changes the appointment's own state machine, and vice
/// versa (design doc §2/§9). `Pending` covers a created-but-not-yet-captured
/// PayPal order; there is no separate "Failed" state - a capture that never
/// succeeds just leaves the row `Pending`, and the patient simply retries
/// (which creates a fresh `Payment`, per design doc §3).
/// </summary>
public enum PaymentStatus
{
    Pending = 0,
    Paid = 1,
    PartiallyRefunded = 2,
    Refunded = 3
}

public static class PaymentStatusExtensions
{
    public static string ToDisplayName(this PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "Na čekanju",
        PaymentStatus.Paid => "Plaćeno",
        PaymentStatus.PartiallyRefunded => "Djelomično vraćeno",
        PaymentStatus.Refunded => "Vraćeno",
        _ => status.ToString()
    };
}
```

- [ ] **Step 2: `CurrencyConverter.cs`**

```csharp
namespace ClinicNow.Model.Common;

/// <summary>
/// PayPal doesn't support BAM (Bosnian convertible mark), so every charge is
/// made in EUR, converted server-side from `MedicalService.Price` (design doc
/// §2/§4 - "server owns the price catalog", never a client-supplied amount).
/// </summary>
public static class CurrencyConverter
{
    /// <summary>Bosnia's currency-board peg (fixed, not a market rate) - 1 EUR = 1.95583 KM.</summary>
    public const decimal EurToKmRate = 1.95583m;

    public static decimal ConvertKmToEur(decimal amountKm) =>
        Math.Round(amountKm / EurToKmRate, 2, MidpointRounding.AwayFromZero);
}
```

- [ ] **Step 3: `PayPalOptions.cs`**

```csharp
namespace ClinicNow.Model.Configuration;

/// <summary>
/// PayPal sandbox credentials, sourced from `.env` (rulebook Part II §C).
/// Mirrors `SmtpOptions`'s pattern: `GetOrDefault` with an empty-string
/// default rather than `Require`, so the API still boots cleanly in phases/
/// environments where PayPal isn't configured yet - `PayPalClient` itself is
/// where a genuinely missing credential surfaces, at the point it's actually
/// needed, not at startup.
/// </summary>
public class PayPalOptions : EnvOptionsBase
{
    public string ClientId { get; }
    public string ClientSecret { get; }
    public string Mode { get; }

    /// <summary>Sandbox vs live REST API base URL - never hardcoded elsewhere.</summary>
    public string BaseUrl => Mode.Equals("live", StringComparison.OrdinalIgnoreCase)
        ? "https://api-m.paypal.com"
        : "https://api-m.sandbox.paypal.com";

    public PayPalOptions()
    {
        ClientId = GetOrDefault("PAYPAL_CLIENT_ID", string.Empty);
        ClientSecret = GetOrDefault("PAYPAL_CLIENT_SECRET", string.Empty);
        Mode = GetOrDefault("PAYPAL_MODE", "sandbox");
    }
}
```

- [ ] **Step 4: `PaymentDto.cs`**

```csharp
namespace ClinicNow.Model.Dto;

/// <summary>
/// One payment attempt on one appointment (design doc §3). `ApproveUrl` is
/// only ever populated by `PaymentService.CreateAsync`'s response - every
/// other response leaves it null, since there's nothing left to approve.
/// </summary>
public class PaymentDto
{
    public int Id { get; set; }

    public int AppointmentId { get; set; }

    public decimal AmountEur { get; set; }

    public Common.PaymentStatus Status { get; set; }

    public string StatusName { get; set; } = string.Empty;

    public decimal RefundedAmountEur { get; set; }

    public decimal RemainingRefundableEur { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    /// <summary>The PayPal-hosted approval page URL - populated only on creation.</summary>
    public string? ApproveUrl { get; set; }
}
```

- [ ] **Step 5: `PaymentRequests.cs`**

```csharp
namespace ClinicNow.Model.Requests;

/// <summary>
/// Starts a payment for one appointment. There is deliberately no `AmountEur`
/// field - the server always computes it from the appointment's own
/// `MedicalService.Price` (design doc §2: never trust the client's amount).
/// </summary>
public class PaymentCreateRequest
{
    public int AppointmentId { get; set; }
}

/// <summary>A manual staff/admin refund - always requires a reason, same convention as `AppointmentCancelRequest`.</summary>
public class PaymentRefundRequest
{
    public decimal Amount { get; set; }

    public string Reason { get; set; } = string.Empty;
}
```

- [ ] **Step 6: Build & verify**

```bash
cd ClinicNow && dotnet build ClinicNow.Model/ClinicNow.Model.csproj
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add ClinicNow.Model/Common/PaymentStatus.cs ClinicNow.Model/Common/CurrencyConverter.cs ClinicNow.Model/Configuration/PayPalOptions.cs ClinicNow.Model/Dto/PaymentDto.cs ClinicNow.Model/Requests/PaymentRequests.cs
git commit -m "feat(payments): add model-layer types (PaymentStatus, CurrencyConverter, PayPalOptions, DTO, requests)"
```

---

## Task 2: `Payment`/`PaymentItem`/`PaymentRefund` entities + EF configuration + migration

**Files:**
- Create: `ClinicNow.Services/Database/Entities/Payment.cs`, `PaymentItem.cs`, `PaymentRefund.cs`
- Create: `ClinicNow.Services/Database/Configurations/PaymentConfiguration.cs`, `PaymentItemConfiguration.cs`, `PaymentRefundConfiguration.cs`
- Modify: `ClinicNow.Services/Database/Entities/Appointment.cs`
- Modify: `ClinicNow.Services/Database/ClinicNowContext.cs`
- Create (generated): migration under `ClinicNow.Services/Database/Migrations/`

**Interfaces:**
- Consumes: `PaymentStatus` (Task 1).
- Produces: `Payment{Id,AppointmentId,Appointment,AmountEur,Status,PayPalOrderId,PayPalCaptureId,CreatedAtUtc,PaidAtUtc,Items,Refunds}`, `PaymentItem{Id,PaymentId,Payment,MedicalServiceId,MedicalService,Description,AmountEur}`, `PaymentRefund{Id,PaymentId,Payment,AmountEur,PayPalRefundId,Reason,RefundedByUserId,RefundedByUser,RefundedAtUtc}`, `Appointment.Payments : ICollection<Payment>`, `ClinicNowContext.{Payments,PaymentItems,PaymentRefunds}` — consumed by Task 3+.

- [ ] **Step 1: `Payment.cs`**

```csharp
using ClinicNow.Model.Common;

namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// One payment attempt on one <see cref="Appointment"/> - fully decoupled
/// from the appointment's own state machine (design doc §3/§9). Multiple
/// rows can exist per appointment from abandoned attempts; the only hard
/// invariant is at most one <see cref="PaymentStatus.Paid"/> row per
/// appointment, enforced both in <c>PaymentService</c> and by a DB filtered
/// unique index (see <c>PaymentConfiguration</c>).
/// </summary>
public class Payment
{
    public int Id { get; set; }

    public int AppointmentId { get; set; }

    public Appointment Appointment { get; set; } = null!;

    public decimal AmountEur { get; set; }

    public PaymentStatus Status { get; set; }

    public string PayPalOrderId { get; set; } = string.Empty;

    public string? PayPalCaptureId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    public ICollection<PaymentItem> Items { get; set; } = [];

    public ICollection<PaymentRefund> Refunds { get; set; } = [];
}
```

- [ ] **Step 2: `PaymentItem.cs`**

```csharp
namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A priced line item under a <see cref="Payment"/> - mirrors the reference
/// repo's `Uplata`/`StavkaUplate` split (design doc §3). Today always exactly
/// one item per payment (the appointment's own service), but keeps the shape
/// the plan calls for rather than flattening the amount onto `Payment` itself.
/// </summary>
public class PaymentItem
{
    public int Id { get; set; }

    public int PaymentId { get; set; }

    public Payment Payment { get; set; } = null!;

    public int MedicalServiceId { get; set; }

    public MedicalService MedicalService { get; set; } = null!;

    public string Description { get; set; } = string.Empty;

    public decimal AmountEur { get; set; }
}
```

- [ ] **Step 3: `PaymentRefund.cs`**

```csharp
namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// One refund (automatic, on cancellation - or manual, staff/admin-triggered)
/// against a <see cref="Payment"/>. <see cref="Payment.Status"/> is always
/// derived from the sum of a payment's refunds vs. its <c>AmountEur</c> -
/// never a separately-maintained flag that could drift from the real total
/// (design doc §3).
/// </summary>
public class PaymentRefund
{
    public int Id { get; set; }

    public int PaymentId { get; set; }

    public Payment Payment { get; set; } = null!;

    public decimal AmountEur { get; set; }

    public string PayPalRefundId { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public int RefundedByUserId { get; set; }

    public User RefundedByUser { get; set; } = null!;

    public DateTime RefundedAtUtc { get; set; }
}
```

- [ ] **Step 4: Add the `Payments` navigation to `Appointment.cs`**

Add after the existing `AuditLogs` collection property (`ClinicNow.Services/Database/Entities/Appointment.cs`):

```csharp
    /// <summary>
    /// Every payment attempt against this appointment (Phase 8) - loaded via
    /// `AppointmentService.IncludeAll` so `AppointmentDto.IsPaid`/`PaymentStatus`
    /// never cost an extra per-row query (no N+1, rulebook Part II §D).
    /// </summary>
    public ICollection<Payment> Payments { get; set; } = [];
```

- [ ] **Step 5: `PaymentConfiguration.cs`**

Look up the real seeded `MedicalService.Price` values in `ClinicNow.Services/Database/Configurations/MedicalServiceConfiguration.cs` before writing this (confirmed already: Id=1 "Opći pregled" = 50.00 KM, Id=2 "Dermatološki pregled" = 80.00 KM, Id=5 "Kardiološki pregled" = 90.00 KM). Seeded against `Appointment` rows 1 (Completed), 2 (Cancelled), 3 (Confirmed) from `AppointmentConfiguration` - so every payment state is demoable on a clean DB.

```csharp
using ClinicNow.Model.Common;
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    private static readonly DateTime Seed = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.PayPalOrderId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.PayPalCaptureId).HasMaxLength(64);
        // decimal(8,2): same explicit money precision as MedicalService.Price -
        // never let EF's provider-default float precision silently apply.
        builder.Property(p => p.AmountEur).HasColumnType("decimal(8,2)");

        builder.HasOne(p => p.Appointment).WithMany(a => a.Payments).HasForeignKey(p => p.AppointmentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.AppointmentId);
        // Belt-and-suspenders for "at most one Paid payment per appointment"
        // (design doc §3) - a filtered unique index so the DB itself can never
        // end up with two Paid rows for the same appointment, even under a
        // race the service-layer check alone might not catch.
        builder.HasIndex(p => p.AppointmentId).HasFilter("[Status] = 1").IsUnique();

        builder.HasData(
            new Payment { Id = 1, AppointmentId = 1, AmountEur = 25.56m, Status = PaymentStatus.Paid, PayPalOrderId = "SEED-ORDER-0001", PayPalCaptureId = "SEED-CAPTURE-0001", CreatedAtUtc = Seed, PaidAtUtc = Seed },
            new Payment { Id = 2, AppointmentId = 2, AmountEur = 40.90m, Status = PaymentStatus.Refunded, PayPalOrderId = "SEED-ORDER-0002", PayPalCaptureId = "SEED-CAPTURE-0002", CreatedAtUtc = Seed, PaidAtUtc = Seed },
            new Payment { Id = 3, AppointmentId = 3, AmountEur = 46.02m, Status = PaymentStatus.PartiallyRefunded, PayPalOrderId = "SEED-ORDER-0003", PayPalCaptureId = "SEED-CAPTURE-0003", CreatedAtUtc = Seed, PaidAtUtc = Seed });
    }
}
```

- [ ] **Step 6: `PaymentItemConfiguration.cs`**

```csharp
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class PaymentItemConfiguration : IEntityTypeConfiguration<PaymentItem>
{
    public void Configure(EntityTypeBuilder<PaymentItem> builder)
    {
        builder.Property(i => i.Description).HasMaxLength(200).IsRequired();
        builder.Property(i => i.AmountEur).HasColumnType("decimal(8,2)");

        builder.HasOne(i => i.Payment).WithMany(p => p.Items).HasForeignKey(i => i.PaymentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.MedicalService).WithMany().HasForeignKey(i => i.MedicalServiceId).OnDelete(DeleteBehavior.Restrict);

        builder.HasData(
            new PaymentItem { Id = 1, PaymentId = 1, MedicalServiceId = 1, Description = "Opći pregled", AmountEur = 25.56m },
            new PaymentItem { Id = 2, PaymentId = 2, MedicalServiceId = 2, Description = "Dermatološki pregled", AmountEur = 40.90m },
            new PaymentItem { Id = 3, PaymentId = 3, MedicalServiceId = 5, Description = "Kardiološki pregled", AmountEur = 46.02m });
    }
}
```

`Cascade` on `Payment→PaymentItem` (unlike the `Restrict` used everywhere else off `Appointment`) is intentional: a `PaymentItem` has no independent existence or legal-retention requirement outside its parent `Payment` - deleting a `Payment` (which never happens through any app code path today, but keeps the model honest) should take its items with it.

- [ ] **Step 7: `PaymentRefundConfiguration.cs`**

```csharp
using ClinicNow.Services.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicNow.Services.Database.Configurations;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    private static readonly DateTime Seed = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.Property(r => r.PayPalRefundId).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(300).IsRequired();
        builder.Property(r => r.AmountEur).HasColumnType("decimal(8,2)");

        builder.HasOne(r => r.Payment).WithMany(p => p.Refunds).HasForeignKey(r => r.PaymentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.RefundedByUser).WithMany().HasForeignKey(r => r.RefundedByUserId).OnDelete(DeleteBehavior.Restrict);

        // Payment 2 (Cancelled appointment) -> refunded in full by Staff (User Id=2).
        // Payment 3 (Confirmed appointment) -> a partial goodwill refund by the
        // Administrator (User Id=1), leaving it PartiallyRefunded - both User
        // IDs confirmed against UserConfiguration.cs's HasData (Phase 7 Task 2
        // already verified: 1=Administrator, 2=Staff).
        builder.HasData(
            new PaymentRefund { Id = 1, PaymentId = 2, AmountEur = 40.90m, PayPalRefundId = "SEED-REFUND-0001", Reason = "Termin otkazan.", RefundedByUserId = 2, RefundedAtUtc = Seed },
            new PaymentRefund { Id = 2, PaymentId = 3, AmountEur = 15.00m, PayPalRefundId = "SEED-REFUND-0002", Reason = "Djelomični povrat na zahtjev pacijenta.", RefundedByUserId = 1, RefundedAtUtc = Seed });
    }
}
```

- [ ] **Step 8: Register the 3 new `DbSet`s in `ClinicNowContext.cs`**

Add after the existing `MedicalRecordEntries`/`RecommenderInteractions` lines:

```csharp
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentItem> PaymentItems => Set<PaymentItem>();
    public DbSet<PaymentRefund> PaymentRefunds => Set<PaymentRefund>();
```

- [ ] **Step 9: Generate the migration**

```bash
cd ClinicNow && dotnet ef migrations add AddPayments --project ClinicNow.Services --startup-project ClinicNow.API --output-dir Database/Migrations
```
(`--output-dir Database/Migrations` matches this repo's real layout — confirmed necessary in Phase 7 Task 2, since EF's default migrations folder differs from where this project's migrations actually live.) Read the generated `Up()` and confirm: 3 new tables (`Payments`, `PaymentItems`, `PaymentRefunds`) with the FKs/indexes above, the filtered unique index (`CREATE UNIQUE INDEX ... WHERE [Status] = 1` or EF's equivalent `HasFilter` SQL), and the 7 seed rows (3 payments, 3 items, 2 refunds) with the exact values above.

- [ ] **Step 10: Build & verify**

```bash
cd ClinicNow && dotnet build ClinicNow.Services/ClinicNow.Services.csproj
```
Expected: 0 warnings/errors.

- [ ] **Step 11: Commit**

```bash
git add ClinicNow.Services/Database/Entities/Payment.cs ClinicNow.Services/Database/Entities/PaymentItem.cs ClinicNow.Services/Database/Entities/PaymentRefund.cs ClinicNow.Services/Database/Entities/Appointment.cs ClinicNow.Services/Database/Configurations/PaymentConfiguration.cs ClinicNow.Services/Database/Configurations/PaymentItemConfiguration.cs ClinicNow.Services/Database/Configurations/PaymentRefundConfiguration.cs ClinicNow.Services/Database/ClinicNowContext.cs "ClinicNow.Services/Database/Migrations/*AddPayments*"
git commit -m "feat(payments): add Payment/PaymentItem/PaymentRefund entities, EF config, and migration"
```

---

## Task 3: `IPayPalClient`/`PayPalClient` (PayPal REST wrapper)

**Files:**
- Create: `ClinicNow.Services/Payments/IPayPalClient.cs`
- Create: `ClinicNow.Services/Payments/PayPalClient.cs`
- Create: `ClinicNow.Services/Payments/PayPalModels.cs`

**Interfaces:**
- Consumes: `PayPalOptions` (Task 1), `IHttpClientFactory`, `IMemoryCache` (both already globally registered in `Program.cs`).
- Produces: `IPayPalClient.{CreateOrderAsync,CaptureOrderAsync,RefundCaptureAsync}` — consumed by Task 4.

- [ ] **Step 1: `IPayPalClient.cs`**

```csharp
namespace ClinicNow.Services.Payments;

/// <summary>
/// Thin wrapper over PayPal's REST Orders v2 API (design doc §4). No PayPal
/// SDK package - a handful of well-understood REST calls via `IHttpClientFactory`
/// is simpler and matches CLAUDE.md's "wrap external API calls in a clean
/// service layer" rule better than an opaque third-party client.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Creates a PayPal order for the given EUR amount; returns the order id and the hosted approval URL the client must open.</summary>
    Task<(string OrderId, string ApproveUrl)> CreateOrderAsync(decimal amountEur, string returnUrl, string cancelUrl, CancellationToken cancellationToken = default);

    /// <summary>Captures a previously-approved order. `Success=false` means PayPal itself reports the capture didn't complete (e.g. the buyer never approved it) - never an exception for that specific, expected case.</summary>
    Task<(string? CaptureId, decimal CapturedAmountEur, bool Success)> CaptureOrderAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds part or all of a capture.</summary>
    Task<string> RefundCaptureAsync(string captureId, decimal amountEur, string reason, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: `PayPalModels.cs`**

PayPal's JSON is `snake_case`, unlike this project's own DTOs - every property here needs an explicit `[JsonPropertyName]` (there's no global snake_case naming policy configured in `Program.cs`, so `System.Text.Json` would otherwise look for exact-cased C# property names and silently leave everything null).

```csharp
using System.Text.Json.Serialization;

namespace ClinicNow.Services.Payments;

internal class PayPalTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal class PayPalMoney
{
    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = "EUR";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "0.00";
}

internal class PayPalLink
{
    [JsonPropertyName("href")]
    public string Href { get; set; } = string.Empty;

    [JsonPropertyName("rel")]
    public string Rel { get; set; } = string.Empty;
}

internal class PayPalOrderResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("links")]
    public List<PayPalLink> Links { get; set; } = [];
}

internal class PayPalCaptureResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnit> PurchaseUnits { get; set; } = [];
}

internal class PayPalPurchaseUnit
{
    [JsonPropertyName("payments")]
    public PayPalPayments? Payments { get; set; }
}

internal class PayPalPayments
{
    [JsonPropertyName("captures")]
    public List<PayPalCapture> Captures { get; set; } = [];
}

internal class PayPalCapture
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public PayPalMoney Amount { get; set; } = new();
}

internal class PayPalRefundResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}
```

- [ ] **Step 3: `PayPalClient.cs`**

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClinicNow.Model.Configuration;
using ClinicNow.Model.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ClinicNow.Services.Payments;

public class PayPalClient : IPayPalClient
{
    private const string TokenCacheKey = "paypal:access_token";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalClient> _logger;

    public PayPalClient(IHttpClientFactory httpClientFactory, IMemoryCache cache, PayPalOptions options, ILogger<PayPalClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task<(string OrderId, string ApproveUrl)> CreateOrderAsync(decimal amountEur, string returnUrl, string cancelUrl, CancellationToken cancellationToken = default)
    {
        var client = await CreateAuthorizedClientAsync(cancellationToken);

        var payload = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new { amount = new { currency_code = "EUR", value = amountEur.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) } }
            },
            application_context = new { return_url = returnUrl, cancel_url = cancelUrl, user_action = "PAY_NOW" }
        };

        var response = await client.PostAsJsonAsync("/v2/checkout/orders", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PayPal CreateOrder failed ({Status}): {Body}", response.StatusCode, body);
            throw new BusinessException("Plaćanje trenutno nije moguće. Pokušajte ponovo kasnije.");
        }

        var order = JsonSerializer.Deserialize<PayPalOrderResponse>(body)
            ?? throw new BusinessException("Plaćanje trenutno nije moguće. Pokušajte ponovo kasnije.");

        var approveUrl = order.Links.FirstOrDefault(l => l.Rel == "approve")?.Href
            ?? throw new BusinessException("PayPal nije vratio link za odobravanje plaćanja.");

        return (order.Id, approveUrl);
    }

    public async Task<(string? CaptureId, decimal CapturedAmountEur, bool Success)> CaptureOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var client = await CreateAuthorizedClientAsync(cancellationToken);

        var response = await client.PostAsync($"/v2/checkout/orders/{orderId}/capture",
            new StringContent("{}", Encoding.UTF8, "application/json"), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // A capture on an order the buyer never approved is an EXPECTED
            // failure (e.g. the patient closed the WebView without approving)
            // - log it, but return Success=false rather than throwing, so
            // PaymentService can leave the Payment row Pending for a retry
            // instead of surfacing a scary 500.
            _logger.LogWarning("PayPal CaptureOrder failed for order {OrderId} ({Status}): {Body}", orderId, response.StatusCode, body);
            return (null, 0m, false);
        }

        var capture = JsonSerializer.Deserialize<PayPalCaptureResponse>(body);
        var captured = capture?.PurchaseUnits.FirstOrDefault()?.Payments?.Captures.FirstOrDefault();

        if (captured is null || !string.Equals(captured.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("PayPal CaptureOrder for order {OrderId} did not complete: {Body}", orderId, body);
            return (null, 0m, false);
        }

        var capturedAmount = decimal.Parse(captured.Amount.Value, System.Globalization.CultureInfo.InvariantCulture);
        return (captured.Id, capturedAmount, true);
    }

    public async Task<string> RefundCaptureAsync(string captureId, decimal amountEur, string reason, CancellationToken cancellationToken = default)
    {
        var client = await CreateAuthorizedClientAsync(cancellationToken);

        var payload = new
        {
            amount = new { value = amountEur.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), currency_code = "EUR" },
            note_to_payer = reason
        };

        var response = await client.PostAsJsonAsync($"/v2/payments/captures/{captureId}/refund", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PayPal RefundCapture failed for capture {CaptureId} ({Status}): {Body}", captureId, response.StatusCode, body);
            throw new BusinessException("Povrat sredstava trenutno nije moguć. Pokušajte ponovo kasnije.");
        }

        var refund = JsonSerializer.Deserialize<PayPalRefundResponse>(body)
            ?? throw new BusinessException("Povrat sredstava trenutno nije moguć. Pokušajte ponovo kasnije.");

        return refund.Id;
    }

    // --- auth -----------------------------------------------------------------

    private async Task<HttpClient> CreateAuthorizedClientAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        return client;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cachedToken) && cachedToken is not null)
        {
            return cachedToken;
        }

        if (string.IsNullOrEmpty(_options.ClientId) || string.IsNullOrEmpty(_options.ClientSecret))
        {
            throw new BusinessException("PayPal integracija nije konfigurisana (nedostaje PAYPAL_CLIENT_ID/PAYPAL_CLIENT_SECRET).");
        }

        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_options.BaseUrl);

        var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("grant_type", "client_credentials")])
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

        var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PayPal OAuth token request failed ({Status}): {Body}", response.StatusCode, body);
            throw new BusinessException("PayPal integracija trenutno nije dostupna.");
        }

        var token = JsonSerializer.Deserialize<PayPalTokenResponse>(body)
            ?? throw new BusinessException("PayPal integracija trenutno nije dostupna.");

        // Cache until shortly before real expiry so a request never races an
        // about-to-expire token.
        _cache.Set(TokenCacheKey, token.AccessToken, TimeSpan.FromSeconds(Math.Max(60, token.ExpiresIn - 60)));
        return token.AccessToken;
    }
}
```

- [ ] **Step 4: Build & verify**

```bash
cd ClinicNow && dotnet build ClinicNow.Services/ClinicNow.Services.csproj
```
Expected: 0 errors.

- [ ] **Step 5: Live verify against the real PayPal sandbox** (this is the one piece of Task 3 worth checking in isolation, since a mistake here is expensive to debug later buried inside `PaymentService`)

Write and run a tiny throwaway console check (or a temporary xUnit-less `Program.cs` snippet, deleted afterward) that resolves `PayPalOptions` from the real `.env` and calls `PayPalClient.CreateOrderAsync(10.00m, "https://clinicnow.local/payment-return", "https://clinicnow.local/payment-cancel")` directly - confirm it returns a real order id (a long alphanumeric PayPal order id, e.g. `5O190127TN364715T`-shaped) and a real `approveUrl` starting with `https://www.sandbox.paypal.com/checkoutnow?token=...`. This proves the OAuth token exchange and order-creation call both work against the real credentials before anything else in this plan depends on them.

- [ ] **Step 6: Commit**

```bash
git add ClinicNow.Services/Payments/IPayPalClient.cs ClinicNow.Services/Payments/PayPalClient.cs ClinicNow.Services/Payments/PayPalModels.cs
git commit -m "feat(payments): add IPayPalClient/PayPalClient (PayPal REST Orders v2 wrapper)"
```

---

## Task 4: `IPaymentService`/`PaymentService`

**Files:**
- Create: `ClinicNow.Services/Payments/IPaymentService.cs`
- Create: `ClinicNow.Services/Payments/PaymentService.cs`
- Create: `ClinicNow.Services/Mapping/PaymentMappingConfig.cs`

**Interfaces:**
- Consumes: `IPayPalClient` (Task 3), `Payment`/`PaymentItem`/`PaymentRefund` entities + `ClinicNowContext.{Payments,PaymentItems,PaymentRefunds}` (Task 2), `PaymentDto`/`PaymentCreateRequest`/`PaymentRefundRequest`/`CurrencyConverter`/`PaymentStatus` (Task 1), `INotificationService.CreateAsync` (existing, `ClinicNow.Services/Notifications/`).
- Produces: `IPaymentService.{CreateAsync,CaptureAsync,RefundAsync,GetByAppointmentIdAsync,RefundForCancelledAppointmentAsync}` — consumed by Task 5 (`PaymentController`, `AppointmentService`).

- [ ] **Step 1: `IPaymentService.cs`**

```csharp
using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;

namespace ClinicNow.Services.Payments;

/// <summary>
/// Bespoke, like <c>IAppointmentService</c> - ownership resolution (which
/// Patient row belongs to this JWT) needs an async lookup no generic
/// <c>ICRUDService</c> shape fits.
/// </summary>
public interface IPaymentService
{
    /// <summary>Starts a payment: creates the PayPal order and a Pending Payment row. `AppointmentId` is validated to belong to the caller.</summary>
    Task<PaymentDto> CreateAsync(PaymentCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>Captures a previously-created payment. Idempotent - already-Paid just returns the current state.</summary>
    Task<PaymentDto> CaptureAsync(int paymentId, CancellationToken cancellationToken = default);

    /// <summary>Manual staff/admin refund, full or partial.</summary>
    Task<PaymentDto> RefundAsync(int paymentId, PaymentRefundRequest request, CancellationToken cancellationToken = default);

    Task<PaymentDto?> GetByAppointmentIdAsync(int appointmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The automatic-refund half of design doc §4 item 4 - called from
    /// <c>AppointmentService.CancelAsync</c> after a cancellation succeeds.
    /// Never throws: a PayPal failure here is logged, not propagated, since
    /// the appointment cancellation itself must not be rolled back over a
    /// refund that can be retried manually by staff.
    /// </summary>
    Task RefundForCancelledAppointmentAsync(int appointmentId, int actingUserId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: `PaymentMappingConfig.cs`**

```csharp
using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Services.Database.Entities;
using Mapster;

namespace ClinicNow.Services.Mapping;

public class PaymentMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Payment, PaymentDto>()
            .Map(dest => dest.StatusName, src => src.Status.ToDisplayName())
            .Map(dest => dest.RefundedAmountEur, src => src.Refunds.Sum(r => r.AmountEur))
            .Map(dest => dest.RemainingRefundableEur, src => src.AmountEur - src.Refunds.Sum(r => r.AmountEur))
            .Ignore(dest => dest.ApproveUrl);
    }
}
```

- [ ] **Step 3: Build & verify with Steps 1-2 in place**

```bash
cd ClinicNow && dotnet build ClinicNow.Services/ClinicNow.Services.csproj
```
Expected: 0 errors (an "unused" nothing yet, since nothing implements `IPaymentService` — that's fine, an interface with no implementation still compiles).

- [ ] **Step 4: `PaymentService.cs` — skeleton, DI, and `CreateAsync`**

```csharp
using System.Security.Claims;
using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Notifications;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicNow.Services.Payments;

public class PaymentService : IPaymentService
{
    private const string ReturnUrl = "https://clinicnow.local/payment-return";
    private const string CancelUrl = "https://clinicnow.local/payment-cancel";

    private readonly ClinicNowContext _context;
    private readonly IMapper _mapper;
    private readonly IPayPalClient _payPalClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly INotificationService _notificationService;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        ClinicNowContext context,
        IMapper mapper,
        IPayPalClient payPalClient,
        IHttpContextAccessor httpContextAccessor,
        INotificationService notificationService,
        ILogger<PaymentService> logger)
    {
        _context = context;
        _mapper = mapper;
        _payPalClient = payPalClient;
        _httpContextAccessor = httpContextAccessor;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<PaymentDto> CreateAsync(PaymentCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actingUserId = CurrentUserId(CurrentUser());
        var ownPatientId = await GetOwnPatientIdAsync(actingUserId, cancellationToken);

        var appointment = await _context.Appointments
            .Include(a => a.MedicalService)
            .SingleOrDefaultAsync(a => a.Id == request.AppointmentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Database.Entities.Appointment), request.AppointmentId);

        if (appointment.PatientId != ownPatientId)
        {
            throw new ForbiddenException("Ne možete platiti tuđi termin.");
        }

        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            throw new ValidationException("appointmentId", "Otkazan termin se ne može platiti.");
        }

        var alreadyPaid = await _context.Payments
            .AnyAsync(p => p.AppointmentId == appointment.Id && p.Status != PaymentStatus.Pending, cancellationToken);
        if (alreadyPaid)
        {
            throw new BusinessException("Ovaj termin je već plaćen.");
        }

        var amountEur = CurrencyConverter.ConvertKmToEur(appointment.MedicalService.Price);
        var (orderId, approveUrl) = await _payPalClient.CreateOrderAsync(amountEur, ReturnUrl, CancelUrl, cancellationToken);

        var payment = new Payment
        {
            AppointmentId = appointment.Id,
            AmountEur = amountEur,
            Status = PaymentStatus.Pending,
            PayPalOrderId = orderId,
            CreatedAtUtc = DateTime.UtcNow
        };
        payment.Items.Add(new PaymentItem
        {
            MedicalServiceId = appointment.MedicalServiceId,
            Description = appointment.MedicalService.Name,
            AmountEur = amountEur
        });

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = _mapper.Map<PaymentDto>(payment);
        dto.ApproveUrl = approveUrl;
        return dto;
    }

    // --- helpers -----------------------------------------------------------------

    private async Task<Payment> LoadTrackedAsync(int paymentId, CancellationToken cancellationToken) =>
        await _context.Payments.Include(p => p.Refunds).SingleOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Payment), paymentId);

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

- [ ] **Step 5: Verify it builds with Step 4 in place**

```bash
cd ClinicNow && dotnet build ClinicNow.Services/ClinicNow.Services.csproj
```
Expected: errors listing the still-missing interface members (`CaptureAsync`, `RefundAsync`, `GetByAppointmentIdAsync`, `RefundForCancelledAppointmentAsync`) — expected at this checkpoint, resolved by Step 6.

- [ ] **Step 6: Add `CaptureAsync`, `GetByAppointmentIdAsync`, the shared refund core, `RefundAsync`, and `RefundForCancelledAppointmentAsync`**

Append to the class (before the `// --- helpers` region):

```csharp
    public async Task<PaymentDto> CaptureAsync(int paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadTrackedAsync(paymentId, cancellationToken);
        await EnsureOwnershipAsync(payment, cancellationToken);

        if (payment.Status != PaymentStatus.Pending)
        {
            // Idempotent: a repeated capture call on an already-Paid payment
            // is a no-op success, never a second PayPal call or notification.
            return _mapper.Map<PaymentDto>(payment);
        }

        var (captureId, capturedAmountEur, success) = await _payPalClient.CaptureOrderAsync(payment.PayPalOrderId, cancellationToken);

        if (!success || captureId is null)
        {
            // Expected outcome if the buyer never approved - the row simply
            // stays Pending, so the client can offer a retry.
            throw new BusinessException("Plaćanje nije odobreno na PayPal-u. Pokušajte ponovo.");
        }

        if (Math.Abs(capturedAmountEur - payment.AmountEur) > 0.01m)
        {
            // PayPal enforces the exact order amount for a plain CAPTURE
            // intent, so this should never actually differ - but "never
            // trust the client" extends to "never silently trust a third
            // party either": log it loudly rather than ignore a captured
            // variable, since a mismatch here would be exactly the kind of
            // bug that's invisible until it costs real money.
            _logger.LogWarning("PayPal captured {CapturedAmountEur} EUR for payment {PaymentId} but expected {ExpectedAmountEur} EUR.",
                capturedAmountEur, payment.Id, payment.AmountEur);
        }

        payment.PayPalCaptureId = captureId;
        payment.Status = PaymentStatus.Paid;
        payment.PaidAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        var appointment = await _context.Appointments.Include(a => a.Patient).SingleAsync(a => a.Id == payment.AppointmentId, cancellationToken);
        if (appointment.Patient?.UserId is int patientUserId)
        {
            await _notificationService.CreateAsync(patientUserId, "Plaćanje uspješno",
                $"Vaša uplata od {payment.AmountEur:F2} EUR je uspješno evidentirana.", cancellationToken);
        }

        return _mapper.Map<PaymentDto>(payment);
    }

    public async Task<PaymentDto?> GetByAppointmentIdAsync(int appointmentId, CancellationToken cancellationToken = default)
    {
        var payment = await _context.Payments
            .Include(p => p.Refunds)
            .Where(p => p.AppointmentId == appointmentId && p.Status != PaymentStatus.Pending)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return payment is null ? null : _mapper.Map<PaymentDto>(payment);
    }

    public async Task<PaymentDto> RefundAsync(int paymentId, PaymentRefundRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payment = await LoadTrackedAsync(paymentId, cancellationToken);
        await RefundCoreAsync(payment, request.Amount, request.Reason, CurrentUserId(CurrentUser()), cancellationToken);

        return _mapper.Map<PaymentDto>(payment);
    }

    public async Task RefundForCancelledAppointmentAsync(int appointmentId, int actingUserId, CancellationToken cancellationToken = default)
    {
        var payment = await _context.Payments
            .Include(p => p.Refunds)
            .Where(p => p.AppointmentId == appointmentId && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.PartiallyRefunded))
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            return; // nothing paid on this appointment - nothing to refund
        }

        var remaining = payment.AmountEur - payment.Refunds.Sum(r => r.AmountEur);
        if (remaining <= 0)
        {
            return;
        }

        try
        {
            await RefundCoreAsync(payment, remaining, "Termin otkazan.", actingUserId, cancellationToken);
        }
        catch (Exception ex)
        {
            // The cancellation itself must not fail because a refund attempt
            // did - log it clearly so staff can retry the refund manually
            // (design doc §4 item 4).
            _logger.LogWarning(ex, "Automatic refund failed for cancelled appointment {AppointmentId}, payment {PaymentId} - staff must refund manually.", appointmentId, payment.Id);
        }
    }

    /// <summary>The one code path both the manual and automatic refund triggers share (design doc §4 item 4) - validates against the real remaining balance, calls PayPal, records the refund, and recomputes Status.</summary>
    private async Task RefundCoreAsync(Payment payment, decimal amount, string reason, int refundedByUserId, CancellationToken cancellationToken)
    {
        if (payment.Status != PaymentStatus.Paid && payment.Status != PaymentStatus.PartiallyRefunded)
        {
            throw new ValidationException("paymentId", "Ova uplata se ne može vratiti u ovom statusu.");
        }

        var alreadyRefunded = payment.Refunds.Sum(r => r.AmountEur);
        var remaining = payment.AmountEur - alreadyRefunded;

        if (amount <= 0 || amount > remaining)
        {
            throw new ValidationException("amount", $"Iznos povrata mora biti između 0 i {remaining:F2} EUR.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException("reason", "Razlog povrata je obavezan.");
        }

        var payPalRefundId = await _payPalClient.RefundCaptureAsync(payment.PayPalCaptureId!, amount, reason, cancellationToken);

        payment.Refunds.Add(new PaymentRefund
        {
            AmountEur = amount,
            PayPalRefundId = payPalRefundId,
            Reason = reason.Trim(),
            RefundedByUserId = refundedByUserId,
            RefundedAtUtc = DateTime.UtcNow
        });

        var totalRefunded = alreadyRefunded + amount;
        payment.Status = totalRefunded >= payment.AmountEur ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;

        await _context.SaveChangesAsync(cancellationToken);

        var appointment = await _context.Appointments.Include(a => a.Patient).SingleAsync(a => a.Id == payment.AppointmentId, cancellationToken);
        if (appointment.Patient?.UserId is int patientUserId)
        {
            await _notificationService.CreateAsync(patientUserId, "Povrat sredstava",
                $"Izvršen je povrat od {amount:F2} EUR za vaš termin. Razlog: {reason}", cancellationToken);
        }
    }

    private async Task EnsureOwnershipAsync(Payment payment, CancellationToken cancellationToken)
    {
        var principal = CurrentUser();
        if (principal.IsInRole(Roles.Administrator) || principal.IsInRole(Roles.Staff)) return;

        var ownPatientId = await GetOwnPatientIdAsync(CurrentUserId(principal), cancellationToken);
        var appointment = await _context.Appointments.SingleAsync(a => a.Id == payment.AppointmentId, cancellationToken);
        if (appointment.PatientId != ownPatientId)
        {
            throw new ForbiddenException("Nemate pristup ovoj uplati.");
        }
    }
```

- [ ] **Step 7: Full build**

```bash
cd ClinicNow && dotnet build
```
Expected: `Build succeeded` across the whole solution, 0 warnings/errors.

- [ ] **Step 8: Commit**

```bash
git add ClinicNow.Services/Payments/IPaymentService.cs ClinicNow.Services/Payments/PaymentService.cs ClinicNow.Services/Mapping/PaymentMappingConfig.cs
git commit -m "feat(payments): implement PaymentService (create/capture/refund, shared refund core for auto+manual triggers)"
```

---

## Task 5: `PaymentController` + DI wiring + `AppointmentDto`/`AppointmentService` integration

**Files:**
- Create: `ClinicNow.API/Controllers/PaymentController.cs`
- Modify: `ClinicNow.API/Program.cs`
- Modify: `ClinicNow.Model/Dto/AppointmentDto.cs`
- Modify: `ClinicNow.Services/Mapping/AppointmentMappingConfig.cs`
- Modify: `ClinicNow.Services/Appointments/AppointmentService.cs`

**Interfaces:**
- Consumes: `IPaymentService` (Task 4), `PaymentDto`/`PaymentCreateRequest`/`PaymentRefundRequest` (Task 1), `Payment`/`Appointment.Payments` (Task 2).
- Produces: `POST api/Payment`, `POST api/Payment/{id}/capture`, `POST api/Payment/{id}/refund`, `GET api/Payment/by-appointment/{appointmentId}`, `AppointmentDto.{IsPaid,PaymentStatus,PaymentId,CanRefund}` — consumed by mobile (Task 6) and desktop (Task 7).

- [ ] **Step 1: `PaymentController.cs`**

```csharp
using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// Payments (Phase 8, design doc). Create/capture are Patient-only (a
/// patient paying for their own appointment); refund is Administrator/Staff
/// only (the manual half of design doc §2's refund trigger - the automatic
/// half runs inside <c>AppointmentService.CancelAsync</c>, not through this
/// controller at all).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _service;

    public PaymentController(IPaymentService service)
    {
        _service = service;
    }

    [HttpPost]
    [Authorize(Roles = Roles.Patient)]
    public async Task<ActionResult<PaymentDto>> Create(PaymentCreateRequest request, CancellationToken cancellationToken)
    {
        var created = await _service.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPost("{id:int}/capture")]
    [Authorize(Roles = Roles.Patient)]
    public async Task<ActionResult<PaymentDto>> Capture(int id, CancellationToken cancellationToken) =>
        Ok(await _service.CaptureAsync(id, cancellationToken));

    [HttpPost("{id:int}/refund")]
    [Authorize(Roles = $"{Roles.Administrator},{Roles.Staff}")]
    public async Task<ActionResult<PaymentDto>> Refund(int id, PaymentRefundRequest request, CancellationToken cancellationToken) =>
        Ok(await _service.RefundAsync(id, request, cancellationToken));

    [HttpGet("by-appointment/{appointmentId:int}")]
    public async Task<ActionResult<PaymentDto>> GetByAppointment(int appointmentId, CancellationToken cancellationToken)
    {
        var payment = await _service.GetByAppointmentIdAsync(appointmentId, cancellationToken);
        return payment is null ? NotFound() : Ok(payment);
    }
}
```

- [ ] **Step 2: Wire DI in `Program.cs`**

Add the `using`:
```csharp
using ClinicNow.Services.Payments;
```

Add a new options instance next to the other `*Options` (near `recommenderOptions`):
```csharp
var payPalOptions = new PayPalOptions();
```
...and register it (near the other `AddSingleton` calls):
```csharp
builder.Services.AddSingleton(payPalOptions);
```

Add the service registrations in a new labeled section, after the "Recommender (Phase 7)" block:
```csharp
// --- Payments (Phase 8) -----------------------------------------------------------
builder.Services.AddScoped<IPayPalClient, PayPalClient>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
```

- [ ] **Step 3: Extend `AppointmentDto.cs`**

Add after `AllowedActions`:
```csharp
    /// <summary>True once a Payment on this appointment reached Paid or PartiallyRefunded - hides the "Pay" button, shows a "Plaćeno" badge (rulebook Part II §J).</summary>
    public bool IsPaid { get; set; }

    /// <summary>Display name of the current payment's status, or null if nothing beyond a Pending attempt exists yet.</summary>
    public string? PaymentStatus { get; set; }

    /// <summary>The current (non-Pending) payment's id, if any - lets the UI call the refund endpoint directly without a lookup.</summary>
    public int? PaymentId { get; set; }

    /// <summary>True when there is a Paid/PartiallyRefunded payment with a remaining refundable balance &gt; 0.</summary>
    public bool CanRefund { get; set; }
```

- [ ] **Step 4: `Ignore` the 4 new fields in `AppointmentMappingConfig.cs`**

Add to the existing `.Ignore(dest => dest.AllowedActions)` chain:
```csharp
            .Ignore(dest => dest.AllowedActions)
            .Ignore(dest => dest.IsPaid)
            .Ignore(dest => dest.PaymentStatus)
            .Ignore(dest => dest.PaymentId)
            .Ignore(dest => dest.CanRefund);
```

- [ ] **Step 5: Wire `AppointmentService.cs`** — load `Payments`, compute the 4 fields, inject `IPaymentService`, call the auto-refund hook

Add the `using` and constructor dependency:
```csharp
using ClinicNow.Services.Payments;
```
Add a field and constructor parameter alongside the existing ones (`_notificationService`, `_emailPublisher`):
```csharp
    private readonly IPaymentService _paymentService;
```
...assigned in the constructor the same way as the other injected services, with `IPaymentService paymentService` added to the parameter list.

Modify `IncludeAll` to also load payments:
```csharp
    private static IQueryable<Appointment> IncludeAll(IQueryable<Appointment> query) => query
        .Include(a => a.Patient).ThenInclude(p => p!.User)
        .Include(a => a.Doctor).ThenInclude(d => d.User)
        .Include(a => a.MedicalService)
        .Include(a => a.Location)
        .Include(a => a.Payments).ThenInclude(p => p.Refunds);
```

Modify `MapToDto`:
```csharp
    private AppointmentDto MapToDto(Appointment appointment)
    {
        var dto = _mapper.Map<AppointmentDto>(appointment);
        dto.AllowedActions = BaseAppointmentState.CreateState(appointment.Status, _serviceProvider)
            .AllowedActions()
            .Select(a => a.ToString())
            .ToList();

        var currentPayment = appointment.Payments
            .Where(p => p.Status != Model.Common.PaymentStatus.Pending)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefault();

        if (currentPayment is not null)
        {
            dto.PaymentId = currentPayment.Id;
            dto.PaymentStatus = currentPayment.Status.ToDisplayName();
            dto.IsPaid = currentPayment.Status != Model.Common.PaymentStatus.Refunded;
            var remaining = currentPayment.AmountEur - currentPayment.Refunds.Sum(r => r.AmountEur);
            dto.CanRefund = (currentPayment.Status == Model.Common.PaymentStatus.Paid || currentPayment.Status == Model.Common.PaymentStatus.PartiallyRefunded) && remaining > 0;
        }

        return dto;
    }
```

Modify `CancelAsync` — after the state transition succeeds and the reload happens, before or alongside the existing notification calls, add the auto-refund call:
```csharp
        var reloaded = await ReloadAsync(appointment.Id, cancellationToken);

        // Auto-refund the remaining balance on any cancellation of a paid
        // appointment (design doc §2/§4 item 4) - never throws, so a PayPal
        // failure here can't undo the cancellation that already succeeded.
        await _paymentService.RefundForCancelledAppointmentAsync(appointment.Id, actingUserId, cancellationToken);
```
(Place this line right after `var reloaded = await ReloadAsync(appointment.Id, cancellationToken);` inside `CancelAsync`, before the existing patient/doctor notification block — so the refund notification and the cancellation notification both land, in a sensible order.)

- [ ] **Step 6: Register `AppointmentService`'s new dependency where it's constructed**

`AppointmentService` is already registered `AddScoped<IAppointmentService, AppointmentService>()` in `Program.cs` — no change needed there, since ASP.NET Core's DI container resolves the new constructor parameter automatically as long as `IPaymentService` itself is registered (done in Step 2). Just confirm the registration order in `Program.cs` doesn't matter here (it doesn't — DI resolves the whole graph lazily).

- [ ] **Step 7: Full build**

```bash
cd ClinicNow && dotnet build
```
Expected: 0 errors.

- [ ] **Step 8: Live verify against a real running instance**

Try `docker compose up -d --build clinicnow-api` first; if Docker isn't reachable in this environment (every prior phase's plan in this repo hit that same limitation), fall back to `dotnet run --project ClinicNow.API` against the already-configured local DB. Then:
1. Log in as `patient@clinicnow.test`/`test`, `POST api/Payment {"appointmentId": <a Pending/Confirmed appointment id owned by this patient>}` → expect `201` with a real `approveUrl` and `amountEur` matching the appointment's service price converted to EUR.
2. `POST api/Payment/{id}/capture` **without** approving first → expect a clean `400` (`BusinessException` → mapped by `ExceptionFilter`), not a `500` — this proves `CaptureOrderAsync`'s `Success=false` path is wired correctly end-to-end.
3. `GET api/Payment/by-appointment/{appointmentId}` for the seeded `Paid` appointment (Id=1) → expect `isPaid`-equivalent data (`status: 1`, `statusName: "Plaćeno"`).
4. `POST api/Payment/{id}/refund` as `staff@clinicnow.test`/`test` on the seeded partially-refunded payment (Id=3), with an amount exceeding the remaining balance → expect a clean `400` validation error naming the real remaining balance.
5. `POST api/Payment/{id}/refund` as `patient@clinicnow.test` (not staff) → expect `403`.
6. `GET api/Appointment/1` (the seeded Paid/Completed appointment) → confirm the response now includes `isPaid: true`, `paymentStatus: "Plaćeno"`, `paymentId: 1`, `canRefund: true`.

- [ ] **Step 9: Commit**

```bash
git add ClinicNow.API/Controllers/PaymentController.cs ClinicNow.API/Program.cs ClinicNow.Model/Dto/AppointmentDto.cs ClinicNow.Services/Mapping/AppointmentMappingConfig.cs ClinicNow.Services/Appointments/AppointmentService.cs
git commit -m "feat(payments): add PaymentController, DI wiring, and AppointmentDto/AppointmentService payment-state integration"
```

---

## Task 6: Mobile (`clinicnow_mobile`) — payment model/provider, WebView screen, booking-flow + detail-screen integration

**Files:**
- Create: `UI/clinicnow_mobile/lib/models/payment.dart`
- Create: `UI/clinicnow_mobile/lib/providers/payment_provider.dart`
- Create: `UI/clinicnow_mobile/lib/screens/payments/payment_webview_screen.dart`
- Modify: `UI/clinicnow_mobile/lib/models/appointment.dart`
- Modify: `UI/clinicnow_mobile/lib/screens/appointments/book_appointment_screen.dart`
- Modify: `UI/clinicnow_mobile/lib/screens/appointments/appointment_detail_screen.dart`
- Modify: `UI/clinicnow_mobile/pubspec.yaml`

**Interfaces:**
- Consumes: `POST api/Payment`, `POST api/Payment/{id}/capture` (Task 5); `AppointmentDto`'s 4 new fields (Task 5).
- Produces: `Payment.fromJson`, `PaymentProvider.{create,capture}`, `PaymentWebViewScreen(approveUrl)` returning `bool` (approved/cancelled) — consumed within this task's own booking-flow/detail-screen changes.

- [ ] **Step 1: Add `webview_flutter` to `pubspec.yaml`**

```bash
cd ClinicNow/UI/clinicnow_mobile && flutter pub add webview_flutter
```
This resolves and pins the current stable version — don't hand-type one.

- [ ] **Step 2: `lib/models/payment.dart`**

```dart
/// Mirrors the backend's `PaymentDto`.
class Payment {
  final int id;
  final int appointmentId;
  final double amountEur;
  final int status;
  final String statusName;
  final double refundedAmountEur;
  final double remainingRefundableEur;
  final DateTime createdAtUtc;
  final DateTime? paidAtUtc;
  final String? approveUrl;

  Payment({
    required this.id,
    required this.appointmentId,
    required this.amountEur,
    required this.status,
    required this.statusName,
    required this.refundedAmountEur,
    required this.remainingRefundableEur,
    required this.createdAtUtc,
    this.paidAtUtc,
    this.approveUrl,
  });

  factory Payment.fromJson(Map<String, dynamic> json) => Payment(
        id: json['id'] as int,
        appointmentId: json['appointmentId'] as int,
        amountEur: (json['amountEur'] as num).toDouble(),
        status: json['status'] as int,
        statusName: json['statusName'] as String,
        refundedAmountEur: (json['refundedAmountEur'] as num).toDouble(),
        remainingRefundableEur: (json['remainingRefundableEur'] as num).toDouble(),
        createdAtUtc: DateTime.parse(json['createdAtUtc'] as String).toLocal(),
        paidAtUtc: json['paidAtUtc'] == null ? null : DateTime.parse(json['paidAtUtc'] as String).toLocal(),
        approveUrl: json['approveUrl'] as String?,
      );
}
```

- [ ] **Step 3: `lib/providers/payment_provider.dart`**

```dart
import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/payment.dart';

class PaymentProvider extends BaseProvider<Payment> {
  PaymentProvider(AuthSession authSession) : super('Payment', authSession);

  @override
  Payment fromJson(Map<String, dynamic> json) => Payment.fromJson(json);

  /// Starts a payment for the given appointment - `insert` already POSTs to
  /// `api/Payment` and decodes the response via `BaseProvider`, no custom
  /// logic needed beyond the request body shape.
  Future<Payment> create(int appointmentId) => insert({'appointmentId': appointmentId});

  Future<Payment> capture(int paymentId) async {
    final response = await http.post(buildUri('api/Payment/$paymentId/capture'), headers: authHeaders());
    return fromJson(decode(response) as Map<String, dynamic>);
  }
}
```

This needs `import 'package:http/http.dart' as http;` at the top alongside the others (same pattern `AppointmentProvider` uses for its own custom actions).

- [ ] **Step 4: `lib/screens/payments/payment_webview_screen.dart`**

```dart
import 'package:flutter/material.dart';
import 'package:webview_flutter/webview_flutter.dart';

/// In-app PayPal approval (design doc §5) - never hands off to an external
/// browser. PayPal's `return_url`/`cancel_url` are fixed, non-resolving
/// sentinel URLs (`PaymentService.ReturnUrl`/`CancelUrl` on the backend) -
/// this screen's `NavigationDelegate` intercepts the attempt to navigate to
/// either one and closes with a result *before* the WebView ever tries to
/// actually load them.
class PaymentWebViewScreen extends StatefulWidget {
  final String approveUrl;

  const PaymentWebViewScreen({super.key, required this.approveUrl});

  @override
  State<PaymentWebViewScreen> createState() => _PaymentWebViewScreenState();
}

class _PaymentWebViewScreenState extends State<PaymentWebViewScreen> {
  static const _returnUrlPrefix = 'https://clinicnow.local/payment-return';
  static const _cancelUrlPrefix = 'https://clinicnow.local/payment-cancel';

  late final WebViewController _controller;

  @override
  void initState() {
    super.initState();
    _controller = WebViewController()
      ..setJavaScriptMode(JavaScriptMode.unrestricted)
      ..setNavigationDelegate(NavigationDelegate(
        onNavigationRequest: (request) {
          if (request.url.startsWith(_returnUrlPrefix)) {
            Navigator.of(context).pop(true);
            return NavigationDecision.prevent;
          }
          if (request.url.startsWith(_cancelUrlPrefix)) {
            Navigator.of(context).pop(false);
            return NavigationDecision.prevent;
          }
          return NavigationDecision.navigate;
        },
      ))
      ..loadRequest(Uri.parse(widget.approveUrl));
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Plaćanje putem PayPal-a'),
        leading: IconButton(
          icon: const Icon(Icons.close),
          onPressed: () => Navigator.of(context).pop(false),
        ),
      ),
      body: WebViewWidget(controller: _controller),
    );
  }
}
```

- [ ] **Step 5: Mirror the 4 new `AppointmentDto` fields in `lib/models/appointment.dart`**

Add fields:
```dart
  final bool isPaid;
  final String? paymentStatus;
  final int? paymentId;
  final bool canRefund;
```
Add to the constructor (`required this.isPaid`, `this.paymentStatus`, `this.paymentId`, `required this.canRefund`) and to `fromJson`:
```dart
        isPaid: json['isPaid'] as bool,
        paymentStatus: json['paymentStatus'] as String?,
        paymentId: json['paymentId'] as int?,
        canRefund: json['canRefund'] as bool,
```

- [ ] **Step 6: Wire the payment step into `book_appointment_screen.dart`**

Add imports:
```dart
import '../payments/payment_webview_screen.dart';
import '../../providers/payment_provider.dart';
```
Add a `late final PaymentProvider _paymentProvider;` field, constructed in `initState` alongside the other providers.

In `_submit()`, after the appointment is successfully created (the existing `await _appointmentProvider.insert({...})` call) and before the existing `Navigator.of(context).pop(true)`, insert a confirm dialog + payment attempt. Replace the body of `_submit()`'s try block from the `insert` call onward with:

```dart
    try {
      final appointment = await _appointmentProvider.insert({
        'doctorId': _doctor!.id,
        'medicalServiceId': _service!.id,
        'startUtc': _selectedSlot!.toUtc().toIso8601String(),
      });
      if (!mounted) return;

      final wantsToPay = await showDialog<bool>(
        context: context,
        builder: (dialogContext) => AlertDialog(
          title: const Text('Plaćanje'),
          content: Text('Termin je zakazan. Željeli biste li odmah platiti (${_service!.price.toStringAsFixed(2)} KM)?'),
          actions: [
            TextButton(onPressed: () => Navigator.of(dialogContext).pop(false), child: const Text('Kasnije')),
            FilledButton(onPressed: () => Navigator.of(dialogContext).pop(true), child: const Text('Plati sada')),
          ],
        ),
      );

      if (wantsToPay == true) {
        await _attemptPayment(appointment.id);
      }

      if (!mounted) return;
      Navigator.of(context).pop(true);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Termin je uspješno zakazan.')),
      );
    } on ApiException catch (e) {
      setState(() {
        _error = e.message;
        _isSubmitting = false;
      });
    }
```

Add the helper method (anywhere in the state class):
```dart
  Future<void> _attemptPayment(int appointmentId) async {
    try {
      final payment = await _paymentProvider.create(appointmentId);
      if (!mounted || payment.approveUrl == null) return;

      final approved = await Navigator.of(context).push<bool>(
        MaterialPageRoute(builder: (_) => PaymentWebViewScreen(approveUrl: payment.approveUrl!)),
      );

      if (approved == true) {
        await _paymentProvider.capture(payment.id);
      }
      // A `false`/null result (cancelled) or a failed capture is silently
      // fine here - the appointment stays booked and unpaid either way
      // (design doc §2), and a "Plati" button remains available on the
      // appointment detail screen for a retry.
    } on ApiException {
      // Payment failures must never block the booking flow that already
      // succeeded - the appointment exists regardless.
    }
  }
```

- [ ] **Step 7: "Plaćeno" badge / "Plati" button in `appointment_detail_screen.dart`**

Add imports:
```dart
import '../payments/payment_webview_screen.dart';
import '../../core/auth_session.dart';
import '../../providers/payment_provider.dart';
```
Add a `late final PaymentProvider _paymentProvider;` field and `bool _isPaying = false;`, constructed in `initState`:
```dart
    _paymentProvider = PaymentProvider(context.read<AuthSession>());
```
(needs `import 'package:provider/provider.dart';` too, if not already present in this file — check first, it currently is not, since this file has no `Provider` usage yet).

Add a `_pay()` method mirroring `_attemptPayment` from Step 6 but operating on `_appointment` and refreshing local state on success:
```dart
  Future<void> _pay() async {
    setState(() => _isPaying = true);
    try {
      final payment = await _paymentProvider.create(_appointment.id);
      if (!mounted || payment.approveUrl == null) return;

      final approved = await Navigator.of(context).push<bool>(
        MaterialPageRoute(builder: (_) => PaymentWebViewScreen(approveUrl: payment.approveUrl!)),
      );

      if (approved == true) {
        await _paymentProvider.capture(payment.id);
        if (!mounted) return;
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Plaćanje uspješno.')));
        // Re-fetch to pick up the server's fresh isPaid/paymentStatus.
        final refreshed = await widget.provider.getById(_appointment.id);
        if (mounted) setState(() => _appointment = refreshed);
      }
    } on ApiException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    } finally {
      if (mounted) setState(() => _isPaying = false);
    }
  }
```

In `build()`, add a payment row right after the existing `_DetailRow` block (before the Cancel button section):
```dart
            const SizedBox(height: 8),
            if (_appointment.isPaid)
              Chip(
                avatar: const Icon(Icons.check_circle, color: Colors.white, size: 18),
                label: Text(_appointment.paymentStatus ?? 'Plaćeno', style: const TextStyle(color: Colors.white)),
                backgroundColor: Colors.green,
              )
            else if (_appointment.status != 3) // never offer to pay a Cancelled appointment
              FilledButton.icon(
                onPressed: _isPaying ? null : _pay,
                icon: _isPaying
                    ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                    : const Icon(Icons.payment),
                label: const Text('Plati'),
              ),
```

- [ ] **Step 8: `flutter analyze` + `flutter test`**

```bash
cd ClinicNow/UI/clinicnow_mobile && flutter analyze && flutter test
```
Expected: `No issues found!`, all tests passing.

- [ ] **Step 9: Live verify in the browser (dev-only Flutter web target, same pattern as every prior phase)**

```bash
cd ClinicNow/UI/clinicnow_mobile && flutter run -d chrome --web-port=5000 --dart-define=API_BASE_URL=http://localhost:5203/
```
Log in as `patient@clinicnow.test`/`test`. Book a new appointment, confirm the "Plati sada" dialog appears, confirm the WebView opens to a real PayPal sandbox login page (do not need to log in for this check - just confirm it loads a genuine `sandbox.paypal.com` page, proving the whole chain from booking through order creation to the WebView actually works). Close the WebView (X) and confirm the appointment still shows in "Termini" as booked/unpaid with a "Plati" button. Open the seeded already-`Paid` appointment (if visible to this patient) and confirm it shows the green "Plaćeno" chip instead of a pay button.

- [ ] **Step 10: Commit**

```bash
git add UI/clinicnow_mobile/pubspec.yaml UI/clinicnow_mobile/pubspec.lock UI/clinicnow_mobile/lib/models/payment.dart UI/clinicnow_mobile/lib/providers/payment_provider.dart UI/clinicnow_mobile/lib/screens/payments/payment_webview_screen.dart UI/clinicnow_mobile/lib/models/appointment.dart UI/clinicnow_mobile/lib/screens/appointments/book_appointment_screen.dart UI/clinicnow_mobile/lib/screens/appointments/appointment_detail_screen.dart
git commit -m "feat(payments): mobile in-app PayPal payment (booking-flow + detail-screen pay/paid state)"
```

---

## Task 7: Desktop (`clinicnow_desktop`) — Refund row-action

**Files:**
- Create: `UI/clinicnow_desktop/lib/models/payment.dart`
- Create: `UI/clinicnow_desktop/lib/providers/payment_provider.dart`
- Modify: `UI/clinicnow_desktop/lib/models/appointment.dart`
- Modify: `UI/clinicnow_desktop/lib/screens/appointments/appointment_screen.dart`

**Interfaces:**
- Consumes: `POST api/Payment/{id}/refund` (Task 5); `AppointmentDto`'s 4 new fields (Task 5).
- Produces: `PaymentProvider.refund(paymentId, amount, reason) : Future<Payment>` — consumed within this task's own `AppointmentScreen` change.

- [ ] **Step 1: Mirror the 4 new `AppointmentDto` fields in `lib/models/appointment.dart`** (identical change to mobile's Task 6 Step 5 — this file is a byte-for-byte duplicate of the mobile one today)

Same 4 fields, same constructor/`fromJson` additions as mobile Task 6 Step 5.

- [ ] **Step 2: `lib/models/payment.dart`**

Desktop only ever calls the refund endpoint (create/capture is a patient-mobile-only flow, design doc §2), but `POST api/Payment/{id}/refund` still returns a real `PaymentDto` — decoding it properly (rather than discarding the body) is both more correct and lets `PaymentProvider` cleanly extend `BaseProvider<Payment>` like every other provider in this codebase, instead of working around a generic type it doesn't need. Same shape as the mobile model (Task 6 Step 2), minus `approveUrl` (never populated by a refund response, so it's omitted here rather than left permanently unused):

```dart
/// Mirrors the backend's `PaymentDto`. Desktop only ever reads this from a
/// refund response - it never creates or captures a payment.
class Payment {
  final int id;
  final int appointmentId;
  final double amountEur;
  final int status;
  final String statusName;
  final double refundedAmountEur;
  final double remainingRefundableEur;
  final DateTime createdAtUtc;
  final DateTime? paidAtUtc;

  Payment({
    required this.id,
    required this.appointmentId,
    required this.amountEur,
    required this.status,
    required this.statusName,
    required this.refundedAmountEur,
    required this.remainingRefundableEur,
    required this.createdAtUtc,
    this.paidAtUtc,
  });

  factory Payment.fromJson(Map<String, dynamic> json) => Payment(
        id: json['id'] as int,
        appointmentId: json['appointmentId'] as int,
        amountEur: (json['amountEur'] as num).toDouble(),
        status: json['status'] as int,
        statusName: json['statusName'] as String,
        refundedAmountEur: (json['refundedAmountEur'] as num).toDouble(),
        remainingRefundableEur: (json['remainingRefundableEur'] as num).toDouble(),
        createdAtUtc: DateTime.parse(json['createdAtUtc'] as String).toLocal(),
        paidAtUtc: json['paidAtUtc'] == null ? null : DateTime.parse(json['paidAtUtc'] as String).toLocal(),
      );
}
```

- [ ] **Step 3: `lib/providers/payment_provider.dart`**

```dart
import 'dart:convert';

import 'package:http/http.dart' as http;

import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/payment.dart';

class PaymentProvider extends BaseProvider<Payment> {
  PaymentProvider(AuthSession authSession) : super('Payment', authSession);

  @override
  Payment fromJson(Map<String, dynamic> json) => Payment.fromJson(json);

  Future<Payment> refund(int paymentId, double amount, String reason) async {
    final response = await http.post(
      buildUri('api/Payment/$paymentId/refund'),
      headers: authHeaders(),
      body: jsonEncode({'amount': amount, 'reason': reason}),
    );
    return fromJson(decode(response) as Map<String, dynamic>);
  }
}
```

- [ ] **Step 4: Add the Refund action to `appointment_screen.dart`**

Add the import:
```dart
import '../../providers/payment_provider.dart';
```
Add a field, constructed in `initState` alongside the other providers:
```dart
  late final PaymentProvider _paymentProvider;
```
```dart
    _paymentProvider = PaymentProvider(authSession);
```

Add the refund dialog method (alongside `_cancel`):
```dart
  Future<void> _refund(Appointment appointment) async {
    final amountController = TextEditingController(text: appointment.canRefund ? '' : '0');
    final reasonController = TextEditingController();
    String? amountError;
    String? reasonError;

    // The remaining refundable balance isn't on the Appointment model itself
    // (only Payment carries AmountEur/refund totals) - the dialog's amount
    // field is free-entry, validated server-side against the real remaining
    // balance (design doc §7); the max shown here is informational only.
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: const Text('Povrat sredstava'),
          content: SizedBox(
            width: 400,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Termin za ${appointment.patientName} kod ${appointment.doctorName}'),
                const SizedBox(height: 16),
                TextField(
                  controller: amountController,
                  keyboardType: const TextInputType.numberWithOptions(decimal: true),
                  decoration: InputDecoration(labelText: 'Iznos povrata (EUR)', errorText: amountError),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: reasonController,
                  decoration: InputDecoration(labelText: 'Razlog povrata', errorText: reasonError),
                  maxLines: 2,
                ),
              ],
            ),
          ),
          actions: [
            TextButton(onPressed: () => Navigator.of(dialogContext).pop(false), child: const Text('Odustani')),
            FilledButton(
              onPressed: () {
                final amount = double.tryParse(amountController.text.replaceAll(',', '.'));
                setDialogState(() {
                  amountError = (amount == null || amount <= 0) ? 'Unesite ispravan iznos veći od 0.' : null;
                  reasonError = reasonController.text.trim().isEmpty ? 'Razlog povrata je obavezan.' : null;
                });
                if (amountError == null && reasonError == null) {
                  Navigator.of(dialogContext).pop(true);
                }
              },
              child: const Text('Izvrši povrat'),
            ),
          ],
        ),
      ),
    );

    if (confirmed != true || appointment.paymentId == null) return;

    try {
      final amount = double.parse(amountController.text.replaceAll(',', '.'));
      await _paymentProvider.refund(appointment.paymentId!, amount, reasonController.text.trim());
      await _load();
      _showError('Povrat je uspješno izvršen.'); // reused SnackBar helper - message just happens to be a success, not an error
    } on ApiException catch (e) {
      _showError(e.message);
    }
  }
```

Add the row action to the `DataCell(Row(...))` actions list, alongside Confirm/Complete/Cancel:
```dart
                                    if (appointment.canRefund)
                                      IconButton(
                                        tooltip: 'Povrat sredstava',
                                        icon: const Icon(Icons.undo, color: Colors.orange),
                                        onPressed: () => _refund(appointment),
                                      ),
```
(No disabled/grey placeholder variant needed here, unlike Confirm/Complete/Cancel — those three are *always* one of the three legal-or-not states for any status; Refund is only ever relevant for a paid appointment, so simply omitting the icon entirely when `canRefund` is false is the right "disabled-with-reason" equivalent: there's nothing to disable-with-reason for an appointment that was never paid in the first place.)

- [ ] **Step 4: `flutter analyze` + `flutter test`**

```bash
cd ClinicNow/UI/clinicnow_desktop && flutter analyze && flutter test
```
Expected: `No issues found!`, all tests passing.

- [ ] **Step 5: Live verify in the browser**

```bash
cd ClinicNow/UI/clinicnow_desktop && flutter run -d chrome --web-port=5001 --dart-define=API_BASE_URL=http://localhost:5203/
```
Log in as `staff@clinicnow.test`/`test`. Confirm the seeded `PartiallyRefunded` appointment (Id=3) shows a Refund icon; open the dialog, enter an amount larger than the remaining balance, confirm a clean validation error appears (not a crash); enter a valid partial amount with a reason, confirm it succeeds and the table refreshes. Confirm the seeded fully-`Refunded` appointment (Id=2) does NOT show a Refund icon (nothing left to refund).

- [ ] **Step 6: Commit**

```bash
git add UI/clinicnow_desktop/lib/models/payment.dart UI/clinicnow_desktop/lib/providers/payment_provider.dart UI/clinicnow_desktop/lib/models/appointment.dart UI/clinicnow_desktop/lib/screens/appointments/appointment_screen.dart
git commit -m "feat(payments): desktop refund row-action on AppointmentScreen"
```

---

## Task 8: End-to-end verification + `GOALS.md` progress-log entry

**Files:**
- Modify: `GOALS.md` (progress log + matrix row 11 "Payments")

- [ ] **Step 1: Full regression sweep**

```bash
cd ClinicNow && dotnet build
cd UI/clinicnow_mobile && flutter analyze && flutter test
cd ../clinicnow_desktop && flutter analyze && flutter test
```
All clean.

- [ ] **Step 2: What curl/a subagent CAN fully verify**

- Order creation against the real PayPal sandbox (Task 3 Step 5, Task 5 Step 8.1) — already proven live.
- Capture-before-approval correctly fails clean, not with a 500 (Task 5 Step 8.2).
- Idempotent capture: call `POST api/Payment/{id}/capture` twice in a row on the seeded already-`Paid` payment (Id=1) — second call must return `200` with unchanged data, and the API log must show no second PayPal API call attempted (only one "capture" log line, or none, since the idempotent short-circuit returns before ever calling `IPayPalClient`).
- Refund math: attempting to refund more than the remaining balance on the seeded `PartiallyRefunded` payment (Id=3) correctly `400`s naming the real number; a valid partial refund on a *fresh* payment (create one via `POST api/Payment` + manual capture, or reuse a test-created one) correctly moves `Paid → PartiallyRefunded → Refunded` as refunds accumulate to the full amount.
- Role gating: `POST api/Payment` as Staff/Doctor/Administrator → `403` (Patient-only); `POST api/Payment/{id}/refund` as Patient → `403` (Staff/Admin-only); anonymous on any endpoint → `401`.
- Auto-refund on cancellation: cancel a *freshly paid* test appointment (not one of the fixed seed rows, to avoid mutating the demo data) and confirm `GET api/Payment/by-appointment/{id}` afterward shows `status: "Vraćeno"` with no manual refund call made — this is the design's core "automatic refund on cancellation" promise, and it's fully curl-verifiable since it doesn't need a live PayPal *approval*, only a capture that already happened.

- [ ] **Step 3: What genuinely needs a human (or the controller's own browser) — do this one live approval**

A full **create → open the real PayPal sandbox login page → approve → capture succeeds → refund** cycle needs an interactive PayPal sandbox login, which a curl-driven subagent cannot complete on its own. Before calling this phase done:
- Either ask Ibrahim to complete one real payment through the running mobile app (Task 6 Step 9 already gets the WebView to a genuine sandbox login page — one manual login+approve click finishes the loop), **or**
- Have the controller session (not a subagent) drive it using its own browser tooling with a PayPal sandbox buyer login (the PayPal Developer Dashboard's Sandbox → Accounts page has a default personal test account, or Ibrahim can provide one) — open the `approveUrl` a `POST api/Payment` call returns, log in, approve, then call `POST api/Payment/{id}/capture` via curl and confirm `status: "Plaćeno"` with a real `PayPalCaptureId`, then `POST api/Payment/{id}/refund` and confirm a real `PayPalRefundId` comes back.

Record in the `GOALS.md` entry (Step 4) exactly which of these two paths was actually used, and if neither was possible in this session, say so plainly — do not claim a full approve-through-refund cycle was verified unless it genuinely was.

- [ ] **Step 4: `GOALS.md` progress-log entry**

Write a real, dated entry in the same voice as the existing ones: what was built, what Step 2's curl-verifiable checks actually showed (with real evidence — status codes, real response bodies, real remaining-balance numbers), and an honest account of Step 3's outcome. `GOALS.md`'s requirement matrix already has a row 11 for this ("Payments: real PayPal sandbox + refund + server-side finalize + idempotent") — flip it to ☑ only if Step 3 was genuinely completed; otherwise leave it ☐ with a note on what's still needed.

- [ ] **Step 5: Commit**

```bash
git add GOALS.md
git commit -m "docs: record Phase 8 (payments) live verification in GOALS.md progress log"
```

---

## Self-Review Notes (for whoever executes this plan)

- **Spec coverage:** design doc §2 (all 6 decisions) → Tasks 1-7 throughout, each decision traceable to a specific implementation choice cited above. §3 (data model) → Task 2. §4 (backend architecture) → Tasks 3-5. §5 (mobile flow) → Task 6. §6 (desktop flow) → Task 7. §7 (error handling/idempotency) → woven through `PaymentService` in Task 4 and the negative-path checks in Task 5 Step 8 / Task 8 Step 2. §8 (seed data) → Task 2 Steps 5-7. §9 (out of scope) → respected: no reports screen built, no multi-item payment UI, no appointment-state-machine change anywhere in this plan.
- **Known limitation to flag to Ibrahim, not silently ship:** Task 8's Step 3 is the one piece of this phase that cannot be fully automated end-to-end by a subagent — flagged explicitly rather than glossed over, with two concrete paths to close it.
- **Type/name consistency check before starting Task 5:** `IPaymentService`'s five method names/signatures (Task 4) must match exactly what `PaymentController` (Task 5) and `AppointmentService.CancelAsync` (Task 5) call — re-verify against the actual `PaymentService.cs` file once Task 4 is done, since a manual edit mid-implementation is where these drift.
