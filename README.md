# ClinicNow

ClinicNow is a SaaS system that digitalizes appointment and patient management for small private
clinics. It has three parts sharing one backend and one database:

- **`ClinicNow.API`** — REST API (ASP.NET Core / .NET 10) for both clients.
- **`ClinicNow.Worker`** — a separate background service that consumes RabbitMQ messages (e.g.
  email notifications) so slow side-effects never block API requests.
- **`UI/clinicnow_desktop`** — Flutter Windows desktop app for clinic staff (Administrator,
  Staff, Doctor).
- **`UI/clinicnow_mobile`** — Flutter Android app for patients (Patient).

Seminar work for **Razvoj softvera II**, Fakultet informacijskih tehnologija (Ibrahim Hodžić,
IB210082). See `recommender-dokumentacija.md` for the recommender system design.

## Quick start (read this first)

Everything needed to run and review this project starts **locally** with the commands below — no
external/cloud service has to be created, opened, or configured. `docker-compose up --build`
brings up SQL Server, RabbitMQ, the API, and the Worker on one Docker network; the API applies EF
Core migrations and seeds demo data automatically on startup. The only two outbound network calls
the app can make (SMTP email, PayPal sandbox checkout) are optional demo paths — the app boots and
every core CRUD/booking flow works without them.

### Step 1 — start the backend

```bash
git clone <this-repo-url>
cd ClinicNow
cp .env.example .env
docker-compose up --build
```

Leave this terminal running. Before starting either client, confirm the API answers:

- API: `http://localhost:5203` — Scalar interactive docs at `/scalar/v1`, health check at `/health`.
- RabbitMQ management UI: `http://localhost:15672` (guest/guest).

**Neither client works until the API responds at `http://localhost:5203/health`.** A client
started against a dead API shows a login screen that rejects every attempt — that is a missing
backend, not a broken client.

### Step 2 — start a client

Each client runs in **its own terminal, opened at the repository root**. Do not chain the two
blocks below in one terminal; each begins with a `cd` relative to the repo root.

**Windows desktop app (staff/administrator)** — requires Visual Studio with the "Desktop
development with C++" workload and Windows Developer Mode enabled (see
[Prerequisites](#prerequisites)):

```bash
cd UI/clinicnow_desktop
flutter pub get
flutter run -d windows --dart-define=API_BASE_URL=http://localhost:5203/
```

**Android app (patient)** — requires a running emulator. List the available emulators, launch one
by id, and wait until it reaches its home screen:

```bash
flutter emulators
flutter emulators --launch <emulator_id>
flutter devices                      # confirm an Android device is listed
```

Then, in the same terminal:

```bash
cd UI/clinicnow_mobile
flutter pub get
flutter run -d android --dart-define=API_BASE_URL=http://10.0.2.2:5203/
```

`-d android` targets whichever Android device is connected, so no hardcoded emulator id is needed.
`10.0.2.2` is the Android emulator's alias for the host machine's `localhost`; on a **physical
device**, use the host's LAN address instead (`ipconfig` → IPv4 Address, e.g.
`http://192.168.1.20:5203/`) and make sure the API listens on `0.0.0.0` rather than `localhost`.

If neither native toolchain is available, both clients also run in a browser — see
[Running in a browser](#running-in-a-browser-no-native-toolchain-required).

### Test accounts (for review)

Two accounts cover the two clients end to end — the full list (Staff, Doctor, second Doctor) is
under [Test accounts](#test-accounts) further down.

| App               | Role          | Email                          | Password |
| ----------------- | ------------- | ------------------------------ | -------- |
| Desktop (Windows) | Administrator | `administrator@clinicnow.test` | `test`   |
| Mobile (Android)  | Patient       | `patient@clinicnow.test`       | `test`   |

## Project status

This repository is at **Phase 6 (Medical Documentation)** of the plan. JWT auth (Phase 1), the
four codebooks (Phase 2), `Patient`/`Doctor`/`WorkingHours`/`ScheduleBlock` (Phase 3), the full
appointment state machine (Phase 4), and notifications/news/async email (Phase 5) are all in
place. Phase 6 adds:

- **`MedicalDocument`** — a file (PDF/PNG/JPEG) plus an optional finding note attached to a
  patient's record. Uploads are validated server-side against **both** the declared MIME type
  **and** the file's actual magic bytes (a renamed file can't fake its way past the check), and
  legally-retained health data is soft-delete only, never hard-deleted.
- Ownership enforced server-side: Administrator/Staff/Doctor can view/upload for any patient; a
  Patient can only ever see (and download) their **own** documents, regardless of what filter the
  client sends.
- Desktop: attach/view/download/delete on a patient's record (a new "Dokumenti" row action on the
  Patients screen), using `file_picker` for a real pick-a-file/save-a-file flow. Mobile: a
  read-only "Dokumenti" tab where a patient views and downloads only their own files.

