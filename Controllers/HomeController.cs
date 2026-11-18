using NileShop.Data;
using NileShop.Services;
using NileShop.ViewModels;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace NileShop.Controllers;
public class HomeController(CatalogService catalog, ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        // Preserve existing bookmarked catalog/filter URLs after the home/shop split.
        if(Request.Query.Keys.Any(key=>new[]{"q","category","page","sort","minPrice","maxPrice","inStock"}.Contains(key,StringComparer.OrdinalIgnoreCase)))return LocalRedirect("/shop"+Request.QueryString);
        var latest=await catalog.Active().OrderByDescending(p=>p.CreatedAt).ThenByDescending(p=>p.Id).Take(4).ToListAsync();
        var featured=await catalog.Active().Where(p=>p.IsFeatured).OrderByDescending(p=>p.Id).Take(4).ToListAsync();
        var categories=await catalog.CategoriesAsync();
        var categoryGroups=await catalog.CategoryGroupsAsync(categories);
        var allProductIds = (featured.Count > 0 ? featured : latest).Concat(latest).Select(p => p.Id)
            .Concat(categoryGroups.SelectMany(g => g.Products).Select(p => p.Id)).Distinct();
        ViewBag.Ratings = await catalog.GetProductRatingsAsync(db, allProductIds);
        ViewBag.BestsellerIds = await catalog.GetBestsellerIdsAsync(db);
        return View(new LandingViewModel {Categories=categoryGroups,Featured=(featured.Count>0?featured:latest).Select(CatalogService.Card).ToList(),Latest=latest.Select(CatalogService.Card).ToList()});
    }
    [HttpGet("/shop")]
    public async Task<IActionResult> Shop(string? q, string? category, int page = 1, decimal? minPrice = null, decimal? maxPrice = null, string sort = "relevance", bool inStock = false)
    {
        page = Math.Clamp(page, 1, 100000);
        q = CatalogSearch.Clean(q);
        if(sort is not ("relevance" or "latest" or "price-low" or "price-high"))sort="relevance";
        var query = catalog.Active();
        query=CatalogSearch.Match(query,q);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(p => p.Category.Slug == category);
        minPrice = minPrice.HasValue ? Math.Max(0, minPrice.Value) : null;
        maxPrice = maxPrice.HasValue ? Math.Max(0, maxPrice.Value) : null;
        if(minPrice.HasValue && maxPrice.HasValue && minPrice>maxPrice)(minPrice,maxPrice)=(maxPrice,minPrice);
        if (minPrice.HasValue) query = query.Where(p => p.Price >= minPrice);
        if (maxPrice.HasValue) query = query.Where(p => p.Price <= maxPrice);
        if (inStock) query = query.Where(p => p.StockQuantity > 0);
        var count = await query.CountAsync();
        var ordered = sort switch { "relevance" => CatalogSearch.Rank(query,q), "price-low" => query.OrderBy(p => p.Price).ThenBy(p => p.Id), "price-high" => query.OrderByDescending(p => p.Price).ThenBy(p => p.Id), _ => query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id) };
        var products = await ordered.Skip((page - 1) * 12).Take(12).ToListAsync();
        var categories=await catalog.CategoriesAsync();
        var grouped=string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(category) && !minPrice.HasValue && !maxPrice.HasValue && !inStock && (sort is "latest" or "relevance") && page==1;
        var groups=grouped ? await catalog.CategoryGroupsAsync(categories) : [];
        var shopProductIds = products.Select(p => p.Id).Concat(groups.SelectMany(g => g.Products).Select(p => p.Id)).Distinct();
        ViewBag.Ratings = await catalog.GetProductRatingsAsync(db, shopProductIds);
        ViewBag.BestsellerIds = await catalog.GetBestsellerIdsAsync(db);
        return View(new CatalogViewModel { TotalMatches=count,GroupByCategory=grouped,CategoryGroups=groups, MinPrice = minPrice, MaxPrice = maxPrice, Sort = sort, InStock = inStock, Products = products.Select(CatalogService.Card).ToList(), Categories = categories, Search = q, Category = category, Page = page, TotalPages = (int)Math.Ceiling(count / 12d) });
    }
    [HttpGet, ResponseCache(Duration=0,Location=ResponseCacheLocation.None,NoStore=true)]
    public async Task<IActionResult> Suggestions(string? q,string? category,decimal? minPrice,decimal? maxPrice,bool inStock=false,CancellationToken ct=default)
    {
        q=CatalogSearch.Clean(q);if(q.Length<2)return Json(new {products=Array.Empty<object>()});
        var query=CatalogSearch.Match(catalog.Active(),q);
        if(!string.IsNullOrWhiteSpace(category))query=query.Where(p=>p.Category.Slug==category);
        minPrice=minPrice.HasValue?Math.Max(0,minPrice.Value):null;maxPrice=maxPrice.HasValue?Math.Max(0,maxPrice.Value):null;
        if(minPrice.HasValue&&maxPrice.HasValue&&minPrice>maxPrice)(minPrice,maxPrice)=(maxPrice,minPrice);
        if(minPrice.HasValue)query=query.Where(p=>p.Price>=minPrice);
        if(maxPrice.HasValue)query=query.Where(p=>p.Price<=maxPrice);
        if(inStock)query=query.Where(p=>p.StockQuantity>0);
        var products=await CatalogSearch.Rank(query,q).Take(6).ToListAsync(ct);
        return Json(new {products=products.Select(p=>new {name=CatalogService.Name(p),price=p.Price,image=CatalogService.Card(p).ImageUrl,url=Url.Action("Details","Products",new {slug=p.Slug})})});
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
    [Route("Home/NotFound")]
    [ResponseCache(Duration = 0, NoStore = true)]
    public IActionResult PageNotFound() => View("NotFound");
}
