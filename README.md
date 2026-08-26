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
| Android Studio + Android SDK | latest | building/running `clinicnow_mobile` |
| Visual Studio 2022/2026 with the **"Desktop development with C++"** workload | latest | building `clinicnow_desktop` for Windows |

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
rulebook Part II §C, and read with `String.fromEnvironment('API_BASE_URL')`.

```bash
# Windows desktop (staff) - API assumed to be on the same machine
cd UI/clinicnow_desktop
flutter pub get
flutter run -d windows --dart-define=API_BASE_URL=http://localhost:5203/

# Android emulator (patient) - 10.0.2.2 is the emulator's alias for the host machine
cd UI/clinicnow_mobile
flutter pub get
flutter run -d emulator-5554 --dart-define=API_BASE_URL=http://10.0.2.2:5203/
```

### Debugging `clinicnow_mobile` in a browser (no Android SDK required)

Useful while Android Studio/SDK isn't installed yet. `clinicnow_mobile` also has the Flutter web
platform enabled purely for this - the graded deliverable is still the Android APK (rulebook
§9.2.1); this is a faster inner dev loop, not a replacement for real Android testing before
submission (anything touching native APIs - camera, push notifications, the PayPal SDK - won't
behave identically in a browser).

```bash
cd UI/clinicnow_mobile
# -d chrome / -d edge opens a real browser window with Flutter DevTools wired in.
# -d web-server just serves over HTTP without launching a browser itself (handy
# for CI or a headless shell) - open the printed http://localhost:5000 manually.
flutter run -d chrome --web-port=5000 --dart-define=API_BASE_URL=http://localhost:5203/
```

Two things differ from the emulator, both already wired up:

- Use `http://localhost:5203/` for `API_BASE_URL`, **not** `10.0.2.2` - that alias is
  Android-emulator-specific and doesn't resolve in a browser.
- Browsers enforce CORS (native Android/Windows apps don't). The dev server's origin
  (`http://localhost:5000` by default) must be in `.env`'s `CORS_ALLOWED_ORIGINS` or the API will
  reject the browser's requests - already set in `.env.example`. If you pick a different
  `--web-port`, update `CORS_ALLOWED_ORIGINS` to match.

### Debugging `clinicnow_desktop` in a browser (no Visual Studio C++ workload required)

Same idea as above, for staff-side screens, useful while the "Desktop development with C++"
Visual Studio workload isn't installed yet. `clinicnow_desktop` also has the Flutter web platform
enabled purely for this - the graded deliverable is still the native Windows build (rulebook
§9.2.1).

```bash
cd UI/clinicnow_desktop
flutter run -d chrome --web-port=5001 --dart-define=API_BASE_URL=http://localhost:5203/
```

Uses port `5001` (not `5000`) so both apps' web dev servers can run side by side without a port
clash - both origins are already present in `.env.example`'s `CORS_ALLOWED_ORIGINS`.

## 4. Building release artifacts

Per the rulebook's submission requirements (§9.2):

```bash
# Android APK - targets 10.0.2.2 (standard Android emulator host alias)
cd UI/clinicnow_mobile
flutter clean
flutter build apk --release --dart-define=API_BASE_URL=http://10.0.2.2:5203/
# -> UI/clinicnow_mobile/build/app/outputs/flutter-apk/app-release.apk

# Windows desktop - targets localhost
cd UI/clinicnow_desktop
flutter clean
flutter build windows --release --dart-define=API_BASE_URL=http://localhost:5203/
# -> UI/clinicnow_desktop/build/windows/x64/runner/Release/
```

For submission, both artifacts are zipped together as `fit-build-YYYY-MM-DD.zip` and attached to
a GitHub **Immutable Release** (never committed to git history) — see `.env.example` and the
rulebook for the full delivery procedure.

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

## Testing

```bash
dotnet test ClinicNow.sln          # backend (once test projects exist)
cd UI/clinicnow_desktop && flutter test
cd UI/clinicnow_mobile && flutter test
```

## Tech stack

.NET 10 · ASP.NET Core Web API · Entity Framework Core + SQL Server · Mapster · JWT auth ·
RabbitMQ · ML.NET (recommender) · PayPal sandbox · Flutter (desktop + mobile) · Docker Compose.
