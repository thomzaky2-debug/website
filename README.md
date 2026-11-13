# BubbleThemeShop

ASP.NET Core 8 MVC, Identity, EF Core and SQL Server bilingual English/Arabic storefront. Uses the MIT BubbleThemeShop template assets; attribution is preserved in `wwwroot/assets/TEMPLATE-LICENSE.txt`.

## Configure and run

Install .NET 8 SDK. Configure secrets with environment variables or `dotnet user-secrets`:

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

1. Create a SQL Server database in the hosting panel. Supply its connection string and admin settings via secure hosting configuration; do not commit credentials.
2. Apply the included initial migration with `dotnet ef database update` (or `Update-Database` in Visual Studio Package Manager Console). Alternatively explicitly set `Database:ApplyMigrationsOnStartup=true` for first deployment, then disable it.
3. Publish with `dotnet publish -c Release -o publish`. Upload the publish directory, including the generated web.config, to the IIS application root. Use the .NET 8 Hosting Bundle and an application pool with No Managed Code.
4. Enable the site's HTTPS certificate and set `AllowedHosts` to the production hostname. Ensure IIS can write ASP.NET Data Protection keys to a persistent, access-controlled location; preserve these across deployments.
5. Browse `/Identity/Account/Login`, then `/Admin/Dashboard`. Registration requests redirect to login. Product/category mutations, order management and logs require the Admin role and antiforgery tokens.

The app expects to run at the site root. The in-memory cart/session requires one IIS worker; use a distributed session store before scaling horizontally. Checkout uses NilePay hosted payment and is disabled until merchant settings are configured (see NILEPAY.md). Prices are EGP and orders contain the final server-calculated prices. Stock decrements use guarded SQL updates within the order transaction. NilePay cancellations and refunds require merchant review; the dashboard blocks automatic cancellation and retains reserved stock. Cancelled and shipped orders cannot be reopened. Ordered products are archived instead of deleted. Fulfilment and customer contact are managed from the dashboard.

## Database quota and operations

Images are static files, not database blobs. Product tables are paginated, order and audit tables display the latest 100 entries. Use the dashboard log cleanup to delete audit records older than 30 days; the cleanup itself is audited. Monitor database usage in the hosting panel and back up orders and catalog data. The 1 GB quota is not enforced by the application. Unhandled failures are logged to both the server logger and, where database access is available, a fresh-scope audit entry without exception secrets.

## Verification

Build: `dotnet build`. Run relational checkout checks with `dotnet run --project tests/CheckoutChecks.csproj`. SQL Server migration deployment and IIS hosting must be verified against your actual hosting account. Do not treat local SQLite execution as SQL Server production verification.
