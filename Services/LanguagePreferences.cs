using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using NileShop.Data;
namespace NileShop.Services;

public static class LanguagePreferences
{
    public static bool IsAdminPath(string path) => new PathString(path).StartsWithSegments("/Admin")
        || new PathString(path).StartsWithSegments("/StoreAdmin") || new PathString(path).StartsWithSegments("/Identity");
    public static string ClaimType(bool admin) => admin ? "nileshop:language:admin" : "nileshop:language:storefront";
    public static string CookieName(bool admin) => admin ? ".NileShop.Language.Admin" : ".NileShop.Language.Storefront";
    public static bool Valid(string? culture) => culture is "en" or "ar";
    public static void WriteCookie(HttpResponse response, bool admin, string culture) => response.Cookies.Append(
        CookieName(admin), CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
        new CookieOptions { Path = "/", Expires = DateTimeOffset.UtcNow.AddYears(1), HttpOnly = true,
            Secure = true, IsEssential = true, SameSite = SameSiteMode.Lax });

    public static async Task SaveAsync(ApplicationDbContext db, string userId, bool admin, string culture)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var type = ClaimType(admin);
        await db.UserClaims.Where(claim => claim.UserId == userId && claim.ClaimType == type).ExecuteDeleteAsync();
        db.UserClaims.Add(new IdentityUserClaim<string> { UserId = userId, ClaimType = type, ClaimValue = culture });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}

public class ScopedLanguageProvider : RequestCultureProvider
{
    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext context)
    {
        var admin = LanguagePreferences.IsAdminPath(context.Request.Path.Value ?? "/");
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (context.User.Identity?.IsAuthenticated == true && userId is not null) {
            // Account preference wins over browser cookies, including on shared computers.
            var db = context.RequestServices.GetRequiredService<ApplicationDbContext>();
            var type = LanguagePreferences.ClaimType(admin);
            var culture = await db.UserClaims.AsNoTracking().Where(claim => claim.UserId == userId && claim.ClaimType == type)
                .OrderByDescending(claim => claim.Id).Select(claim => claim.ClaimValue).FirstOrDefaultAsync(context.RequestAborted);
            return LanguagePreferences.Valid(culture) ? new ProviderCultureResult(culture!) : null;
        }
        var cookie = new CookieRequestCultureProvider { CookieName = LanguagePreferences.CookieName(admin) };
        var result = await cookie.DetermineProviderCultureResult(context);
        return result is not null && result.Cultures.All(culture => LanguagePreferences.Valid(culture.Value))
            && result.UICultures.All(culture => LanguagePreferences.Valid(culture.Value)) ? result : null;
    }
}
