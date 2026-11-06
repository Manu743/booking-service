# Booking Service

Assessment API for room bookings, built with .NET 10, ASP.NET Core Controllers, Dapper and SQL Server 2022. All examples and seed data are synthetic. See [AI usage](AI_USAGE.md) and the [MIT license](LICENSE).

## Quickstart: Docker Compose

### Clone, start and test

With Git, Docker Compose v2 and the .NET 10 SDK installed (Docker engine running):

```bash
git clone https://github.com/Manu743/booking-service.git && cd booking-service
docker compose up -d --build
dotnet test
```

This is the **current test/demo edition**: no `.env` setup is required on a fresh checkout. Missing or empty `MSSQL_SA_PASSWORD` uses the intentionally public, disposable credential `LocalAssessmentOnly!2026` consistently for SQL, initialization and API access. It is not a real secret and must never protect real data. The earlier environment-only configuration is superseded for this test edition; this demo does not claim production or final environment-only secret compliance. Plain `dotnet test` runs the solution in Debug; integration tests create their own disposable SQL container.

### Prerequisites and optional password override

- Git and Docker with the Compose v2 plugin; start the Docker engine first. On Windows, use Docker Desktop with Linux containers/WSL2.
- SQL Server containers use `linux/amd64`; other architectures need compatible emulation. Allocate enough Docker memory for SQL Server and allow initial image downloads.
- Host TCP port 8080 must be available. The .NET SDK is only needed for local builds/tests, not the container quickstart.
- No password setup is required for this disposable demo. For an optional override, choose a unique, strong SQL password: at least eight characters, with characters from at least three of uppercase, lowercase, digits and symbols. **Double quotes (`"`) are rejected by `db/init.sh`**, because Compose embeds the password in a quoted SQL connection-string value.

Optionally, before Compose startup, supply `MSSQL_SA_PASSWORD` through the current terminal environment. In Bash, prompt without echoing it or putting its value in command history:

```bash
read -r -s -p 'Assessment SQL password: ' MSSQL_SA_PASSWORD
printf '\n'
export MSSQL_SA_PASSWORD
```

Alternatively, in PowerShell:

```powershell
$securePassword = Read-Host 'Assessment SQL password' -AsSecureString
$env:MSSQL_SA_PASSWORD = [System.Net.NetworkCredential]::new('', $securePassword).Password
Remove-Variable securePassword
```

Or create a private, git-ignored `.env` in the repository root and add `MSSQL_SA_PASSWORD` yourself. For example, the following is a **placeholder, not a usable password**:

```dotenv
MSSQL_SA_PASSWORD='<replace-with-your-own-strong-assessment-password>'
```

Replace the entire placeholder. Use Compose-compatible quoting (single quotes keep `$` literal), restrict access to the file, and never commit it or share resolved Compose configuration. An exported terminal value takes precedence over Compose's `.env` value; keep it consistent across restarts. `.env.example` must contain placeholders only, including `MSSQL_SA_PASSWORD=YOUR_STRONG_SQL_PASSWORD`. The owner is adding that template entry manually; this change did not inspect or edit either env file and does not claim the template update is verified. Compose constructs its own API connection string with the `BookingDatabase` key.

### Start and check readiness

```bash
docker compose up -d --build
docker compose ps -a
docker compose logs db-init
curl -i http://localhost:8080/health/database
```

Allow time for SQL Server startup and initialization; a detached `up` returning is not proof of readiness. SQL must become healthy, `db-init` must exit successfully (code 0), and the API's inherited HTTP/database healthcheck must pass. `/health/database` returns 200 with `status: "Connected"` and the database name, or 503 if it cannot connect.

- Swagger UI: <http://localhost:8080/swagger>
- OpenAPI: <http://localhost:8080/openapi/v1.json>
- Database health: <http://localhost:8080/health/database>

Only the API publishes a host port, bound exclusively to loopback (`127.0.0.1:8080:8080`). **SQL Server has no published host port**; services connect to `mssql` over the Compose network. Swagger/OpenAPI are enabled only in Development, which Compose explicitly selects. The API container runs as the runtime image's non-root application user.

This is **not a production deployment**: there is no authentication/authorization, Compose uses `sa`, SQL certificate validation is bypassed with `TrustServerCertificate=True`/`sqlcmd -C`, and HTTP/Development documentation are exposed for assessment use. Do not expose it to untrusted networks or put real data in it.

