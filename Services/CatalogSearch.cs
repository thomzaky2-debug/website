using NileShop.Models;
namespace NileShop.Services;
public static class CatalogSearch
{
    public static string Clean(string? value)
    {
        var cleaned=string.Join(" ",(value??"").Trim().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
        return cleaned[..Math.Min(100,cleaned.Length)];
    }
    public static IQueryable<Product> Match(IQueryable<Product> query,string? value)
    {
        foreach(var term in Clean(value).ToLowerInvariant().Split(new[]{' ','-'},StringSplitOptions.RemoveEmptyEntries).Distinct().Take(8)) {
            var token=term;
            query=query.Where(p=>p.Translations.Any(t=>t.Name.ToLower().Contains(token)||(t.ShortDescription!=null&&t.ShortDescription.ToLower().Contains(token)))
                ||p.Slug.ToLower().Contains(token)||p.Category.Translations.Any(t=>t.Name.ToLower().Contains(token)));
        }
        return query;
    }
    public static IOrderedQueryable<Product> Rank(IQueryable<Product> query,string? value)
    {
        var phrase=Clean(value).ToLowerInvariant();
        if(phrase.Length==0)return query.OrderByDescending(p=>p.CreatedAt).ThenByDescending(p=>p.Id);
        return query.OrderByDescending(p=>p.Translations.Any(t=>t.Name.ToLower()==phrase))
            .ThenByDescending(p=>p.Translations.Any(t=>t.Name.ToLower().StartsWith(phrase)))
            .ThenByDescending(p=>p.Translations.Any(t=>t.Name.ToLower().Contains(phrase)))
            .ThenByDescending(p=>p.StockQuantity>0).ThenByDescending(p=>p.Id);
    }
}
