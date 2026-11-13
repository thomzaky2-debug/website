using BubbleThemeShop.Services;
using BubbleThemeShop.ViewModels;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace BubbleThemeShop.Controllers;
public class HomeController(CatalogService catalog) : Controller
{
    public async Task<IActionResult> Index(string? q, string? category, int page = 1)
    {
        page = Math.Clamp(page, 1, 100000);
        q = q?.Trim(); if (q?.Length > 100) q = q[..100];
        var query = catalog.Active();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(p => p.Translations.Any(t => t.Name.Contains(q)));
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(p => p.Category.Slug == category);
        var count = await query.CountAsync();
        var products = await query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id).Skip((page - 1) * 12).Take(12).ToListAsync();
        var featured = await catalog.Active().Where(p => p.IsFeatured).OrderBy(p => p.Id).Take(4).ToListAsync();
        return View(new CatalogViewModel { Products = products.Select(CatalogService.Card).ToList(), Featured = featured.Select(CatalogService.Card).ToList(), Categories = await catalog.CategoriesAsync(), Search = q, Category = category, Page = page, TotalPages = (int)Math.Ceiling(count / 12d) });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SetLanguage(string culture, string returnUrl)
    {
        if (culture is not ("en" or "ar")) return BadRequest();
        Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), HttpOnly = true, Secure = true, IsEssential = true, SameSite = SameSiteMode.Lax });
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() { Response.StatusCode = 500; return View(); }
}
