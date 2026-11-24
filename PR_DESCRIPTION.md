# Scrub Classification Fix, Quarantine, and Regression Tests — Chic & Unique

Branch: `security/scrub-quarantine-tests` (local commits only; not pushed to `main` or any remote).

---

## Summary of Changes

### 1. Fix Latent Destructive Defect in Legacy Queue Scrub Classification
- **Classification**: Latent defect in cleanup logic with conditional availability impact (loss of pending queued emails if the prior heuristic scrub were ever run against unprefixed DataProtection ciphertext rows created before the `"dp:v1:"` prefix was added), not a confirmed exploit.
- **What changed** ([Services/AccountMailSender.cs](Services/AccountMailSender.cs)):
  - Removed the heuristic `ScrubLegacyPlaintextAsync`.
  - Added read-only **`ClassifyLegacyQueueDryRunAsync`**: attempts `UnprotectBody` on `PayloadVersion == 0 && Body != ""`, returning counts + `(Id, PayloadVersion, BodySha256)` per bucket (`ProvenEncrypted`, `UnprovenUndecryptable`) without modifying any rows or logging body content.
  - Added separate **`ApplyLegacyReEnvelopeAsync`**: re-verifies `UnprotectBody` at apply time, re-enveloping only proven rows with `"dp:v1:"` and `PayloadVersion = 1` while preserving unproven rows untouched.
  - Wired `ClassifyLegacyQueueDryRunAsync` at startup in [Program.cs](Program.cs) (logs counts only; never auto-applies or auto-clears).

### 2. Explicit Per-Row Reviewed Clear (No Global Destroy Switch)
- Added **`ClearReviewedUnprovenRowsAsync`** accepting an explicit list of `ReviewedClearTarget(Id, ExpectedPayloadVersion, ExpectedBodySha256)`.
- Each clear uses a conditional `ExecuteUpdateAsync` matching `Id`, `PayloadVersion`, and exact observed `Body` (verified by SHA-256 hash). If a row changed concurrently, the update matches 0 rows and preserves it.
- Documented the backup → dry-run → apply → reviewed-clear procedure in [DEPLOYMENT_RUNBOOK.md](DEPLOYMENT_RUNBOOK.md).

### 3. Quarantine Undecryptable Rows in Dispatch (Fail Closed Without Destruction)
- Added `Quarantined` (`bool`, default `false`) to `AccountEmailQueue` ([Models/AccountEmail.cs](Models/AccountEmail.cs), [Data/DevelopmentSqlite.cs](Data/DevelopmentSqlite.cs)) and generated EF Core migration `20261124215401_AddAccountEmailQuarantine`.
- Updated `StoreWorker.SendAccountEmailsAsync` ([Services/StoreWorker.cs](Services/StoreWorker.cs)):
  - On `DecryptFailed`, sets `Quarantined = true`, `LastError = "DecryptFailed"`, and **preserves `Body` intact**.
  - Excludes quarantined rows from dispatch and from the 24h terminal-row retention purge.
  - Dispatches queued account emails every 2 seconds while keeping payment reconciliation/cleanup at 30 seconds.

### 4. Admin 8-Hour Absolute Expiry Fix Across Stamp Renewal
- Extracted cookie event handlers into [Services/AuthCookieEvents.cs](Services/AuthCookieEvents.cs) using injectable `TimeProvider`.
- Fixed a subtle bug where `SecurityStampValidator` setting `ShouldRenew = true` on a valid admin stamp could allow cookie renewal past `T + 8h`: `OnValidatePrincipalAsync` now clamps `ExpiresUtc = absExpiry`, `AllowRefresh = false`, `IsPersistent = false` even on renewal.

### 5. Regression Tests & Local Verification Results
Added in [tests/Program.cs](tests/Program.cs) and executed locally:
- `dotnet build --no-restore --nologo`: **Build succeeded (0 Warning(s), 0 Error(s))**.
- `dotnet run --project tests/CheckoutChecks.csproj`: **All 52 checks PASSED**, including:
  - `PASS dry-run classifies unprefixed ciphertext as proven-encrypted`
  - `PASS dry-run classifies undecryptable row as unproven`
  - `PASS dry-run modifies zero rows`
  - `PASS apply re-envelopes proven row once and is idempotent`
  - `PASS re-enveloped row keeps valid decryptable payload with dp:v1 prefix`
  - `PASS apply leaves unproven row body untouched`
  - `PASS reviewed clear skips row when snapshot hash does not match`
  - `PASS reviewed clear clears only explicitly matched snapshot`
  - `PASS undecryptable row is quarantined with body preserved and survives 24h purge`
  - `PASS expired terminal row older than 24h is purged`
  - `PASS concurrent payment-vs-release ends in consistent state without double release or lost funds` (independent `ApplicationDbContext` instances + `Barrier(2)`).
  - `PASS session remains valid before 5-minute stamp validation interval elapses` (controllable `FakeTimeProvider`).
  - `PASS chained SecurityStampValidator rejects session after 5-minute interval following stamp change`
  - `PASS admin sign-in anchors 8h absolute expiry and disables refresh`
  - `PASS admin expiry stays anchored to original T+8h even when stamp validator triggers renewal`
  - `PASS admin session is rejected after T+8h absolute cap`
- `node tests/security_js.cjs`: **PASS**.
- `python3 scripts/secret-scan.py`: **PASS (0 new secrets)**.
- `python3 tests/security_http.py`: **All 16 HTTP security checks PASSED** (CSP, fresh nonces, nosniff, DENY, no-store, Secure/HttpOnly cookies, antiforgery, rate limits, production host/SQL guards).
- `python3 tests/email_otp_http.py`: **All 6 checks PASSED** (signup OTP + replay rejection, 60s per-email cooldown throttling immediate repeat over HTTP, customer login OTP, admin OTP restriction, notifications, and one-time password reset).
- `python3 tests/nilepay_http.py`: **All 58 NilePay mock HTTP checks PASSED** (gateway fallback, bounded idempotent retries, webhook signature/timestamp/amount/currency validation, expired/failed/cancelled labels & admin filters, duplicate confirmation idempotency, polling reconciliation, verified reviews, and refund final state).
- Added a `build-and-test` job to `.github/workflows/secret-scan.yml` so GitHub Actions runs `dotnet build`, `CheckoutChecks`, `security_js.cjs`, `security_http.py`, `email_otp_http.py`, and `nilepay_http.py` alongside `secret-scan` and `dependency-audit`.
- **SQL Server pre-existing schema migration check**: Testcontainers/local SQL Server is unavailable in this environment. Local procedure for staging: restore a pre-migration SQL Server backup, seed an old-format unprefixed ciphertext row (`PayloadVersion = 0`), run `dotnet ef database update`, verify startup fail-loud check passes and dry-run classifies the row as `ProvenEncrypted`.

### 6. Preserved Controls
- Enforced CSP & security headers (`SecurityHeadersMiddleware`), lockout (5 password failures / 15m; 5 OTP attempts), non-sliding cooldowns, generic auth responses, upload magic-byte/size/random-name validation, and CI `dependency-audit` job are all preserved.

---

## Approval-Gated Follow-Up Actions (Not Performed Here)
- Push `security/scrub-quarantine-tests`, open/merge PR, and configure GitHub branch protection / required checks.
- Apply EF Core migrations (`dotnet ef database update`) to production before deploying.
- Run dry-run → backup → apply / reviewed clear in production only after owner review.
- Provision persistent `DataProtection:KeysPath` on the host after key-ring backup and staging decrypt check.
- Rotate database and admin credentials exposed in historical commits (`37b2189`, `27a1589`, `603d0bc`).
