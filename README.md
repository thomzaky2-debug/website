# NileShop

ASP.NET Core 10 MVC, Identity, EF Core and SQL Server bilingual English/Arabic storefront. Uses the MIT Bubble theme template assets; attribution is preserved in `wwwroot/assets/TEMPLATE-LICENSE.txt`.

## Configure and run

Install .NET 10 SDK (10.0.404 or newer supported servicing release). Configure secrets with environment variables or `dotnet user-secrets`:

```sh
dotnet user-secrets set 'ConnectionStrings:DefaultConnection' 'Server=YOUR_SERVER;Database=YOUR_DATABASE;User Id=YOUR_USER;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=False'
dotnet user-secrets set 'AdminSettings:Email' 'your-admin@example.com'
dotnet user-secrets set 'AdminSettings:Password' 'YOUR_UNIQUE_STRONG_PASSWORD'
dotnet tool restore
dotnet ef database update
dotnet run
```

The password must have at least 12 characters, upper/lowercase, a digit and punctuation. No default credentials are included. Startup seeds the Admin role, configured admin and bilingual sample catalog. Existing admin passwords are never reset on restart. Remove the bootstrap password after the account has been created. Set `AllowedHosts` to the actual hostname in production configuration. All session, authentication, culture and antiforgery cookies require HTTPS.

For local development only, set `ASPNETCORE_ENVIRONMENT=Development` and `Database__UseDevelopmentSqlite=true` to use a local SQLite file instead. This requires a system SQLite library (`libsqlite3` on Linux) and uses EnsureCreated and is not a replacement for validating SQL Server migrations. Production rejects SQLite.

## Windows IIS / MonsterASP.NET

1. Set `ConnectionStrings__DefaultConnection` in the hosting panel with the existing SQL Server connection string. Use `Encrypt=True;TrustServerCertificate=False`. Set `AdminSettings__Email` and `AdminSettings__Password` only when creating the first administrator.
2. Apply the included SQL Server migrations with `dotnet ef database update` (or `Update-Database` in Visual Studio Package Manager Console). Alternatively, set `Database__ApplyMigrationsOnStartup=true` for initial setup, then disable it.
3. Publish with `dotnet publish -c Release -o publish`. Upload the publish directory, including the generated web.config, to the IIS application root. Use the .NET 10 Hosting Bundle and an application pool with No Managed Code.
4. Enable the site's HTTPS certificate and set `AllowedHosts` to the production hostname. Ensure IIS can write ASP.NET Data Protection keys to a persistent, access-controlled location; preserve these across deployments.
5. Browse `/Identity/Account/Login`, then `/Admin/Dashboard`. The legacy Identity registration route redirects to admin login. Customer signup and login are available at `/Account/Register` and `/Account/Login`. Product/category mutations, order management and logs require the Admin role and antiforgery tokens.

For HTTP 500.30 on MonsterASP.NET, open the site's **Logs → ASP.NET Core debug**, enable logging briefly, and restart to capture the first startup exception. Check **Scripting** for the runtime and environment variables, then disable debug logging after collecting the error.

The app expects to run at the site root. The in-memory cart/session requires one IIS worker; use a distributed session store before scaling horizontally. Checkout uses NilePay hosted payment and is disabled until merchant settings are configured (see NILEPAY.md). Prices are EGP and orders contain the final server-calculated prices. Stock decrements use guarded SQL updates within the order transaction. The order operations page reconciles NilePay status before cancelling an unpaid order and releases stock once. Refunds are recorded only after the merchant completes them externally; NileShop does not issue a NilePay refund. Cancelled and shipped orders cannot be reopened. Ordered products are archived instead of deleted. Fulfilment and customer contact are managed from the dashboard.

## Database quota and operations

Images are static files, not database blobs. Product tables are paginated, order and audit tables display the latest 100 entries. Use the dashboard log cleanup to delete audit records older than 30 days; the cleanup itself is audited. Monitor database usage in the hosting panel and back up orders and catalog data. The 1 GB quota is not enforced by the application. Unhandled failures are logged to both the server logger and, where database access is available, a fresh-scope audit entry without exception secrets.

