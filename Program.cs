using System.Globalization;
using System.Threading.RateLimiting;
using NileShop;
using NileShop.Data;
using NileShop.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true).AddEnvironmentVariables();
if (!builder.Environment.IsDevelopment() && (string.IsNullOrWhiteSpace(builder.Configuration["AllowedHosts"]) || builder.Configuration["AllowedHosts"]!.Split(';').Any(host => host.Trim() == "*")))
    throw new InvalidOperationException("Configure explicit AllowedHosts for production.");
if (!builder.Environment.IsDevelopment()) {
    var pubUrl = builder.Configuration["NilePay:PublicUrl"];
    if (!Uri.TryCreate(pubUrl, UriKind.Absolute, out var pubUri) || pubUri.Scheme != Uri.UriSchemeHttps || pubUri.AbsolutePath != "/" || !string.IsNullOrEmpty(pubUri.UserInfo) || !string.IsNullOrEmpty(pubUri.Query) || !string.IsNullOrEmpty(pubUri.Fragment))
        throw new InvalidOperationException("Configure a valid https NilePay:PublicUrl for production.");
}
builder.WebHost.ConfigureKestrel(o => { o.AddServerHeader = false; o.Limits.MaxRequestBodySize = 18 * 1024 * 1024; });
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => { o.MultipartBodyLengthLimit = 16 * 1024 * 1024; o.ValueLengthLimit = 65536; o.ValueCountLimit = 256; });
var sqlite = builder.Configuration.GetValue<bool>("Database:UseDevelopmentSqlite");
if (sqlite) DevelopmentSqlite.Initialize();
if (sqlite && !builder.Environment.IsDevelopment()) throw new InvalidOperationException("SQLite is restricted to Development. Production requires SQL Server.");
builder.Services.AddDbContext<ApplicationDbContext>(options => {
    if (sqlite) options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = builder.Configuration["Database:DevelopmentSqlitePath"] ?? "bubbleshop.db" }.ToString());
    else {
        var conn = builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(conn)) throw new InvalidOperationException("Missing connection string: configure ConnectionStrings:DefaultConnection in appsettings.json with your MonsterASP MS SQL connection string.");
        if (!builder.Environment.IsDevelopment()) {
            var sql = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(conn);
            if (sql.Encrypt == Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Optional || sql.TrustServerCertificate)
                throw new InvalidOperationException("Production SQL Server connections require Encrypt=True and TrustServerCertificate=False.");
        }
        options.UseSqlServer(conn, sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
    }
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning));
});
builder.Services.AddDefaultIdentity<IdentityUser>(options => {
    options.Password.RequiredLength = 12;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.User.RequireUniqueEmail = true;
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));
builder.Services.ConfigureApplicationCookie(o => {
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.IsEssential = true;
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    o.SlidingExpiration = true;
    o.LoginPath = "/Account/Login";
    o.Events.OnSigningIn = context => {
        if (context.Principal?.IsInRole("Admin") == true) {
            context.Properties.AllowRefresh = false;
            context.Properties.IsPersistent = false;
            context.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8);
        }
        return Task.CompletedTask;
    };
    o.Events.OnCheckSlidingExpiration = context => {
        if (context.Principal?.IsInRole("Admin") == true) context.ShouldRenew = false;
        return Task.CompletedTask;
    };
    o.Events.OnValidatePrincipal = context => {
        if (context.Principal?.IsInRole("Admin") == true && (context.Properties.ExpiresUtc is null || (context.Properties.IssuedUtc.HasValue && context.Properties.ExpiresUtc.Value - context.Properties.IssuedUtc.Value > TimeSpan.FromHours(8.5)))) {
            context.RejectPrincipal();
        }
        return Task.CompletedTask;
    };
    o.Events.OnRedirectToLogin = context => {
        var admin = context.Request.Path.StartsWithSegments("/Admin") || context.Request.Path.StartsWithSegments("/StoreAdmin");
        var path = admin ? "/Identity/Account/Login" : "/Account/Login";
        var destination=HttpMethods.IsGet(context.Request.Method)
            ? (context.Request.PathBase+context.Request.Path+context.Request.QueryString).ToString()
            : admin ? "/Admin/Dashboard" : context.Request.Path.StartsWithSegments("/Saved") ? "/saved" : "/Account";
        context.Response.Redirect(path + QueryString.Create("returnUrl",destination));
        return Task.CompletedTask;
    };
});
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddControllersWithViews(o => o.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()))
    .AddViewLocalization().AddDataAnnotationsLocalization(o => o.DataAnnotationLocalizerProvider = (_, f) => f.Create(typeof(SharedResource)));