### Persistence, initialization and safe troubleshooting

The named `sql-data` volume persists the database. On a fresh database, [db/init.sh](db/init.sh) runs these scripts in explicit order:

1. [000_create_database.sql](db/000_create_database.sql)
2. [001_schema.sql](db/001_schema.sql)
3. [002_seed_golden.sql](db/002_seed_golden.sql)
4. [003_create_booking_procedure.sql](db/003_create_booking_procedure.sql)
5. [010_utilisation.sql](db/010_utilisation.sql)
6. [011_top_customers.sql](db/011_top_customers.sql)
7. [012_free_windows.sql](db/012_free_windows.sql)
8. [013_data_quality.sql](db/013_data_quality.sql)

Only after all scripts succeed does it write the database extended property `Booking.ContainerInitialization = v1`. Subsequent starts skip initialization for a marked database, preserving bookings and other changes. Marked databases created by an earlier bootstrap are not automatically repaired or migrated; the corrected procedure placement was verified on fresh disposable volumes only. This is not a migration mechanism: editing scripts does not upgrade an already marked database. The seed resets data and is run **only on the fresh path**; do not manually rerun it on data you want to keep.

An existing database without the marker is rejected, including a partially initialized database left after a script fails. SQL commands fail fast; the API will not start through the successful-init dependency. This fail-closed behavior avoids silently reseeding or accepting partial state.

Inspect before changing anything:

```bash
docker compose ps -a
docker compose logs mssql db-init api
```

Check Docker availability, optional environment overrides, port conflicts and the first initialization error. Redact logs before sharing. Changing the environment password (including reverting to the public default) does not rotate `sa` in an existing volume; a mismatch can cause SQL error 18456. Use the password that created it or perform deliberate database administration. A deliberately separate new project/volume initializes with the currently selected password; preserve old volumes. If `booking-service-fresh` is already running on 8080 with your private password, leave it unchanged: this test-edition configuration does not convert that existing stack to the default. For unmarked/partial state, preserve the volume and investigate/back up first; use a separate fresh assessment volume only by deliberate choice. Never add the marker merely to bypass an error.

To stop while retaining data:

```bash
docker compose down
```

**Data-loss warning:** `docker compose down -v` deletes the Compose project's named database volume and all its data. It is not a routine troubleshooting step. Run it only after explicitly choosing a disposable reset and backing up anything needed; a later startup would load the golden seed again. No reset is required for an ordinary restart.

## Restore, build and test

Install the SDK selected by [global.json](global.json) (`10.0.403`, `latestFeature` roll-forward), and run these exact solution commands from the root:

```bash
dotnet restore BookingService.slnx
dotnet build BookingService.slnx --no-restore -c Release
dotnet test BookingService.slnx --no-build -c Release --logger "console;verbosity=normal" --logger "trx" --results-directory TestResults
```

Integration tests require a running Docker engine. They use **Testcontainers**, creating their own SQL Server and applying the repository SQL scripts; they do **not** use or require a running Compose stack or its password/volume. Configuration tests invoke the Compose CLI with an explicitly empty temporary env file and synthetic process values; they never read the user's env files. Domain tests run without SQL. The [CI workflow](.github/workflows/CI.yml) also checks package vulnerability reports structurally and uploads TRX results even after test failures.

### Optional local API (without the API container)

Provision SQL Server 2022 with compatibility level 160 and initialize a dedicated assessment database using the ordered scripts above. The golden seed is destructive; use it only on disposable data. A host process cannot reach Compose SQL at `localhost:1433`, since Compose publishes no SQL port. Use a separately reachable SQL instance for this path.

Set `ConnectionStrings__BookingDatabase` in the process environment to your own connection string, replacing every placeholder below (do not commit the result):

```text
Server=<reachable-sql-host>,<port>;Database=BookingServiceDb;User Id=<assessment-user>;Password=<assessment-password>;Encrypt=True;TrustServerCertificate=True;
```

