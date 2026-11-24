# Security Regression Fixes — Chic & Unique

Branch: `fix/hardening-regressions` (local commits only; not pushed to `main` or any remote).

---

## Summary of Fixes

### 1. EF Core Migration & Startup Schema Check for Email Tables
- **Conditional impact**: On existing SQL Server databases with `Database:ApplyMigrationsOnStartup=false`, `EmailRateLimits` and `AccountEmailQueues` would be missing unless provisioned beforehand.
- **What changed**:
  - Generated EF Core migration `20261124210757_AddAccountEmailQueueAndRateLimits` (`Migrations/`).
  - Added an early startup check in `Program.cs` (non-SQLite branch when `ApplyMigrationsOnStartup=false`) that verifies both tables exist in `INFORMATION_SCHEMA.TABLES` and throws an actionable `InvalidOperationException` if missing.
  - Updated `SECURITY.md` with the required pre-deploy migration step.
- **Required pre-deploy step**: Run `dotnet ef database update` against the target SQL Server database **before** deploying the updated application when `Database:ApplyMigrationsOnStartup=false`.

### 2. Chained `SecurityStampValidator` & Anchored Admin 8-Hour Expiry
- **What changed**:
  - In `Program.cs` (`OnValidatePrincipal`), chained `await SecurityStampValidator.ValidatePrincipalAsync(context);` **before** applying admin lifetime checks, restoring 5-minute security-stamp invalidation on password/stamp changes.
  - In `OnSigningIn`, anchored `AdminAbsoluteExpiresUtc` in `AuthenticationProperties.Items` on initial sign-in so any stamp-triggered re-issue or `RefreshSignInAsync` preserves the original 8-hour absolute cap and never resets it.

### 3. Encrypted Email Queue Payloads, Terminal-State Clearing, Retention & Non-Sliding Cooldown
- **What changed**:
  - `AccountMailSender.QueueAsync` encrypts `Body` at rest using ASP.NET Core Data Protection (`NileShop.AccountEmailQueue.v1`).
  - `StoreWorker.SendAccountEmailsAsync` decrypts `Body` only at dispatch time, clears `Body = ""` at every terminal state (sent, final failed attempt `Attempts >= 5`, expired, or superseded OTP), and purges terminal rows older than 24 hours.
  - `PasswordRecoveryController.Forgot` sets `expiresAtUtc = +1h` on queued reset-link emails.
  - `EmailOtp.SendAsync` checks the non-sliding per-email cooldown **before** clearing the active session challenge, so rate-limited resends do not invalidate an active OTP.
  - Updated `Otp_Intro` in `Resources/SharedResource.en.resx` to conditional wording so the UI does not assert delivery before background dispatch.
- **Key-protection, backup & rotation plan**:
  - `AccountEmailQueue` relies on the existing `NileShop` Data Protection key ring (`.dataprotection-keys` or `DataProtection:KeysPath`). Back up the key ring before moving `KeysPath` outside the webroot, keep `SetApplicationName("NileShop")`, and rotate keys on standard ASP.NET Core Data Protection schedule (old keys remain readable for decryption until existing queued rows expire/purge within 24h).
- **One-time legacy plaintext cleanup (requires approval before running anywhere shared)**:
  ```sql
  -- Clear terminal/expired rows and expire any legacy plaintext pending rows:
  UPDATE AccountEmailQueues SET Body = '' WHERE SentAtUtc IS NOT NULL OR Attempts >= 5;
  UPDATE AccountEmailQueues SET SentAtUtc = GETUTCDATE(), LastError = 'LegacyPlaintextExpired', Body = '' WHERE SentAtUtc IS NULL;
  ```
- **Cooldown denial-of-service trade-offs**:
  - Per-address cooldowns (1/min, 5/hour) are non-sliding and preserve active challenges and generic responses. Residual risk: an attacker knowing a victim's email can still trigger the 1/min or 5/hour cap to delay the victim's own reset/OTP requests.

### 4. Secret-Scan CI Workflow Path
- Moved `secret-scan.yml` to `.github/workflows/secret-scan.yml`.
- Verified `scripts/secret-scan.py` locally against a synthetic fixture (exits non-zero on hit) and against the clean repo (exits 0).

### 5. Isolated Regression Tests
- Added tests in `tests/Program.cs` covering: encrypted-at-rest queue payloads, non-sliding cooldown (`LastSentUtc` unchanged on rejection), and single-release idempotency in `ReleaseInventoryIfUnpaidAsync`.
- All `CheckoutChecks`, `security_js.cjs`, and `secret-scan.py` checks pass locally.

---

## Approved-Only Follow-Up Actions
- Push `fix/hardening-regressions` and open/merge PR (requires `workflow` token scope or adding `.github/workflows/secret-scan.yml` via GitHub UI).
- Apply EF migration (`dotnet ef database update`) and run legacy cleanup SQL (if any plaintext rows exist) before deploying.
- Rotate exposed database & admin credentials from historical commits (`37b2189`, `27a1589`, `603d0bc`).
- Enable GitHub Actions secret-scan workflow & branch protection.