Since Phase 6, a full patient **medical file ("medicinski karton")** was added — distinct from
`MedicalDocument`'s file attachments:

- **`MedicalRecord`** — exactly one per patient (DB-level unique index), with a header (name, age,
  gender, address, email, phone — all denormalized onto one response), an allergies/notes section,
  and a **`MedicalRecordEntry`** treatment-history table (Date/Treatment/Description, all required).
- A **Doctor can only ever add content** — append to allergies/notes, add a history row — never
  edit or delete anything already applied to the file; this is enforced structurally (there is no
  Doctor-reachable code path that overwrites/removes prior content), not just a role check.
- **Administrator has full CRUD** over the same content (replace notes, edit/delete any entry).
- Desktop: a new "medicinski karton" row action on the Patients screen opens the full
  view/editor. No mobile UI for this yet (out of scope for now).

The recommender and payments start at Phase 7+.

## Architecture

```
ClinicNow/
  ClinicNow.sln / ClinicNow.slnx
  docker-compose.yml          # SQL Server + RabbitMQ + API + Worker
  Dockerfile.api
  Dockerfile.worker
  .env.example                 # copy to .env and fill in real values
  ClinicNow.Model/              # DTOs, requests, search objects, exceptions, shared config
  ClinicNow.Services/           # EF Core entities + DbContext, business services
  ClinicNow.API/                # Controllers, Program.cs, OpenAPI + Scalar UI
  ClinicNow.Worker/             # RabbitMQ consumer (background service)
  UI/
    clinicnow_desktop/          # Flutter, staff, Windows
    clinicnow_mobile/           # Flutter, patients, Android
  recommender-dokumentacija.md
```

Layering is strict: **Controller → Service → DbContext**. Controllers never contain business
logic or touch the database directly. See the repository's `CLAUDE.md`/`PLAN.md` (kept alongside
this project, not committed here) for the full engineering rules this codebase follows.

## Prerequisites

