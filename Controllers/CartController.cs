using System.Security.Cryptography;
using BubbleThemeShop.Data;
using BubbleThemeShop.Services;
using BubbleThemeShop.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
namespace BubbleThemeShop.Controllers;
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class CartController(CartService cart, ApplicationDbContext db, CheckoutService checkout, NilePayClient nilePay, IStringLocalizer<SharedResource> localizer) : Controller
{
    public async Task<IActionResult> Index() => View(await cart.ViewAsync());
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int productId, int quantity = 1, string? returnUrl = null)
    {
        var p = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId && p.IsActive);
        var items = cart.Read();
        if (p is null || quantity is < 1 or > 99 || items.GetValueOrDefault(productId) + quantity > Math.Min(99, p.StockQuantity) || (!items.ContainsKey(productId) && items.Count >= 50))
            TempData["Error"] = "Error_StockChanged";
        else { items[productId] = items.GetValueOrDefault(productId) + quantity; cart.Save(items); TempData["Success"] = "Cart_Added"; }
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Action(nameof(Index))!);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateQuantity(int productId, int quantity)
    {
        var items = cart.Read();
        if (!items.ContainsKey(productId)) return RedirectToAction(nameof(Index));
        var stock = await db.Products.Where(p => p.Id == productId && p.IsActive).Select(p => (int?)p.StockQuantity).SingleOrDefaultAsync();
        if (quantity == 0) items.Remove(productId);
        else if (quantity is >= 1 and <= 99 && stock >= quantity) items[productId] = quantity;
        else TempData["Error"] = "Error_StockChanged";
        cart.Save(items); return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Remove(int productId) { var items = cart.Read(); items.Remove(productId); cart.Save(items); return RedirectToAction(nameof(Index)); }
    [HttpGet]
    public async Task<IActionResult> Checkout()
    {
        var view = await cart.ViewAsync(); if (view.Lines.Count == 0) return RedirectToAction(nameof(Index));
        var token = HttpContext.Session.GetString("CheckoutToken") ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        HttpContext.Session.SetString("CheckoutToken", token);
        return View(new CheckoutViewModel { Cart = view, CheckoutToken = token });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(CheckoutViewModel model)
    {
        if (model.CheckoutToken != HttpContext.Session.GetString("CheckoutToken")) return BadRequest();
        model.Cart = await cart.ViewAsync();
        if (!nilePay.Ready) ModelState.AddModelError("", localizer["Payment_Unavailable"]);
        if (!ModelState.IsValid) return View(model);
        try {
            var order = await checkout.PlaceAsync(model, cart.Read());
            cart.Clear();
            HttpContext.Session.SetString("LastOrder", order.OrderNumber);
            HttpContext.Session.Remove("CheckoutToken");
            return RedirectToAction("Return", "Payment", new { token = order.PaymentAccessToken });
        } catch (CheckoutException ex) { ModelState.AddModelError("", localizer[ex.Message]); return View(model); }
    }
    public async Task<IActionResult> Confirmation()
    {
        var number = HttpContext.Session.GetString("LastOrder");
        if (number is null) return RedirectToAction(nameof(Index));
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleOrDefaultAsync(o => o.OrderNumber == number);
        return order is null ? NotFound() : RedirectToAction("Return", "Payment", new { token = order.PaymentAccessToken });
    }
}
