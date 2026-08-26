# Phase 8 — Payments (PayPal Sandbox + Refund) Design

> Companion to `PLAN.md` Phase 8 and `CLAUDE.md` Part II §J. This document is the validated design from brainstorming with Ibrahim on 2026-08-26; the implementation plan (written next, via `writing-plans`) argues from this doc.

## 1. Purpose

A patient pays for a service **at booking time**, via a real PayPal sandbox integration, finalized server-side. Staff/Administrator can refund (full or partial), and cancelling a paid appointment auto-refunds the remaining balance.

## 2. Key decisions (from brainstorming)

| Decision | Choice | Why |
|---|---|---|
| When does payment happen? | At booking time — immediately after the appointment is created, same flow | Clearest UX; matches most clinic booking apps |
| If the patient backs out of paying? | Appointment stays booked, unpaid | Booking and payment are decoupled entities; matches how staff-created (walk-in/cash) appointments already work with zero payment |
| Refund trigger | **Both**: automatic on any cancellation of a paid appointment, **and** a manual staff/admin action | One shared `RefundAsync` code path serves both, so they can never drift or double-refund |
| Manual refund scope | **Partial refunds supported** (editable amount, validated against remaining balance) | Real clinic scenarios (partially-attended service, goodwill refund) |
| Currency | **EUR**, converted server-side from `MedicalService.Price` (KM) via the fixed peg `1 EUR = 1.95583 KM` | PayPal doesn't support BAM; the fixed peg makes conversion exact, not a fluctuating market rate |
| Server-side finalization | **Direct server verification** (backend calls PayPal's Capture API itself and trusts only that response), not a webhook | A webhook needs a public HTTPS endpoint PayPal can call back to — real infra burden for local/sandbox dev and a grader's machine; direct verification gives the same "client never records success" guarantee with zero extra infrastructure |
| Mobile in-app payment UX | **In-app WebView** (`webview_flutter`) showing PayPal's hosted approval page, watching for a fixed sentinel return/cancel URL | PayPal's native mobile SDKs are effectively deprecated in favor of Braintree, which needs a different account type — not what the provided credentials are for. WebView is the standard approach for a plain PayPal REST integration and satisfies "in-app, not an external browser with no return" |

## 3. Data model

- **`Payment`** — one row per payment *attempt* on one `Appointment`. `AppointmentId`, `AmountEur` (server-computed, never trusted from the client), `Status` (`Pending → Paid`, then `→ PartiallyRefunded/Refunded`; a capture that never succeeds just leaves it `Pending` — no separate `Failed` state, since PayPal's own error is surfaced to the client per-request and the patient can simply retry, which just creates a fresh `Payment`), `PayPalOrderId`, `PayPalCaptureId`, `CreatedAtUtc`, `PaidAtUtc`. Multiple `Pending` rows can accumulate per appointment from abandoned attempts — harmless; the only hard invariant is **at most one `Paid` `Payment` per `Appointment`**.
- **`PaymentItem`** — the priced line item(s) under a `Payment` (`MedicalServiceId`, description, unit amount). Mirrors the reference repo's `Uplata`/`StavkaUplate` split; today always exactly one item per payment (the appointment's service), but keeps the shape the plan calls for.
- **`PaymentRefund`** — one row per refund (auto or manual): `PaymentId`, `Amount`, `PayPalRefundId`, `Reason`, `RefundedByUserId`, `RefundedAtUtc`. `Payment.Status` is *derived* from `sum(PaymentRefund.Amount)` vs `Payment.AmountEur` — this is what makes partial refunds safe (can never refund more than remains) and keeps a single source of truth instead of a separately-maintained status flag that could drift.

## 4. Backend architecture

**`IPayPalClient`** (new, `ClinicNow.Services/Payments/`) — thin wrapper over PayPal's REST API via `IHttpClientFactory`:
- `GetAccessTokenAsync()` — OAuth2 client-credentials grant against the sandbox token endpoint, cached in memory until near-expiry.
- `CreateOrderAsync(amountEur, returnUrl, cancelUrl)` → `(orderId, approveUrl)`.
- `CaptureOrderAsync(orderId)` → `(captureId, capturedAmountEur, status)` — this is the value the backend trusts, never the client's claim.
- `RefundCaptureAsync(captureId, amountEur, reason)` → `refundId`.

**`PaymentService`** (bespoke, like `AppointmentService` — not `BaseCRUDService`) orchestrates:
1. `POST api/Payment {appointmentId}` — ownership check (caller's own appointment), reject if a `Paid` payment already exists for it, compute `AmountEur` server-side from `MedicalService.Price`, create the PayPal order, persist `Payment`(`Pending`)+`PaymentItem`, return `{paymentId, approveUrl, amountEur}`.
2. `POST api/Payment/{id}/capture` — **idempotent**: if already `Paid`, returns current state without a second PayPal call. Otherwise captures via PayPal; only a genuinely successful capture flips `Status → Paid`, sets `PaidAtUtc`, fires a notification.
3. `POST api/Payment/{id}/refund {amount, reason}` — `Administrator`/`Staff` only. Validates `0 < amount <= remaining balance`. Calls PayPal's refund API, writes `PaymentRefund`, recomputes `Payment.Status`, notifies the patient.
4. **Automatic refund**: `AppointmentService.CancelAsync`, after a successful state transition, checks for a `Paid`/`PartiallyRefunded` payment on that appointment and — if found — calls the same refund path internally for the full remaining balance, reason `"Appointment cancelled"`. One code path, two triggers.

`AppointmentDto` gains `IsPaid`/`PaymentStatus` (computed from the linked `Payment`, if any) so both clients can show a "Paid" badge and gate their respective UI (hide "Pay" once paid; show "Refund" only when refundable) without an extra round-trip per row.

## 5. Mobile flow (patient)

Booking: pick slot → appointment created (unchanged) → confirm dialog ("Proceed to payment?") → `POST api/Payment` → open `PaymentWebViewScreen(approveUrl)`. Its `NavigationDelegate` watches for two fixed, non-resolving sentinel URLs passed as PayPal's `return_url`/`cancel_url` (e.g. `https://clinicnow.local/payment-return`, `.../payment-cancel`) — the WebView intercepts the navigation attempt itself and closes with a result *before* it ever tries to load them. On return: `POST api/Payment/{id}/capture`, show success, `IsPaid` badge appears. On cancel: just close; the appointment is already saved, unpaid; a "Pay" button (same flow, new `Payment`) stays available on the appointment detail screen for a retry.

## 6. Desktop flow (staff/admin)

No new screen. `AppointmentScreen`'s existing row-action pattern (Confirm/Complete/Cancel icons, each rendered only when legal) gains a **Refund** icon, shown only when `IsPaid`/refundable, opening a dialog with an amount field (pre-filled with the full remaining balance, editable — partial refund) and a reason field, following the same `AlertDialog`+validation pattern the existing Cancel dialog already uses. The table also shows a payment-status indicator so front-desk staff can see paid state at a glance.

## 7. Error handling & idempotency

- PayPal API failures (order create/capture/refund) → `BusinessException`, logged via `ILogger<T>` with the raw PayPal error, never leaked to the client.
- Capture is idempotent by checking `Payment.Status` first — no double PayPal call, no double notification on a client retry.
- Refund validates against the *actual* remaining balance (`AmountEur - sum(refunds)`), never a client-supplied "how much is left" claim.
- `userId`/`patientId` for ownership checks always resolved from the JWT, same pattern as every existing bespoke service (`AppointmentService`, `RecommenderService`).

## 8. Seed data

A few `Payment` rows across states on existing seeded appointments — one `Paid`, one `PartiallyRefunded`, one fully `Refunded` — so paid/refund states are demoable without a live PayPal call.

## 9. Explicitly out of scope for this phase

- A dedicated payments/reports screen (Phase 9 builds the revenue report; this phase only needs the inline refund action).
- Multi-item payments in practice (the `PaymentItem` shape supports it, but nothing in this phase creates more than one item per payment).
- Any change to the appointment state machine — payment is fully decoupled from `AppointmentStatus`.