| Tool | Version | Needed for |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0+ | building/running the API and Worker |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | with Compose v2 | running the full stack (SQL Server, RabbitMQ, API, Worker) |
| [Flutter SDK](https://docs.flutter.dev/get-started/install) | latest stable (3.44+) | building/running either client |
| Android Studio + Android SDK + at least one AVD | latest | building/running `clinicnow_mobile` |
| Visual Studio 2022/2026 with the **"Desktop development with C++"** workload | latest | building `clinicnow_desktop` for Windows |
| Windows **Developer Mode** enabled | — | building `clinicnow_desktop` (plugins need symlink support) |

Run `flutter doctor` before starting. Every row must be `[√]` for the platform you intend to
launch — a `[!]` on *Visual Studio* blocks the Windows build, and a `[!]` on *Android toolchain*
blocks the APK. Neither blocks the browser fallback below.

## 1. Configuration

All configuration lives in a single `.env` file at the repository root — never in
`appsettings.json`, never hardcoded (see `CLAUDE.md` Part II §C).

```bash
cp .env.example .env
```

Then fill in real values (JWT key, SMTP, PayPal sandbox keys, etc. — see the comments in
`.env.example`). `DB_SA_PASSWORD` and `DB_NAME` are the single source of truth for the database:
docker-compose uses them both to start the SQL Server container and to build the API/Worker's
connection string.

## 2. Running the backend

### Option A — Docker Compose (primary, required path)

```bash
docker-compose up --build
```

This starts, on one bridge network: SQL Server (`clinicnow-sql`), RabbitMQ (`clinicnow-rabbitmq`,
management UI at `http://localhost:15672`), the API (`clinicnow-api`, `http://localhost:5203`),
and the Worker (`clinicnow-worker`). The API applies pending EF Core migrations automatically on
startup — no manual DB setup needed.

- **API testing UI (Scalar)**: `http://localhost:5203/scalar/v1` — interactive request builder, reads the
  OpenAPI document generated by Swashbuckle at `http://localhost:5203/swagger/v1/swagger.json`.
- Health check: `http://localhost:5203/health`

### Option B — running the API/Worker directly on your machine (local dev)

Useful when Docker isn't available. Point `DB_CONNECTION_STRING` in `.env` at a SQL Server /
SQL Server LocalDB instance you have locally, then:

```bash
dotnet run --project ClinicNow.API
dotnet run --project ClinicNow.Worker   # separate terminal - needs a reachable RabbitMQ
```

### Migrations

```bash
dotnet tool install -g dotnet-ef   # first time only
dotnet ef migrations add <Name> --project ClinicNow.Services --startup-project ClinicNow.API --output-dir Database/Migrations
dotnet ef database update --project ClinicNow.Services --startup-project ClinicNow.API
```

Migrations apply automatically on API startup either way (`Database.Migrate()` in `Program.cs`),
so the second command above is only needed if you want to apply a migration without starting the
API.

## 3. Running the Flutter clients (development)

The API base URL is never hardcoded — it's passed at build/run time via `--dart-define`, per
rulebook Part II §C, and read with `String.fromEnvironment('API_BASE_URL')`. **Always pass the
flag**; omitting it leaves the client with no API address.

Each block below assumes a **fresh terminal opened at the repository root**. Both clients can run
at the same time, but each needs its own terminal.

### Windows desktop (staff)

```bash
cd UI/clinicnow_desktop
flutter pub get
flutter run -d windows --dart-define=API_BASE_URL=http://localhost:5203/
```

### Android emulator (patient)

From the repository root, start an emulator first — `flutter run` cannot start one for you:

```bash
flutter emulators                       # lists available AVDs by id
flutter emulators --launch <emulator_id>
```

Wait for the emulator to finish booting to its home screen, then verify Flutter can see it:

```bash
flutter devices                         # an Android device must appear in this list
```

Then run the app:

```bash
cd UI/clinicnow_mobile
flutter pub get
flutter run -d android --dart-define=API_BASE_URL=http://10.0.2.2:5203/
```

If `flutter emulators` prints nothing, no AVD exists yet: Android Studio → **More Actions** →
**Virtual Device Manager** → **Create Device**.

## Running in a browser (no native toolchain required)

Both clients also have the Flutter web platform enabled, purely as a fallback for reviewing the
app without Visual Studio's C++ workload or the Android SDK. The graded deliverables are still the
native Windows build and the Android APK (rulebook §9.2.1); this is a faster inner dev loop, not a
replacement for testing the real artifacts (anything touching native APIs — file pickers, printing,
push notifications, the PayPal SDK — does not behave identically in a browser).

Each of the two commands below needs its own terminal, opened at the repository root:

```bash
cd UI/clinicnow_mobile
flutter run -d chrome --web-port=5000 --dart-define=API_BASE_URL=http://localhost:5203/
```

```bash
cd UI/clinicnow_desktop
flutter run -d chrome --web-port=5001 --dart-define=API_BASE_URL=http://localhost:5203/
```

Two things differ from the native builds, both already wired up:

- Use `http://localhost:5203/` for `API_BASE_URL` in **both** cases, **not** `10.0.2.2` — that
  alias is Android-emulator-specific and does not resolve in a browser.
- Browsers enforce CORS (native Android/Windows apps don't). Each dev server's origin
  (`http://localhost:5000` and `http://localhost:5001`) must be in `.env`'s
  `CORS_ALLOWED_ORIGINS` or the API rejects the browser's requests — both are already set in
  `.env.example`. **If you change `--web-port`, add the new origin to `CORS_ALLOWED_ORIGINS` and
  restart the API**, or every request fails with a CORS error.

The two ports differ on purpose so both clients can run side by side without a clash.

## 4. Building release artifacts

Per the rulebook's submission requirements (§9.2). Run from the repository root; each block
returns to the root with `cd ../..` so the two can be run back to back in one terminal:

```bash
# Android APK - targets 10.0.2.2 (standard Android emulator host alias)
cd UI/clinicnow_mobile
flutter clean
flutter pub get
flutter build apk --release --dart-define=API_BASE_URL=http://10.0.2.2:5203/
cd ../..
# -> UI/clinicnow_mobile/build/app/outputs/flutter-apk/app-release.apk
```

```bash
# Windows desktop - targets localhost
cd UI/clinicnow_desktop
flutter clean
flutter pub get
flutter build windows --release --dart-define=API_BASE_URL=http://localhost:5203/
cd ../..
# -> UI/clinicnow_desktop/build/windows/x64/runner/Release/
```

The Windows artifact is the **entire `Release` folder**, not just the `.exe`. The executable needs
`flutter_windows.dll`, the other DLLs, and the `data/` folder beside it; the `.exe` alone does not
start on another machine.

For submission, both artifacts are zipped together as `fit-build-YYYY-MM-DD.zip` and attached to
a GitHub **Immutable Release** (never committed to git history) — see `.env.example` and the
rulebook for the full delivery procedure.

## Troubleshooting

**`Unable to find suitable Visual Studio toolchain`**
Visual Studio is installed without the C++ workload. Open Visual Studio Installer → **Modify** →
check **Desktop development with C++** (MSVC build tools, C++ CMake tools for Windows, Windows
SDK). Verify with:

```bash
flutter doctor -v
```

The *Visual Studio* section must point at a Visual Studio install path, not at SQL Server
Management Studio.

**`Building with plugins requires symlink support`**
Enable Developer Mode (`start ms-settings:developers`), then restart the terminal.

**Edited `.env`, but the API still behaves as if nothing changed**
Compose passes `.env` values into the container when the container is **created**, so editing the
file has no effect on an already-running one — restarting it isn't enough either. Recreate it:

```bash
docker-compose up -d clinicnow-api
```

Confirm the new value actually landed before debugging any further:

```bash
docker exec clinicnow-api printenv PAYPAL_CLIENT_ID
```

**`No supported devices connected` when running the mobile app**
No emulator is running. Launch one with `flutter emulators --launch <emulator_id>` and confirm
with `flutter devices` before running `flutter run`.

**Client starts, but login fails or every screen is empty**
The API is not running or is on a different port. Confirm `http://localhost:5203/health` responds
in a browser, and that `--dart-define=API_BASE_URL=...` was passed.

**Browser client fails every request with a CORS error**
The dev server's origin is missing from `CORS_ALLOWED_ORIGINS` in `.env`. Add it and restart the
API.

**Stale build after pulling changes**

```bash
flutter clean
flutter pub get
```

## Test accounts

Seeded via the `AddIdentity` migration (`ClinicNow.Services/Database/Configurations/UserConfiguration.cs`).
All four demo accounts use the password `test`; the desktop app accepts Administrator/Staff/Doctor
accounts only, the mobile app accepts Patient accounts only (checked client-side after login, and
independently enforced per-endpoint on the backend).

| Context | Email | Password |
|---|---|---|
| Desktop — Administrator | `administrator@clinicnow.test` | `test` |
| Desktop — Staff | `staff@clinicnow.test` | `test` |
| Desktop — Doctor | `doctor@clinicnow.test` | `test` |
| Desktop — Doctor (2nd, Kardiologija) | `doctor2@clinicnow.test` | `test` |
| Mobile — Patient | `patient@clinicnow.test` | `test` |

New patient accounts can also self-register from the mobile app's "Registruj se" link
(`POST api/auth/register`) - registration always creates a Patient-role account; the server never
accepts a client-supplied role (rulebook §5).

## Demo payment data (read before trying a refund)

The seeded payments exist to demonstrate the **UI states** - the "Plaćeno" / "Djelomično vraćeno" /
"Vraćeno" badges in the desktop appointment list, and the refund dialog opening with a real
remaining balance. They are **not** backed by real PayPal transactions: their capture ids are
synthetic placeholders (`SEED-CAPTURE-000x`).

That means the seeded refundable rows - the partially-refunded **Appointment Id = 3** and the fully
paid **Appointment Id = 1** - show an enabled orange Refund action, and the dialog opens and
prefills its remaining balance correctly, but actually submitting the refund fails with a PayPal
error: PayPal has no record of a capture by those ids. This is expected on a clean database, not a
bug in the refund flow.

To demo a **real** refund end to end:

1. Log into the mobile app as `patient@clinicnow.test`, book an appointment, and choose
   "Plati sada".
2. Complete the checkout in the in-app PayPal **sandbox** window with a sandbox buyer account.
3. Log into the desktop app as `staff@clinicnow.test` (or `administrator@clinicnow.test`), find that
   appointment in "Termini", and use the orange Refund action on it. That payment has a real PayPal
   capture id behind it, so a full or partial refund goes through against the sandbox for real.

> **The sandbox business account must be in a country that can *receive* payments.** PayPal treats
> some countries as send-only — Bosnia and Herzegovina among them — and a sandbox account inherits
> that restriction. With a send-only business account the order is created and the buyer can approve
> it, but the **capture** fails with `422 UNPROCESSABLE_ENTITY / COMPLIANCE_VIOLATION`, which reads in
> the app as "Plaćanje nije odobreno na PayPal-u". Nothing is wrong with the code — create the
> sandbox business (and buyer) accounts with country **United States** and use that business
> account's REST app credentials in `.env`.

Starting a second payment for the same appointment while one is still in flight is refused on
purpose ("Plaćanje za ovaj termin je već u toku"). Backing out of the PayPal screen retires that
attempt immediately, so paying again works right away; if the app is killed mid-payment instead, the
attempt expires on its own after five minutes.

## Testing

```bash
dotnet test ClinicNow.Tests/ClinicNow.Tests.csproj     # backend
```

Note the solution file is `ClinicNow.slnx`, not `.sln`. To run a single test class:

```bash
dotnet test ClinicNow.Tests/ClinicNow.Tests.csproj --filter FullyQualifiedName~PaymentServiceTests
```

```bash
cd UI/clinicnow_desktop
flutter test
cd ../..
```

```bash
cd UI/clinicnow_mobile
flutter test
cd ../..
```

## Tech stack

.NET 10 · ASP.NET Core Web API · Entity Framework Core + SQL Server · Mapster · JWT auth ·
RabbitMQ · ML.NET (recommender) · PayPal sandbox · Flutter (desktop + mobile) · Docker Compose.