## Verification

Build: `dotnet build`. Run relational checkout checks with `dotnet run --project tests/CheckoutChecks.csproj`. SQL Server migration deployment and IIS hosting must be verified against your actual hosting account. Do not treat local SQLite execution as SQL Server production verification.

## NileShop store operations

Customers can filter and sort the catalog, choose configured sizes/colors, select a delivery area, pay via NilePay, and use a private order link at `/orders/track?token=...` to see updates and carrier details. The payment page checks status periodically while visible. A background worker also reconciles nonterminal NilePay attempts; webhook confirmation remains authoritative. Admins can search orders, reconcile, cancel eligible unpaid orders, record external refunds, dispatch with tracking, retry queued emails, manage contact/policies and delivery methods, and export a dated sales CSV. Product uploads accept up to six JPEG/PNG/WebP files of 4 MB each.

Starting delivery values are Cairo/Giza: EGP 50, 3–5 business days; other governorates: EGP 80, 5–7 business days. The return request window is 14 days. Edit these under `/StoreAdmin/Settings`. Enter actual contact email and phone there. SMTP is disabled by default. Configure it from `/StoreAdmin/Settings` under Order emails; the password is encrypted using ASP.NET Core Data Protection and is never displayed after saving. Persist the Data Protection key ring securely across restarts and deployments, or saved SMTP passwords will become unreadable. NilePay API credentials remain in private server configuration and are shown only as a configuration status in admin settings. Emails are queued durably and retry up to eight times; SMTP delivery may duplicate a message after a process crash following send. Keep the private order link confidential.

The SQL Server migration `NileShopEnhancements` and regenerated idempotent `Data/initial.sql` include the new tables and columns. Apply migrations to production SQL Server before deploying the new binary. Existing local SQLite preview databases created by `EnsureCreated` require a backed-up schema upgrade; do not delete them if they contain orders.

The `AdminMailSettings` SQL Server migration adds the protected SMTP configuration table. Apply it before deploying this version.

## Customer accounts

Storefront navigation includes customer login/signup and a signed-in account page at `/Account`. Registration uses ASP.NET Core Identity password hashing, unique emails, password confirmation and the existing password policy. Customer login uses lockout protection; customer auth POSTs are limited to 20 attempts per remote IP per five minutes. New accounts receive no administrative roles. Admin login remains at `/Identity/Account/Login`. Return URLs must be local, and registration never grants access to past guest orders by matching an unverified email. Guest checkout and private tracking links remain available.

## Catalog search

The shop supports bounded multiword matching across English/Arabic names, short descriptions, category names and slugs. Best-match ordering favors exact names, name prefixes and phrase matches; price and newest sorting remain available. Suggestions return at most six active products, respect category/price/stock filters and cancel superseded browser requests. This is database-backed substring search, not a typo-correction or personalized recommendation engine; large-catalog load testing and a full-text search index remain separate deployment work.

Home (`/`) is a discovery landing page with category cards, featured picks and latest arrivals. Shop (`/shop`) provides the complete grouped catalog, search, suggestions, filters and sorting. Old root URLs containing catalog query parameters redirect to Shop while preserving their parameters.

## Customer experience features

Saved items (`/saved`) persist per signed-in account. My orders (`/Account/Orders`) shows only orders linked to that account at checkout. Existing guest orders are deliberately not attached by matching email. Product reviews require a paid, shipped purchase by that account with no payment review flag, are limited to one per product, and remain private until an administrator publishes them at `/StoreAdmin/Reviews`. Administrators can search customer contact profiles and account order counts at `/StoreAdmin/Customers`. Product removal archives products referenced by saved lists, reviews or orders. Apply the `CustomerExperience` SQL Server migration before deploying this release.

## Device/browser support

A shared responsive layer covers phone, tablet and desktop layouts in both languages. Navigation collapses below 1101px, catalog grids adapt to available width, complex forms stack, and wide admin tables scroll inside keyboard-focusable regions. Touch targets, safe-area spacing, iOS field text sizing, reduced motion and no-JavaScript navigation are included. Product zoom falls back to opening the image when native dialogs are unavailable.