With that variable configured, a Bash local launch is:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Booking.Api/Booking.Api.csproj --no-launch-profile --urls http://localhost:8080
```

Normal ASP.NET Core configuration applies first: command-line configuration overrides environment variables, which override environment-specific/base appsettings. A nonblank `ConnectionStrings:BookingDatabase` already present in that configuration **skips dotenv loading entirely**. Only if it is missing/blank does startup search from `AppContext.BaseDirectory` upward for the first `.env`, load it with `Env.NoClobber()` (existing environment variables are not overwritten), and append an environment-variable configuration provider. In that fallback branch, the appended provider has final precedence. The local `.env` can supply `ConnectionStrings__BookingDatabase`; `MSSQL_SA_PASSWORD` alone does not configure a local API connection. **The local dotenv fallback has not been exercised in the current verification evidence.** Prefer explicit environment configuration.

## HTTP interface

Resource routes are controller-based; `/health/database` is a small Minimal API endpoint. There is no general bookings-list endpoint.

| Method | Path | Result / principal errors |
|---|---|---|
| GET | `/rooms` | 200 room array |
| POST | `/rooms` | 201 + relative `Location`; 400 validation |
| GET | `/customers` | 200 customer array |
| POST | `/customers` | 201 + relative `Location`; 400 validation |
| POST | `/bookings` | 201 + relative `Location`; 400 validation, 404 missing room/customer, 409 conflict, 422 idempotency mismatch |
| GET | `/bookings/{id}` | 200 booking; 400 nonpositive id, 404 missing |
| POST | `/bookings/{id}/cancel` | 200 updated booking; 400 nonpositive id, 404 missing, 409 already cancelled/started |
| GET | `/rooms/{id}/availability?date=yyyy-MM-dd&minMinutes=N` | 200 array of `{from,to}` windows; 400 invalid input, 404 missing room |
| GET | `/health/database` | 200 connected; 503 unavailable |

Rooms require `name` (nonblank, at most 100 characters), positive `capacity` and positive decimal `hourlyRate`. Customers require `name` (same bounds) and case-sensitive `tier`: `None`, `Silver` or `Gold`. Most validation uses `ValidationProblemDetails`; booking lookup/cancellation with a nonpositive id retains a small `{error}` response.

Bookings use **local wall-clock** `startAt`/`endAt` without `Z` or an offset. Both must align to 15-minute boundaries with zero seconds, stay within one day and opening hours, and have duration 30–480 minutes inclusive. Opening hours: Monday–Friday 07:00–22:00, Saturday 08:00–18:00, Sunday closed. Availability merges active blocked intervals with a 15-minute buffer on both sides, excludes cancelled bookings, and filters gaps by positive `minMinutes`; Sunday returns an empty array. A free window can end exactly at closing time.

### Synthetic curl walkthrough

Examples below use Bash and `curl` (use `curl.exe` rather than a PowerShell alias where needed). Create a new room/customer so the seeded conflicts cannot affect the example:

```bash
curl -i http://localhost:8080/rooms -H 'Content-Type: application/json' \
  -d '{"name":"Demo Room","capacity":6,"hourlyRate":40}'
curl -i http://localhost:8080/customers -H 'Content-Type: application/json' \
  -d '{"name":"Demo Customer","tier":"Silver"}'
curl -sS http://localhost:8080/rooms
curl -sS http://localhost:8080/customers
```

Read the `id` fields in the two POST response bodies (or the GET arrays); no `jq` is required. Enter those actual positive numbers below, not the golden IDs:

```bash
read -r -p 'New room id: ' ROOM_ID
read -r -p 'New customer id: ' CUSTOMER_ID
curl -i http://localhost:8080/bookings \
  -H 'Content-Type: application/json' -H 'Idempotency-Key: demo-2030-03-04-01' \
  -d "{\"roomId\":$ROOM_ID,\"customerId\":$CUSTOMER_ID,\"startAt\":\"2030-03-04T08:00:00\",\"endAt\":\"2030-03-04T10:00:00\"}"
