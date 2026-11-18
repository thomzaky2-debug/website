using NileShop.Data;
using NileShop.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace NileShop.Controllers;
public class InfoController(ApplicationDbContext db) : Controller
{
    [HttpGet("/contact")]
    public async Task<IActionResult> Contact() => View(await db.StoreSettings.AsNoTracking().FirstOrDefaultAsync() ?? new StoreSettings());
    [HttpGet("/delivery")]
    public async Task<IActionResult> Delivery() { ViewData["Methods"] = await db.ShippingMethods.AsNoTracking().Where(s => s.IsActive).ToListAsync(); return View(await db.StoreSettings.AsNoTracking().FirstOrDefaultAsync() ?? new StoreSettings()); }
    [HttpGet("/returns")]
    public async Task<IActionResult> Returns() => View(await db.StoreSettings.AsNoTracking().FirstOrDefaultAsync() ?? new StoreSettings());
}