Manual device coverage still needs verification in Android Chrome/Firefox, iPhone/iPad Safari, Windows Chrome/Edge/Firefox and macOS Safari/Chrome at phone/tablet/desktop sizes and browser zoom. The local HTTPS smoke suite verifies flows, not the visual rendering of each browser.

`https://localhost:5443` refers only to the device running the app. Cross-device access needs a deployed HTTPS URL with a publicly trusted certificate. Configure the deployment hostname in `AllowedHosts`, set `NilePay:PublicUrl` to that HTTPS origin and register its webhook URL. Keep local secrets excluded from publish output. Do not point a phone at its own localhost or bypass certificate warnings to test payments.

Language preferences are independent for the storefront and admin routes. Guests use separate secure cookies for each area; signed-in users use separate account preferences stored in Identity claims. Customers choose English or Arabic at signup or in **My account → Preferred language**. Saved account preferences take precedence over browser cookies, including on shared computers, and are restored on other devices after login. Without a saved choice, the browser language is used when supported, with English as the default. The old shared ASP.NET culture cookie is ignored.


## Security configuration

Read [SECURITY.md](SECURITY.md) for the review findings, checks and deployment requirements. Production configuration contains no credentials: supply `ConnectionStrings__DefaultConnection` and a semicolon-separated `AllowedHosts` through your host's secure settings. Creating the first administrator also requires `AdminSettings__Email` and `AdminSettings__Password`. SQL connections must use `Encrypt=True;TrustServerCertificate=False`, and SMTP delivery requires TLS. Apply reviewed SQL migrations with a separate deployment identity; automatic production migrations are disabled by default. The running application should have only its required database permissions.

Authentication, session and antiforgery cookies require HTTPS. Behind a reverse proxy, configure forwarding only for trusted proxy addresses and retain HTTPS awareness. Persist ASP.NET Core Data Protection keys outside the webroot with access restricted to the application identity (and protected shared keys for multiple instances). An in-memory cart/session and per-process rate limiter are suitable for one application instance; use shared session storage and an edge/distributed limiter before scaling across instances.

Existing administrator passwords are never reset from configuration during startup. Remove the bootstrap password after initial provisioning. Any credential previously committed must be rotated at its provider; deleting it from the current tree does not remove it from Git history. Keep deployment secrets, private certificates and local configuration out of publish archives.

### Client notifications deployment

Admin → Notifications offers explicit Everyone or Choose a customer audiences, customer search by name/email, and a live message preview. Selecting a customer is required for private messages; leaving the email blank never broadcasts. Signed-in clients have an unread badge (refreshes every minute while the tab is visible), a paginated inbox, and asynchronous mark-as-read. Notifications do not use SMTP or browser push and do not appear outside the website. Broadcasts remain visible to future registered customers. Admin sending history shows the latest 30 messages.

Before deploying this release, apply the `20261123195250_ClientNotifications` EF migration to SQL Server using a deployment identity. It creates the notification tables and reconciles the delivery settings/Delivered constraint previously added at startup. Schema alterations no longer run unconditionally on application startup. Generate a reviewed script with `.dotnet/dotnet ef migrations script 20261120010635_SupportedLtsSecurityBaseline 20261123195250_ClientNotifications --idempotent --output notifications-deploy.sql`. Back up the database before applying. The local SQLite development schema is upgraded automatically.

Verification uses `.dotnet/dotnet run --project tests/CheckoutChecks.csproj` and `python3 tests/email_otp_http.py` after building. The latter uses an isolated local TLS mail server and database; it does not send messages to real customers.

If SQL Server reported the 904-byte `PK_NotificationReads` warning when applying the notification migration, apply the subsequent `NotificationReadKeySize` migration or `deploy/notification-read-key-fix.sql`. It preserves read receipts, uses an integer primary key and retains a unique nonclustered index on notification/user pairs. Apply the original notification migration first. SQLite retains its supported composite primary key. The SQL correction is compatible with the initially deployed notification code.
