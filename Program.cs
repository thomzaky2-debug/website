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
var sqlite = builder.Configuration.GetValue<bool>("Database:UseDevelopmentSqlite");
if (sqlite) DevelopmentSqlite.Initialize();
if (sqlite && !builder.Environment.IsDevelopment()) throw new InvalidOperationException("SQLite is restricted to Development. Production requires SQL Server.");
builder.Services.AddDbContext<ApplicationDbContext>(options => {
    if (sqlite) options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = builder.Configuration["Database:DevelopmentSqlitePath"] ?? "bubbleshop.db" }.ToString());
    else {
        var conn = builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(conn)) throw new InvalidOperationException("Missing connection string: configure ConnectionStrings:DefaultConnection in appsettings.json with your MonsterASP MS SQL connection string.");
        options.UseSqlServer(conn);
    }
});
builder.Services.AddDefaultIdentity<IdentityUser>(options => {
    options.Password.RequiredLength = 12;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.User.RequireUniqueEmail = true;
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.ConfigureApplicationCookie(o => {
    o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Lax; o.ExpireTimeSpan = TimeSpan.FromHours(2);
    o.LoginPath = "/Account/Login";
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
builder.Services.AddRazorPages();
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("customer-auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit=20, Window=TimeSpan.FromMinutes(5), QueueLimit=0, AutoReplenishment=true }));
});
builder.Services.Configure<RequestLocalizationOptions>(o => {
    o.DefaultRequestCulture = new RequestCulture("en");
    o.SupportedCultures = o.SupportedUICultures = new[] { new CultureInfo("en"), new CultureInfo("ar") };
    o.RequestCultureProviders = [new ScopedLanguageProvider(), new AcceptLanguageHeaderRequestCultureProvider()];
});
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o => {
    o.IdleTimeout = TimeSpan.FromMinutes(30); o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Lax;
});
builder.Services.AddAntiforgery(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);
builder.Services.AddHttpContextAccessor();
builder.Services.AddDataProtection().SetApplicationName("NileShop");
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.Configure<NilePayOptions>(builder.Configuration.GetSection("NilePay"));
builder.Services.AddHttpClient<NilePayClient>(http => http.Timeout = TimeSpan.FromSeconds(20))
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
app.UseExceptionHandler(error => error.Run(async context => {
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError(exception, "Unhandled request failure. Trace {TraceId}", context.TraceIdentifier);
    try {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuditService>().LogAsync("SYSTEM_ERROR", "Application", $"{exception?.GetType().Name}: {exception?.Message}; Trace {context.TraceIdentifier}");
    } catch (Exception auditError) { app.Logger.LogError(auditError, "Could not persist exception audit"); }

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "text/html; charset=utf-8";
    var details = exception is null ? "An unknown error occurred." : $"{exception.GetType().FullName}: {exception.Message}\n\n{exception.StackTrace}";
    await context.Response.WriteAsync($"<!DOCTYPE html><html><head><title>Application Error</title><style>body{{font-family:Segoe UI,Tahoma,sans-serif;padding:2rem;line-height:1.5;background:#f8f9fa;color:#333}}pre{{background:#fff;padding:1.25rem;border:1px solid #dee2e6;border-radius:4px;overflow:auto;font-size:0.9rem;}}h2{{color:#dc3545;margin-top:0;}}</style></head><body><h2>Application Diagnostic Details</h2><p><strong>Trace Identifier:</strong> {context.TraceIdentifier}</p><pre>{System.Net.WebUtility.HtmlEncode(details)}</pre></body></html>");
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
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-Frame-Options"] = "DENY";
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
        // Automatically create and migrate SQL Server schema on production
        await db.Database.MigrateAsync();
    }
    await DbInitializer.InitializeAsync(scope.ServiceProvider, builder.Configuration);
}
app.Run();
public partial class Program { }
