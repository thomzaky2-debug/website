using NileShop.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace NileShop.Controllers;
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class OrdersController(ApplicationDbContext db) : Controller
{
    [HttpGet("/orders/track")]
    public async Task<IActionResult> Track(string? token)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (string.IsNullOrWhiteSpace(token)) return View("Find");
        if (token.Length != 64) return NotFound();
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Updates).AsSplitQuery().SingleOrDefaultAsync(o => o.PaymentAccessToken == token);
        return order is null ? NotFound() : View(order);
    }
}
