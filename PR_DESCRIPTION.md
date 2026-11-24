# Security Hardening PR — Chic & Unique

Branch: `security/hardening-fixes` (local commits only; not pushed to `main` or any remote).

---

## Summary of Findings & Fixes

### Priority 1: Exposed Secrets in Public Git History (Owner Actions Prepared)
- **What changed**:
  - Added `scripts/secret-scan.py` (redacted scanner checking working tree and recent commits, excluding the recorded historical baseline `37b2189`, `27a1589`, `603d0bc`).
  - Added `.github/workflows/secret-scan.yml` to fail CI on any new secret.
  - Updated `.gitignore` to cover `appsettings.Local.json`, `appsettings.*.Local.json`, `appsettings.Secrets.json`, and `appsettings.Development.json`.
  - Documented safe SQL Server connection string requirements (`Encrypt=True;TrustServerCertificate=False;`) in `SECURITY.md` (already enforced at startup in `Program.cs`).
- **Files touched**: `.gitignore`, `SECURITY.md`, `scripts/secret-scan.py`, `.github/workflows/secret-scan.yml`.
- **Owner actions required**: See the **Owner Action Checklist** below.

### Priority 2: Role-Differentiated Session Lifetimes (`Program.cs`)
- **What changed**:
  - Replaced the 60-day global cookie lifetime with **14-day sliding sessions for customers** and **8-hour non-sliding sessions for admins**.
  - Removed hardcoded `o.Cookie.MaxAge = TimeSpan.FromDays(60)` so browser cookie expiry is governed by the server-side ticket.
  - Added `OnSigningIn` (sets `AllowRefresh = false`, `IsPersistent = false`, `ExpiresUtc = +8h` for `Admin` role), `OnCheckSlidingExpiration` (sets `ShouldRenew = false` for `Admin`), and `OnValidatePrincipal` (rejects any `Admin` ticket with lifetime > 8.5h or missing `ExpiresUtc`).
  - Kept `HttpOnly = true`, `SecurePolicy = CookieSecurePolicy.Always`, `SameSite = SameSiteMode.Lax`, and `SecurityStampValidatorOptions.ValidationInterval = 5 minutes`.
- **Behavior change**: Admins will be signed out after 8 hours regardless of activity; customers stay signed in for 14 days with sliding renewal.
- **Files touched**: `Program.cs`.

### Priority 3: Bounded Oldest-First Expired Order Release & Open Unpaid Limits
- **What changed**:
  - Updated `StoreWorker` to query only expired unpaid orders (`PaymentStatus == "Unpaid" && !InventoryReleased && PaidAt == null && Status == Pending && CreatedAt <= expiryCutoff`), ordered oldest-first (`CreatedAt`, `Id`), in bounded batches (`batchSize = 20`, `maxBatches = 10`) honoring `stoppingToken`.
  - Updated `NilePayService.ReleaseInventoryIfUnpaidAsync` to perform an atomic conditional `ExecuteUpdateAsync` state guard (`!InventoryReleased && PaidAt == null && PaymentStatus == "Unpaid" && Status != Cancelled`) inside the serializable transaction before incrementing product stock, preventing double-release or releasing paid orders.
  - Added configurable open unpaid order limits in `CheckoutService` (`Checkout:MaxOpenUnpaidOrdersPerAccount` default `3`, `Checkout:MaxOpenUnpaidOrdersPerIp` default `5`), returning localized `Checkout_TooManyUnpaid` errors.
- **Behavior change**: Customers/IPs exceeding 3 open unpaid orders per account or 5 per IP (30m window) are blocked from creating additional unpaid orders until existing ones are paid or expire.
- **Files touched**: `Services/StoreWorker.cs`, `Services/NilePayService.cs`, `Services/CheckoutService.cs`, `Resources/SharedResource.en.resx`, `Resources/SharedResource.ar.resx`.

### Priority 4: Per-Email Persistent Cooldown & Background Email Dispatch
- **What changed**:
  - Added `EmailRateLimit` and `AccountEmailQueue` entities (`Models/AccountEmail.cs`, `Data/ApplicationDbContext.cs`, `Data/DevelopmentSqlite.cs`).
  - Enforced persistent per-normalized-email cooldowns (1 request per 60s, 5 per 60m) in `AccountMailSender.CheckAndRecordCooldownAsync`, applied identically to known and unknown emails in `PasswordRecoveryController.Forgot` and `EmailOtp.SendAsync`.
  - Queued OTP and password-reset emails in `AccountEmailQueues` and dispatched them in `StoreWorker.SendAccountEmailsAsync` with bounded retries (`Attempts < 5`) and exponential backoff, re-checking `EmailOtp.IsChallengeActive` before sending so expired/superseded OTPs are skipped.
  - No codes, tokens, reset links, or SMTP credentials are logged.