read -r -p 'Booking id from the 201 response: ' BOOKING_ID
curl -i "http://localhost:8080/bookings/$BOOKING_ID"
curl -i "http://localhost:8080/rooms/$ROOM_ID/availability?date=2030-03-04&minMinutes=60"
curl -i -X POST "http://localhost:8080/bookings/$BOOKING_ID/cancel"
```

2030-03-04 is a Monday; choose a later valid weekday if running after it. For rate 40 and Silver, 08:00–10:00 gives subtotal 100, discount 10 and total 90. The booking body includes `id`, `roomId`, `customerId`, `startAt`, `endAt`, `status`, `segments`, `subtotal`, `discount`, `total`, `fee`. Cancellation changes `status` from `A` to `C`; at least 48 hours before start its fee is zero. A second cancellation returns 409.

Repeat the exact booking POST with the same IDs/key within 24 hours to replay the stored 201 response, including the original body and `Location`. Reuse that key with a different valid payload (for example `endAt` 10:15) to get 422. A replay is the original creation response, not a fresh view of a subsequently cancelled booking; GET shows current state. Use a new key for an independent walkthrough.

## Design decisions

- **Layers:** HTTP → controller orchestration → pure [domain services](src/Booking.Domain/Services) and Dapper [repositories](src/Booking.Api/Data/Repositories) → SQL. Domain has no HTTP/database dependency. No empty application layer is introduced.
- **Pricing:** split at 09:00, 12:00, 14:00 and 17:00. Weekday peak windows `[09:00,12:00)`/`[14:00,17:00)` cost 1.50×, off-peak 1.00×, Saturday 1.25×. All arithmetic is `decimal`; segment amounts/subtotal are not rounded before discounting. Silver is 10%, Gold 20%, None 0%; discount is capped at 50. Only the final total is rounded to two decimals with `AwayFromZero`. SQL stores subtotal/discount/total as `decimal(18,2)`, so storage precision is separate from the unrounded in-memory calculation. GET reconstructs segments using the current room rate while returning persisted totals.
- **Cancellation and time:** injected `TimeProvider.GetLocalNow().DateTime` matches timezone-free business timestamps. At least 48 hours remaining → 0% fee; at least 24 but under 48 → 50%; under 24 → 100%; at/after start → 409. Fee rounds to two decimals away from zero. A conditional active-only UPDATE persists status, fee and cancellation timestamp atomically; concurrent cancellation has one winner. Business local time follows the host/container local zone, not a configured customer timezone. Idempotency expiry instead uses `GetUtcNow()` and UTC storage, since it measures elapsed lifetime rather than a business appointment.
- **Concurrency:** `dbo.usp_CreateBooking` checks active bookings under `SERIALIZABLE`, `UPDLOCK` and `HOLDLOCK`, using `IX_Bookings_Room_StartAt` for range locking before insert. The symmetric conflict condition is `newStart < existingEnd + 15 minutes AND existingStart < newEnd + 15 minutes`; equality at the buffer boundary is allowed. Buffer may extend past closing. Only deadlock-victim error 1205 is retried: three total attempts, waits 20 ms then 50 ms; ambiguous timeouts are not retried. The idempotent path retries the whole transaction. A unique slot-table design was rejected: the golden seed intentionally contains conflicts, so slot uniqueness would reject it or leave seeded rows outside conflict protection. Per-room application locks are another possible trade-off, but would serialize all bookings for that room rather than rely on indexed range locks.
- **Canonical persisted idempotency:** optional `Idempotency-Key`, maximum 200 characters; absent/blank means ordinary creation. SHA-256 hashes the parsed `roomId|customerId|startAt|endAt` with invariant IDs and round-trip ISO dates, not raw JSON. A locked SQL key lookup, booking creation and stored status/body share one transaction. Valid entries at most 24 hours old replay identical content; another hash returns 422. Expired/incomplete entries are replaced transactionally. Conflicts are not cached. Keys survive API restarts; normal input/resource validation still precedes the repository replay lookup.

## SQL reports and golden outputs (B3–B6)

After successful fresh Compose initialization, open an interactive SQL session inside the container (Bash host syntax; no host SQL port or host `sqlcmd` installation needed):

```bash
docker compose exec mssql bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -b -V 16 -d BookingServiceDb'
```

At the `sqlcmd` prompt, enter the following SQL (`GO` submits each batch; `QUIT` exits):

```sql
EXEC dbo.usp_RoomUtilisation @RoomId=1, @FromDate='2026-03-02', @ToDate='2026-03-08';
GO
EXEC dbo.usp_TopCustomersByMonth;
GO
EXEC dbo.usp_FreeWindows @RoomId=1, @Date='2026-03-02', @MinMinutes=60;
GO
EXEC dbo.usp_ConflictingBookings;
GO
QUIT
```

These expected outputs describe the unchanged golden seed (R1 is room ID 1; C1–C4 are seeded customer names). Mutating seeded rows can change them.

### B3: R1, 2026-03-02 through 2026-03-08

Active booked minutes exclude buffer; every calendar day is included. Closed days have NULL utilisation.

| Date | OpenMinutes | BookedMinutes | Utilisation % |
|---|---:|---:|---:|
| 2026-03-02 | 900 | 360 | 40.00 |
| 2026-03-03 | 900 | 0 | 0.00 |
| 2026-03-04 | 900 | 0 | 0.00 |
| 2026-03-05 | 900 | 0 | 0.00 |
| 2026-03-06 | 900 | 0 | 0.00 |
| 2026-03-07 | 600 | 0 | 0.00 |
| 2026-03-08 | 0 | 0 | NULL |

### B4: monthly top three ranks, including ties

Revenue is active `Total` plus cancelled `Fee`, grouped by booking-start month. `DENSE_RANK` preserves both customers tied at rank 3.

| Month | Customer | Revenue | Rank |
|---|---|---:|---:|
| 2026-03 | C2 | 271.25 | 1 |
| 2026-03 | C1 | 156.25 | 2 |
| 2026-03 | C3 | 135.00 | 3 |
| 2026-03 | C4 | 135.00 | 3 |
| 2026-04 | C4 | 180.00 | 1 |
| 2026-04 | C1 | 37.50 | 2 |
| 2026-04 | C3 | 25.00 | 3 |

### B5: R1, 2026-03-02, minimum 60 minutes

Active buffered intervals are merged and clipped to opening hours before finding gaps.

| From | To |
|---|---|
| 10:15 | 11:15 |
| 15:45 | 22:00 |

### B6: active conflicting pairs

Each pair is emitted once, lower ID first. These deliberate seed conflicts are not defects to remove from the dataset.

| LowerId | HigherId |
|---:|---:|
| 2 | 3 |
| 6 | 7 |
| 9 | 10 |

## B7: query performance and index trade-offs

[db/PERFORMANCE.md](db/PERFORMANCE.md) documents the date-range rewrite, proposed covering index, index alternatives, write-cost trade-offs and a SQL Server 2022 benchmark with 1,000,000 synthetic rows. The recorded range query with explicit columns used 3,121 logical reads with a non-covering index and 8 with the covering index. The improvement came from index coverage together with the explicit projection, not the date rewrite alone; the original `CONVERT(date, ...)` query also used a dynamic seek in that benchmark. These are documented benchmark observations, not a measurement rerun during this README update or a guarantee for other workloads.

For a date parameter `@d`, use explicit columns and a half-open range:

```sql
SELECT Id, RoomId, CustomerId, StartAt, EndAt, Status
FROM dbo.Bookings
WHERE StartAt >= @d
  AND StartAt < DATEADD(DAY, 1, @d)
  AND Status <> 'C';
```

This exposes a sargable range instead of wrapping every `StartAt` in `CONVERT(date, ...)`, and uses the actual status code `C`, not `Cancelled`. SQL Server can sometimes transform date conversions into dynamic seeks; a scan-to-seek improvement is not guaranteed without inspecting the actual plan. Existing indexes lead with room/customer, not `StartAt` alone, so they do not guarantee an efficient global date seek.

A candidate for this explicit projection is `(StartAt) INCLUDE (RoomId, CustomerId, EndAt, Status)` (the clustered ID is implicitly present). Coverage can avoid key lookups caused by requesting columns absent from an index; `SELECT *` defeats that narrow coverage. A filtered index for active rows may be smaller if the active subset is selective and the query predicate matches; `(Status, StartAt)` is another workload-dependent alternative. Wider/more indexes cost storage, write amplification and maintenance; filtered indexes add predicate/parameterization caveats. None of these proposed indexes has been added to the schema: the current application does not expose this global date report, so an additional index's write cost is not yet justified. Re-run the benchmark with representative data, actual execution plans and IO/time measurements before selecting an index for a real workload or claiming production gains.

## Time spent

Total development time: **18 hours**, spread over **3 days at 6 hours per day**.