builder.Services.AddRazorPages(o => o.Conventions.AddAreaPageApplicationModelConvention("Identity", "/Account/Login", model => model.EndpointMetadata.Add(new Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute("admin-auth"))));
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) => {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        return ValueTask.CompletedTask;
    };
    options.AddPolicy("customer-auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit=20, Window=TimeSpan.FromMinutes(5), QueueLimit=0, AutoReplenishment=true }));
    options.AddPolicy("admin-auth", context => HttpMethods.IsPost(context.Request.Method)
        ? RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit=20, Window=TimeSpan.FromMinutes(5), QueueLimit=0, AutoReplenishment=true })
        : RateLimitPartition.GetNoLimiter<string>("read"));
    options.AddPolicy("checkout", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit=20, Window=TimeSpan.FromMinutes(1), QueueLimit=0, AutoReplenishment=true }));
    options.AddPolicy("payment-status", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit=60, Window=TimeSpan.FromMinutes(1), QueueLimit=0, AutoReplenishment=true }));
});
builder.Services.Configure<RequestLocalizationOptions>(o => {
    o.DefaultRequestCulture = new RequestCulture("en");
    o.SupportedCultures = o.SupportedUICultures = new[] { new CultureInfo("en"), new CultureInfo("ar") };
    o.RequestCultureProviders = [new ScopedLanguageProvider(), new AcceptLanguageHeaderRequestCultureProvider()];
});
builder.Services.AddDistributedMemoryCache();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<EmailOtp>();
builder.Services.AddScoped<AccountMailSender>();
builder.Services.AddSession(o => {
    o.IdleTimeout = TimeSpan.FromDays(7);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Lax;
});
builder.Services.AddAntiforgery(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);
builder.Services.AddHttpContextAccessor();
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("NileShop");
var keyDirectory = builder.Configuration["DataProtection:KeysPath"];
if (string.IsNullOrWhiteSpace(keyDirectory))
    keyDirectory = Path.Combine(builder.Environment.ContentRootPath, ".dataprotection-keys");
dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.Configure<NilePayOptions>(builder.Configuration.GetSection("NilePay"));
builder.Services.AddHttpClient<NilePayClient>(http => { http.Timeout = TimeSpan.FromSeconds(20); http.MaxResponseContentBufferSize = 65536; })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<NilePayService>();
builder.Services.AddScoped<OrderOperations>();
builder.Services.Configure<SubdomainOptions>(builder.Configuration.GetSection("Subdomains"));
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<CustomerAdminService>();
builder.Services.AddScoped<ProductImageService>();
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddHostedService<StoreWorker>();
var app = builder.Build();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler(error => error.Run(async context => {
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError(exception, "Unhandled request failure. Trace {TraceId}", context.TraceIdentifier);
    try {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuditService>().LogAsync("SYSTEM_ERROR", "Application", $"{exception?.GetType().Name}; Trace {context.TraceIdentifier}");
    } catch (Exception auditError) { app.Logger.LogError(auditError, "Could not persist exception audit"); }

    context.Response.StatusCode = exception is BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "text/html; charset=utf-8";
    context.Response.Headers.CacheControl = "no-store";
    await context.Response.WriteAsync($"<!doctype html><html lang=\"en\"><head><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Unable to complete request</title></head><body><h1>Unable to complete your request</h1><p>Please try again later.</p><p>Reference: {System.Net.WebUtility.HtmlEncode(context.TraceIdentifier)}</p></body></html>");
}));
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseStatusCodePages(async statusCodeContext => {
    var ctx = statusCodeContext.HttpContext;
    if (ctx.Response.StatusCode == StatusCodes.Status404NotFound && HttpMethods.IsGet(ctx.Request.Method)) {
        var originalPath = ctx.Request.Path;
        var originalQuery = ctx.Request.QueryString;
        try {
            ctx.Request.Path = "/Home/NotFound";
            ctx.Request.QueryString = QueryString.Empty;
            await statusCodeContext.Next(ctx);
        } finally {
            ctx.Request.Path = originalPath;
            ctx.Request.QueryString = originalQuery;
        }
    }
});
app.UseHttpsRedirection();
app.Use(async (context, next) => {
    if (context.Request.Path.StartsWithSegments("/Identity/Account/Register", StringComparison.OrdinalIgnoreCase)) {
        context.Response.Redirect("/Identity/Account/Login"); return;
    }
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseSession();
app.UseAuthentication();
app.UseRequestLocalization();
app.UseMiddleware<SubdomainMiddleware>();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();
await using (var scope = app.Services.CreateAsyncScope()) {
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (sqlite) {
        await db.Database.EnsureCreatedAsync();
        await DevelopmentSqlite.EnsureWalletSchemaAsync(db);
    }
    else {
        if (builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false)) await db.Database.MigrateAsync();
        else {
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('EmailRateLimits', 'AccountEmailQueues')";
            var tableCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            if (tableCount < 2)
                throw new InvalidOperationException("Database schema is missing EmailRateLimits or AccountEmailQueues. Apply EF Core migrations (dotnet ef database update) before starting the application with Database:ApplyMigrationsOnStartup=false.");
        }

    }
    await DbInitializer.InitializeAsync(scope.ServiceProvider, builder.Configuration);
}
app.Run();
public partial class Program { }