- **Note for owner**: Check your SMTP provider sending limits and SPF/DKIM records, as delayed codes were previously observed and the external provider cause is unconfirmed.
- **Files touched**: `Models/AccountEmail.cs`, `Data/ApplicationDbContext.cs`, `Data/DevelopmentSqlite.cs`, `Services/AccountMailSender.cs`, `Services/EmailOtp.cs`, `Controllers/PasswordRecoveryController.cs`, `Services/StoreWorker.cs`.

### Priority 5: Strict `PublicUrl` for Production Reset Links
- **What changed**:
  - Added startup validation in `Program.cs` requiring a valid `https://` `NilePay:PublicUrl` in non-Development environments.
  - Updated `PasswordRecoveryController.PublicOrigin()` to never fall back to `Request.Host` in non-Development environments.
- **Files touched**: `Program.cs`, `Controllers/PasswordRecoveryController.cs`.

### Priority 6: Lower-Severity Hardening & Proposals
- **Deduplicated security headers**: Removed redundant `X-Content-Type-Options`, `Referrer-Policy`, and `X-Frame-Options` inline middleware in `Program.cs` (`SecurityHeadersMiddleware` sets them in one place).
- **Proposed (not silently applied — awaiting owner decision)**:
  1. **Lockout & early registration**: Combining per-IP + per-account lockout counting and treating registrations as unconfirmed until the first emailed OTP is entered. Trade-off: shared-IP users (e.g. mobile carrier NAT) could affect each other under per-IP limits, and unconfirmed accounts require cleanup/expiry.
  2. **CSP inline styles & HSTS**: Removing `'unsafe-inline'` from `style-src` requires auditing all inline `style="..."` attributes across Razor views; raising HSTS `max-age` to 6–12 months should be done only after confirming HTTPS works across all served endpoints.
  3. **DataProtection `KeysPath` migration plan**: Do not change `KeysPath` directly (it would invalidate stored SMTP passwords and auth cookies). Safe plan: (a) back up `.dataprotection-keys`, (b) copy keys to the new path outside webroot, (c) keep `SetApplicationName("NileShop")`, (d) verify decryption in staging, (e) switch `DataProtection:KeysPath` and keep the backup.

---

## Extra Audit Results
- **Dependency vulnerability scan**: `dotnet list package --vulnerable --include-transitive` reported **0 vulnerable packages**.
- **Secret scan**: `python3 scripts/secret-scan.py` passed with **0 new secrets** in the working tree or recent commits.
- **Build & tests**: `dotnet build --no-restore`, `dotnet run --project tests/CheckoutChecks.csproj`, and `node tests/security_js.cjs` passed.

---

## Owner Action Checklist (In Order)
1. **Make the GitHub repository Private** (note: making it Private does not erase copies already cloned/cached).
2. **Rotate all exposed credentials**: change the SQL Server database password, change the Admin account password, and change the admin email password if reused elsewhere. (Purging Git history does not replace rotating passwords.)
3. **Review database and admin audit logs** for any unknown activity.
4. **Confirm production configuration**: verify `AllowedHosts` lists only your real domain(s) and `NilePay:PublicUrl` is set to your `https://` origin.
5. **Confirm email provider limits & DNS**: check SMTP provider rate limits, SPF, and DKIM records.
6. **Decide on proposals**: approve/reject the lockout/registration trade-offs and HSTS/CSP inline-style changes.
7. **Plan DataProtection key-ring move** using the backup-and-verify steps above.
8. **Deploy** the approved changes (and optionally purge historical Git commits `37b2189..27a1589, 603d0bc` after rotation).

---

## What Could Not Be Verified Locally
- Live production `appsettings.Production.json` / hosting environment variables (`AllowedHosts`, `NilePay:PublicUrl`, SQL connection string).
- Whether the currently deployed production build matches `HEAD`.
- Live SMTP provider delivery latency, rate limits, SPF, and DKIM configuration.

