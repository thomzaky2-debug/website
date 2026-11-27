using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NileShop.Data;
using NileShop.Services;
using NileShop.ViewModels;
namespace NileShop.Controllers;

public class OffersViewModel
{
    public List<NileShop.Models.Offer> Live { get; set; } = [];
    public List<ProductCard> Products { get; set; } = [];
}
public class OffersController(ApplicationDbContext db) : Controller
{
    [HttpGet("/offers")]
    public async Task<IActionResult> Index()
    {
        var live = OfferContext.Live.ToList();
        var products = await db.Products.AsNoTracking().Include(p => p.Translations).Include(p => p.Category).ThenInclude(c => c.Translations)
            .Where(p => p.IsActive && !p.Category.IsHidden).OrderByDescending(p => p.Id).Take(400).ToListAsync();
        var cards = products.Select(CatalogService.Card).Where(c => c.OfferLabel is not null).Take(60).ToList();
        return View(new OffersViewModel { Live = live, Products = cards });
    }
}
