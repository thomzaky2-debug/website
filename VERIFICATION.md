# Verification

- .NET 8 build: passed, zero warnings and errors.
- Eight relational SQLite checks: server-calculated totals, inventory decrement, idempotent retry, transaction rollback, no order on failure, English fallback, terminal status restrictions, pending cancellation.
- Eight HTTPS smoke checks: rendered seeded storefront, product details, antiforgery rejection, blocked registration, protected admin routes, Arabic culture cookie and RTL, admin login/dashboard, bilingual editor views.
- NilePay integration: local mock HTTPS end-to-end checks cover persisted retry keys/payloads across restart, signatures, timestamps, event deduplication, early/unknown sessions, mismatched totals/currency, nonconfirmed states and authenticated reconciliation. No live payment calls.
- SQL Server initial and NilePay EF migrations and model snapshot generated. Idempotent SQL script: `Data/initial.sql`.

SQL Server 2025 execution, live MonsterASP.NET/IIS deployment, visual browser QA and production load tests have not been performed. Local relational tests use SQLite; they do not establish SQL Server behavior. Real hosting requires the database connection, admin bootstrap settings, domain, HTTPS and persistent Data Protection keys described in README.md.

Run `dotnet run --project tests/CheckoutChecks.csproj` for relational checks. On Linux, `python3 tests/http_smoke.py` runs HTTPS checks using the workspace `.dotnet/dotnet` SDK, randomized test credentials and an isolated development database. It stops its server on completion.

NilePay update: clean build; 25 local mock payment HTTP checks, eight checkout checks, and eight storefront HTTPS checks passed. Updated Release IIS publish output was generated. Live NilePay payments and SQL Server deployment remain unverified.

## Project review — 2026-11-14

Fixed local configuration copying into build/publish artifacts; blank optional SMTP values and disabled credential decryption; controller-level checkout response retries; stale inactive cart entries; catalog and delivery amounts with fractional piastres; gateway category length; refunded/cancelled payment page state; decimal sales aggregation.

Validation: clean .NET build and Release publish; relational checkout checks including one-time cancellation stock release, late payment review, duplicate refunds and confirmation after refund; HTTPS smoke checks including disabled SMTP, invalid product edits, image upload and stale cart pruning; full isolated NilePay HTTP mock suite including checkout response retry and refunded payment-page behavior. The original local configuration remains in the workspace.

Limits: these checks use local SQLite and a mock payment gateway. SQL Server deployment/concurrency, a real SMTP provider, and live bank settlement were not exercised. Browser screenshot access was unavailable for visual layout verification.

## Customer experience upgrade

Added account-owned saved items and order history, purchase-verified reviews with admin moderation, admin customer search and storefront service links. The local preview database was backed up before adding the new schema. Tests cover saved-item idempotency, anonymous login redirects, account/admin separation, guest-order isolation, rejection of unverified reviews, moderation/publication, checkout ownership and the existing NilePay mock flows. EF reports no pending model changes after `CustomerExperience`; SQL Server deployment still needs its migration applied.
