using System.Xml.Linq;
using NileShop.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace NileShop.Controllers;
public class SitemapController(ApplicationDbContext db) : Controller
{
    [Route("sitemap.xml")]
    [HttpGet]
    [ResponseCache(Duration = 3600)]
    public async Task<IActionResult> Index()
    {
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var urlSet = new XElement(ns + "urlset");
        var staticPages = new[] { "/", "/shop", "/info/contact", "/info/delivery", "/info/returns" };
        foreach (var page in staticPages)
        {
            urlSet.Add(new XElement(ns + "url",
                new XElement(ns + "loc", baseUrl + page)));
        }
        var categorySlugs = await db.Categories.AsNoTracking()
            .Select(c => c.Slug)
            .ToListAsync();
        foreach (var catSlug in categorySlugs)
        {
            urlSet.Add(new XElement(ns + "url",
                new XElement(ns + "loc", baseUrl + "/shop?category=" + Uri.EscapeDataString(catSlug))));
        }
        var products = await db.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.Slug, p.CreatedAt })
            .ToListAsync();
        foreach (var p in products)
        {
            urlSet.Add(new XElement(ns + "url",
                new XElement(ns + "loc", baseUrl + "/products/" + p.Slug),
                new XElement(ns + "lastmod", p.CreatedAt.ToString("yyyy-MM-dd"))));
        }
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), urlSet);
        return Content(doc.Declaration + "\n" + doc.ToString(), "application/xml");
    }
}
