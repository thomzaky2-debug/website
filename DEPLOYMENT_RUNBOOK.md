# Deployment & Operations Runbook — Chic & Unique

This runbook lists the human-executed production steps that require explicit owner approval and host/database access.

---

## 1. Pre-Deploy EF Core Migration

When `Database:ApplyMigrationsOnStartup` is `false` (the production default):

1. Back up the production SQL Server database.
2. Apply pending EF Core migrations **before** deploying/starting the new application binary:
   ```bash
   dotnet ef database update --connection "<PRODUCTION_CONNECTION_STRING>"
   ```
   *(Or set `Database:ApplyMigrationsOnStartup=true` deliberately for a single startup, then revert to `false`.)*
3. Verify the startup schema check passes without throwing `InvalidOperationException` for `EmailRateLimits` or `AccountEmailQueues`.

---

## 2. Durable DataProtection Key Storage

Current code (`Program.cs`) persists keys to `DataProtection:KeysPath` if configured, or `<ContentRootPath>/.dataprotection-keys` otherwise, with `SetApplicationName("NileShop")`. Under ephemeral container/app-pool storage without a persistent volume, restarts would regenerate keys and invalidate auth cookies, stored SMTP passwords, and queued email payloads.

**Migration checklist:**
1. **Back up** the existing key ring directory (`.dataprotection-keys`) before changing any path.
2. **Provision** persistent storage outside ephemeral hosting directories (e.g. a mounted volume or dedicated non-webroot folder with restricted ACLs).
3. **Copy** the backed-up key ring into the new `DataProtection:KeysPath` and keep `SetApplicationName("NileShop")` unchanged.
4. **Staging decrypt check**: In staging, encrypt a synthetic throwaway payload under the same key ring and application name, persist it, restart the process, and verify it decrypts cleanly before cutting over production.
5. **Cut over** by setting `DataProtection__KeysPath` in hosting environment variables, and retain the backup.

---

## 3. Credential Rotation

Historic secrets remain recoverable from public Git history (`37b2189`, `27a1589`, `603d0bc`) even after rotation. Rewriting Git history does **not** replace rotation — rotation is the effective step.

1. Make the GitHub repository **Private** (if not already).
2. Rotate the **SQL Server database password** and update `ConnectionStrings__DefaultConnection` in hosting settings (must use `Encrypt=True;TrustServerCertificate=False;`).
3. Rotate the **Admin account password** and any reused **admin email / SMTP password**.
4. Review database and `AuditLogs` for any unknown activity.

---

## 4. Reviewed Cleanup of Quarantined / Unproven Legacy Queue Rows

`AccountEmailQueues` startup dry-run logs counts only and never destroys undecryptable rows. If dry-run or dispatch reports `UnprovenUndecryptable` or `Quarantined` (`DecryptFailed`) rows:

1. **Back up affected rows first** to a secured, restricted-access file outside the repository (so a wrong review is recoverable):
   ```sql
   SELECT Id, PayloadVersion, Quarantined, SentAtUtc, LastError, Body, CreatedAtUtc
   FROM AccountEmailQueues
   WHERE (PayloadVersion = 0 AND Body <> '') OR Quarantined = 1;
   ```
2. **Apply safe re-envelope for proven rows**: run `AccountMailSender.ApplyLegacyReEnvelopeAsync` (re-verifies decrypt at apply time; re-envelopes proven rows with `"dp:v1:"` and `PayloadVersion = 1`; leaves unproven rows untouched).
3. **Explicit per-row reviewed clear**: after human review of the dry-run `(Id, PayloadVersion, BodySha256)` list and backup confirmation, invoke `AccountMailSender.ClearReviewedUnprovenRowsAsync` with the exact reviewed `(Id, ExpectedPayloadVersion, ExpectedBodySha256)` targets. Any row modified concurrently is skipped and preserved. Ordinary logs record counts and row IDs only — never body content.

---

## 5. GitHub Actions & Branch Protection

1. Verify `.github/workflows/secret-scan.yml` runs on GitHub Actions (`secret-scan` and `dependency-audit` jobs).
2. Optionally enable branch protection on `main` requiring `secret-scan` and `dependency-audit` checks to pass before merge.
