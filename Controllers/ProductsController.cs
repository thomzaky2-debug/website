using BubbleThemeShop.Services;
using BubbleThemeShop.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace BubbleThemeShop.Controllers;
public class ProductsController(CatalogService catalog) : Controller
{
    [HttpGet("/products/{slug}")]
    public async Task<IActionResult> Details(string slug)
    {
        var p = await catalog.Active().SingleOrDefaultAsync(p => p.Slug == slug);
        if (p is null) return NotFound();
        var t = p.Translations.FirstOrDefault(t => t.LanguageCode == CatalogService.Language) ?? p.Translations.FirstOrDefault(t => t.LanguageCode == "en");
        var related = await catalog.Active().Where(x => x.CategoryId == p.CategoryId && x.Id != p.Id).OrderBy(x => x.Id).Take(4).ToListAsync();
        return View(new ProductDetailsViewModel(CatalogService.Card(p), t?.ShortDescription ?? "", t?.FullDescription ?? "", related.Select(CatalogService.Card).ToList()));
    }
}
