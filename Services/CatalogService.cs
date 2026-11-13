using System.Globalization;
using BubbleThemeShop.Data;
using BubbleThemeShop.Models;
using BubbleThemeShop.ViewModels;
using Microsoft.EntityFrameworkCore;
namespace BubbleThemeShop.Services;
public class CatalogService(ApplicationDbContext db)
{
    public static string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    public static string Name(Product p) => p.Translations.FirstOrDefault(t => t.LanguageCode == Language)?.Name ?? p.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Name ?? p.Slug;
    public static string Name(Category c) => c.Translations.FirstOrDefault(t => t.LanguageCode == Language)?.Name ?? c.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Name ?? c.Slug;
    public static ProductCard Card(Product p) => new(p.Id, p.Slug, Name(p), Name(p.Category), p.ImageUrl ?? "/assets/img/products/shoe1.jpg", p.Price, p.OldPrice, p.StockQuantity, p.IsFeatured);
    public IQueryable<Product> Active() => db.Products.AsNoTracking().Where(p => p.IsActive).Include(p => p.Translations).Include(p => p.Category).ThenInclude(c => c.Translations);
    public async Task<IReadOnlyList<CategoryLink>> CategoriesAsync() => (await db.Categories.AsNoTracking().Include(c => c.Translations).OrderBy(c => c.Id).ToListAsync()).Select(c => new CategoryLink(c.Id, c.Slug, Name(c))).ToList();
}
