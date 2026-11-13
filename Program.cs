using System.Globalization;
using BubbleThemeShop;
using BubbleThemeShop.Data;
using BubbleThemeShop.Services;
using Microsoft.AspNetCore.Diagnostics;
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
    else options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection."));
});
builder.Services.AddDefaultIdentity<IdentityUser>(options => {
    options.Password.RequiredLength = 12;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.User.RequireUniqueEmail = true;
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.ConfigureApplicationCookie(o => {
    o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Lax; o.ExpireTimeSpan = TimeSpan.FromHours(2);
    o.LoginPath = "/Identity/Account/Login";
});
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddControllersWithViews(o => o.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()))
    .AddViewLocalization().AddDataAnnotationsLocalization(o => o.DataAnnotationLocalizerProvider = (_, f) => f.Create(typeof(SharedResource)));
builder.Services.AddRazorPages();
builder.Services.Configure<RequestLocalizationOptions>(o => {
    o.DefaultRequestCulture = new RequestCulture("en");
    o.SupportedCultures = o.SupportedUICultures = new[] { new CultureInfo("en"), new CultureInfo("ar") };
    o.RequestCultureProviders = [new CookieRequestCultureProvider()];
});
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o => {
    o.IdleTimeout = TimeSpan.FromMinutes(30); o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Lax;
});
builder.Services.AddAntiforgery(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.Configure<NilePayOptions>(builder.Configuration.GetSection("NilePay"));
builder.Services.AddHttpClient<NilePayClient>(http => http.Timeout = TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<NilePayService>();
var app = builder.Build();
app.UseExceptionHandler(error => error.Run(async context => {
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError(exception, "Unhandled request failure. Trace {TraceId}", context.TraceIdentifier);
    try {
        // Fresh scope avoids persisting any pending changes left by a failed request.
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuditService>().LogAsync("SYSTEM_ERROR", "Application", $"{exception?.GetType().Name}; Trace {context.TraceIdentifier}");
    } catch (Exception auditError) { app.Logger.LogError(auditError, "Could not persist exception audit"); }
    context.Response.Redirect("/Home/Error");
}));
if (!app.Environment.IsDevelopment()) app.UseHsts();
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
app.UseRequestLocalization();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();
await using (var scope = app.Services.CreateAsyncScope()) {
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (sqlite) await db.Database.EnsureCreatedAsync();
    else if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup")) await db.Database.MigrateAsync();
    await DbInitializer.InitializeAsync(scope.ServiceProvider, builder.Configuration);
}
app.Run();
public partial class Program { }
