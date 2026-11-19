using NileShop.Services;
using NileShop.Data;
using System.Security.Claims;
using System.Text.Json;
using NileShop.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace NileShop.Controllers;
public class ProductsController(CatalogService catalog, ApplicationDbContext db) : Controller
{
    [HttpGet("/products/{slug}")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Details(string slug)
    {
        var p = await catalog.Active().SingleOrDefaultAsync(p => p.Slug == slug);
        if (p is null) {
            var hidden = await db.Products.AsNoTracking()
                .AnyAsync(product => product.Slug == slug && (!product.IsActive || product.Category.IsHidden));
            if (!hidden) return NotFound();
            Response.StatusCode = StatusCodes.Status404NotFound;
            ViewData["Robots"] = "noindex, nofollow";
            return View("Unavailable");
        }
        var t = p.Translations.FirstOrDefault(t => t.LanguageCode == CatalogService.Language) ?? p.Translations.FirstOrDefault(t => t.LanguageCode == "en");
        var related = await catalog.Active().Where(x => x.CategoryId == p.CategoryId && x.Id != p.Id).OrderBy(x => x.Id).Take(4).ToListAsync();
        var returnDays=await db.StoreSettings.Select(s=>(int?)s.ReturnDays).FirstOrDefaultAsync()??14;
        var shipping=await db.ShippingMethods.AsNoTracking().Where(s=>s.IsActive).OrderBy(s=>s.Fee).Take(5).ToListAsync();
        ViewData["Reviews"]=await db.ProductReviews.AsNoTracking().Where(r=>r.ProductId==p.Id&&r.IsApproved).OrderByDescending(r=>r.CreatedAt).Take(20).ToListAsync();
        var userId=User.FindFirstValue(ClaimTypes.NameIdentifier);
        ViewData["CanReview"]=userId!=null && !await db.ProductReviews.AnyAsync(r=>r.ProductId==p.Id&&r.UserId==userId) && await db.Orders.AnyAsync(o=>o.CustomerUserId==userId&&(o.PaymentStatus=="Paid"||o.PaymentStatus=="Overpaid")&&!o.NeedsPaymentReview&&o.Status=="Shipped"&&o.Items.Any(i=>i.ProductId==p.Id));
        ViewBag.Ratings = await catalog.GetProductRatingsAsync(db, related.Select(x => x.Id).Append(p.Id).Distinct());
        ViewBag.BestsellerIds = await catalog.GetBestsellerIdsAsync(db);
        if (userId != null)
        {
            ViewBag.SavedProductIds = (await db.SavedProducts
                .AsNoTracking()
                .Where(s => s.UserId == userId)
                .Select(s => s.ProductId)
                .ToListAsync()).ToHashSet();
        }
        var localizedName = t?.Name ?? p.Slug;
        var localizedDesc = t?.ShortDescription ?? "";
        if (localizedDesc.Length > 160) localizedDesc = localizedDesc[..160];
        var absoluteImage = $"{Request.Scheme}://{Request.Host}{p.ImageUrl ?? "/assets/img/product-placeholder.svg"}";
        ViewData["og:title"] = localizedName;
        ViewData["og:description"] = localizedDesc;
        ViewData["og:image"] = absoluteImage;
        ViewData["og:type"] = "product";
        ViewData["og:price"] = p.Price.ToString("F2");
        var jsonLd = JsonSerializer.Serialize(new
        {
            @context = "https://schema.org",
            @type = "Product",
            name = localizedName,
            description = localizedDesc,
            image = absoluteImage,
            offers = new
            {
                @type = "Offer",
                price = p.Price.ToString("F2"),
                priceCurrency = "EGP",
                availability = p.StockQuantity > 0 ? "https://schema.org/InStock" : "https://schema.org/OutOfStock"
            }
        });
        ViewData["JsonLd"] = jsonLd;
        return View(new ProductDetailsViewModel(CatalogService.Card(p), t?.ShortDescription ?? "", t?.FullDescription ?? "", related.Select(CatalogService.Card).ToList(), new[] { p.ImageUrl ?? "/assets/img/product-placeholder.svg" }.Concat((p.GalleryImages ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct().Take(7).ToArray(), ProductOptions.Values(p.Sizes), ProductOptions.Values(p.Colors),p.Category.Slug,returnDays,shipping));
    }
}
