# Verification

- .NET 8 build: passed, zero warnings and errors.
- Eight relational SQLite checks: server-calculated totals, inventory decrement, idempotent retry, transaction rollback, no order on failure, English fallback, terminal status restrictions, pending cancellation.
- Eight HTTPS smoke checks: rendered seeded storefront, product details, antiforgery rejection, blocked registration, protected admin routes, Arabic culture cookie and RTL, admin login/dashboard, bilingual editor views.
- NilePay integration: local mock HTTPS end-to-end checks cover persisted retry keys/payloads across restart, signatures, timestamps, event deduplication, early/unknown sessions, mismatched totals/currency, nonconfirmed states and authenticated reconciliation. No live payment calls.
- SQL Server initial and NilePay EF migrations and model snapshot generated. Idempotent SQL script: `Data/initial.sql`.

SQL Server 2025 execution, live MonsterASP.NET/IIS deployment, visual browser QA and production load tests have not been performed. Local relational tests use SQLite; they do not establish SQL Server behavior. Real hosting requires the database connection, admin bootstrap settings, domain, HTTPS and persistent Data Protection keys described in README.md.

Run `dotnet run --project tests/CheckoutChecks.csproj` for relational checks. On Linux, `python3 tests/http_smoke.py` runs HTTPS checks using the workspace `.dotnet/dotnet` SDK, randomized test credentials and an isolated development database. It stops its server on completion.

NilePay update: clean build; 25 local mock payment HTTP checks, eight checkout checks, and eight storefront HTTPS checks passed. Updated Release IIS publish output was generated. Live NilePay payments and SQL Server deployment remain unverified.
