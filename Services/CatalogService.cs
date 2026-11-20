using System.Globalization;
using NileShop.Data;
using NileShop.Models;
using NileShop.ViewModels;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
namespace NileShop.Services;
public class CatalogService(ApplicationDbContext db)
{
    public static string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    public static string Name(Product p) => p.Translations.FirstOrDefault(t => t.LanguageCode == Language)?.Name ?? p.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Name ?? p.Slug;
    public static string Name(Category c) => c.Translations.FirstOrDefault(t => t.LanguageCode == Language)?.Name ?? c.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Name ?? c.Slug;
    public static ProductCard Card(Product p) => new(p.Id, p.Slug, Name(p), Name(p.Category), p.ImageUrl ?? "/assets/img/product-placeholder.svg", p.Price, p.OldPrice, p.StockQuantity, p.IsFeatured, !string.IsNullOrWhiteSpace(p.Sizes) || !string.IsNullOrWhiteSpace(p.Colors), (DateTime.UtcNow - p.CreatedAt).TotalDays <= 14)
    {
        Sizes = ProductOptions.Values(p.Sizes),
        Colors = ProductOptions.Values(p.Colors)
    };
    public IQueryable<Product> Active() => db.Products.AsNoTracking().Where(p => p.IsActive && !p.Category.IsHidden).Include(p => p.Translations).Include(p => p.Category).ThenInclude(c => c.Translations);
    public async Task<IReadOnlyList<CategoryLink>> CategoriesAsync(bool includeHidden = false) => (await db.Categories.AsNoTracking().Where(c => includeHidden || !c.IsHidden).Include(c => c.Translations).OrderBy(c => c.Id).ToListAsync()).Select(c => new CategoryLink(c.Id, c.Slug, Name(c))).ToList();
    public async Task<IReadOnlyList<CategoryProductGroup>> CategoryGroupsAsync(IReadOnlyList<CategoryLink> categories)
    {
        var counts=await db.Products.AsNoTracking().Where(p=>p.IsActive).GroupBy(p=>p.CategoryId).Select(g=>new {Id=g.Key,Count=g.Count()}).ToDictionaryAsync(g=>g.Id,g=>g.Count);
        // Limit previews in the database instead of loading the entire product catalog.
        var previews=await Active().Where(p=>db.Products.Count(other=>other.IsActive && other.CategoryId==p.CategoryId && other.Id>p.Id)<4).OrderByDescending(p=>p.Id).ToListAsync();
        return categories.Select(c=>new CategoryProductGroup(c,counts.GetValueOrDefault(c.Id),previews.Where(p=>p.CategoryId==c.Id).Select(Card).ToList())).ToList();
    }
    public async Task<Dictionary<int, (double Average, int Count)>> GetProductRatingsAsync(ApplicationDbContext db, IEnumerable<int> productIds)
    {
        var ids = productIds.ToList();
        if (ids.Count == 0) return new();
        var groups = await db.ProductReviews
            .AsNoTracking()
            .Where(r => r.IsApproved && ids.Contains(r.ProductId))
            .GroupBy(r => r.ProductId)
            .Select(g => new { ProductId = g.Key, Average = g.Average(r => (double)r.Rating), Count = g.Count() })
            .ToListAsync();
        return groups.ToDictionary(g => g.ProductId, g => (g.Average, g.Count));
    }
    public async Task<HashSet<int>> GetBestsellerIdsAsync(ApplicationDbContext db, int top = 3)
    {
        var ids = await db.OrderItems
            .AsNoTracking()
            .GroupBy(i => i.ProductId)
            .OrderByDescending(g => g.Sum(i => i.Quantity))
            .Take(top)
            .Select(g => g.Key)
            .ToListAsync();
        return ids.ToHashSet();
    }
}
