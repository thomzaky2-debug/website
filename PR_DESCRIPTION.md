# Remaining Repository Hardening — Chic & Unique

Branch: `security/remaining-hardening` (local commits only; not pushed to `main` or any remote).

---

## Summary of Changes

### 1. Explicit `PayloadVersion` Marker & Safe Idempotent Legacy Plaintext Scrub
- Added `PayloadVersion` (`int`, default `0` for legacy plaintext, `1` for `"dp:v1:"`-prefixed DataProtection payloads) to `AccountEmailQueue` ([Models/AccountEmail.cs](Models/AccountEmail.cs), [Data/DevelopmentSqlite.cs](Data/DevelopmentSqlite.cs)) and generated EF Core migration `20261124213415_AddAccountEmailPayloadVersion`.
- Updated `AccountMailSender.QueueAsync` to store `PayloadVersion = 1` with `"dp:v1:"` envelope prefix.
- Added `AccountMailSender.ScrubLegacyPlaintextAsync` to clear `Body = ""` and mark `SentAtUtc` / `LastError = "LegacyPlaintextScrubbed"` only on rows confirmed legacy (`PayloadVersion == 0` and not starting with `"dp:v1:"`). Ambiguous rows are skipped and counted; no scrubbed content is logged.
- **Note**: Clearing stored bodies removes readable secrets from `AccountEmailQueues`, but any OTP/reset token already issued remains valid until its own expiry.

### 2. Durable DataProtection Key Storage & Deployment Runbook
- Added `"DataProtection": { "KeysPath": "" }` template entries to `appsettings.json` and `appsettings.Production.json`.
- Created [DEPLOYMENT_RUNBOOK.md](DEPLOYMENT_RUNBOOK.md) documenting:
  - Pre-deploy `dotnet ef database update` when `ApplyMigrationsOnStartup=false`.
  - Key-ring backup, persistent storage provisioning, synthetic staging decrypt check, and cutover while preserving `SetApplicationName("NileShop")`.
  - Credential rotation (database & admin passwords; noting Git history rewrite does not replace rotation).

### 3. Regression Tests (`tests/Program.cs`)
- Added isolated tests for:
  - Legacy plaintext scrub (scrubs `PayloadVersion == 0`, preserves `PayloadVersion == 1`, skips ambiguous `dp:v1:` rows, idempotent on repeat).
  - Cross-instance DataProtection key persistence (encrypt in `dpA`, decrypt in `dpB` using persisted key directory).
  - Upload validation (valid PNG magic bytes accepted with random filename; `<script>` with `.png` extension rejected).
- Verified `dotnet build --no-restore --nologo` (0 warnings, 0 errors), `dotnet run --project tests/CheckoutChecks.csproj` (all 45+ checks passed), `node tests/security_js.cjs` (passed), and `python3 scripts/secret-scan.py` (passed).

### 4–6. Audits (Headers, Lockout, Uploads)
- **Security headers**: `SecurityHeadersMiddleware` enforces `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`, `Cache-Control: no-store` on sensitive/authenticated routes, and nonce-based CSP (`script-src 'self' 'nonce-...'`, `frame-ancestors 'none'`, `object-src 'none'`, `base-uri 'none'`). No duplicate header middleware remains.
- **Lockout & OTP limits**: 5 password failures / 15m lockout, 5 attempts per OTP challenge, and non-sliding per-email cooldowns (1/min, 5/h) are preserved with generic responses. Residual DoS trade-off: an attacker knowing a victim's email can still trigger the per-address cooldown.
- **Upload validation**: `ProductImageService` already enforces admin-only access, 4 MB per-file / 16 MB total limits, magic-byte validation (PNG/JPEG/WebP), and `Guid.NewGuid()` random filenames.

### 7. Dependency Vulnerability Gate in CI
- Added a `dependency-audit` job to `.github/workflows/secret-scan.yml` running `dotnet restore` with `NuGetAuditMode=all` and `WarningsAsErrors="NU1901;NU1902;NU1903;NU1904"`, plus `dotnet list package --vulnerable --include-transitive` failing non-zero on any advisory or unverified scan.

---

## Approved-Only Follow-Up Actions (Out of Scope Here)
- Push `security/remaining-hardening`, open/merge PR, and enable GitHub Actions branch protection / required checks.
- Apply EF Core migrations (`dotnet ef database update`) to production before deploying.
- Provision persistent `DataProtection:KeysPath` on the host after key-ring backup & staging decrypt check.
- Rotate database and admin credentials exposed in historical commits (`37b2189`, `27a1589`, `603d0bc`).
