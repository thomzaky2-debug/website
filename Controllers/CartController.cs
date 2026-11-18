using System.Security.Cryptography;
using NileShop.Data;
using NileShop.Services;
using NileShop.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
namespace NileShop.Controllers;
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class CartController(CartService cart, ApplicationDbContext db, CheckoutService checkout, NilePayClient nilePay, IStringLocalizer<SharedResource> localizer, UserManager<IdentityUser> users) : Controller
{
    public async Task<IActionResult> Index() => View(await cart.ViewAsync());
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int productId, int quantity = 1, string? size = null, string? color = null, string? returnUrl = null)
    {
        var p = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId && p.IsActive);
        var items = cart.Read(); var selection = new CartSelection(productId, quantity, size, color);
        if (p is null || quantity is < 1 or > 99 || items.Values.Where(x => x.ProductId == productId).Sum(x => x.Quantity) + quantity > Math.Min(99, p.StockQuantity) || (items.Count >= 50 && !items.ContainsKey(selection.Key)))
            TempData["Error"] = "Error_StockChanged";
        else if (!ProductOptions.Valid(p.Sizes, size) || !ProductOptions.Valid(p.Colors, color)) {
            TempData["Error"] = "Options_Choose"; return RedirectToAction("Details", "Products", new { slug = p.Slug });
        } else { items[selection.Key] = selection with { Quantity = items.GetValueOrDefault(selection.Key)?.Quantity + quantity ?? quantity }; cart.Save(items); TempData["Success"] = "Cart_Added"; }
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Action(nameof(Index))!);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateQuantity(string cartKey, int quantity)
    {
        if (!ModelState.IsValid) return BadRequest();
        var items = cart.Read(); if (cartKey is null || !items.TryGetValue(cartKey, out var item)) return RedirectToAction(nameof(Index));
        var stock = await db.Products.Where(p => p.Id == item.ProductId && p.IsActive).Select(p => (int?)p.StockQuantity).SingleOrDefaultAsync();
        var other = items.Values.Where(x => x.ProductId == item.ProductId && x.Key != cartKey).Sum(x => x.Quantity);
        if (quantity == 0) items.Remove(cartKey);
        else if (quantity is >= 1 and <= 99 && other + quantity <= Math.Min(stock ?? 0, 99)) items[cartKey] = item with { Quantity = quantity };
        else TempData["Error"] = "Error_StockChanged";
        cart.Save(items);
        if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)) {
            var view = await cart.ViewAsync();
            var error = TempData["Error"] as string;
            return Json(new { total=view.Total, count=view.Lines.Sum(l=>l.Quantity), error=error is null ? null : localizer[error].Value,
                lines=view.Lines.Select(l=>new {key=l.Key,quantity=l.Quantity,total=l.Total,unitPrice=l.Product.Price}) });
        }
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Remove(string cartKey) { var items = cart.Read(); if (cartKey is not null) items.Remove(cartKey); cart.Save(items); return RedirectToAction(nameof(Index)); }
    [HttpGet]
    public async Task<IActionResult> Checkout()
    {
        var view = await cart.ViewAsync(); if (view.Lines.Count == 0) return RedirectToAction(nameof(Index));
        var token = HttpContext.Session.GetString("CheckoutToken") ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        HttpContext.Session.SetString("CheckoutToken", token);
        var methods = await db.ShippingMethods.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Id).ToListAsync();
        var model=new CheckoutViewModel { Cart = view, CheckoutToken = token, ShippingMethods = methods, ShippingMethodId = methods.FirstOrDefault()?.Id ?? 0 };
        await FillSavedContact(model, prefillAddress: true);return View(model);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(CheckoutViewModel model)
    {
        if (model.CheckoutToken != HttpContext.Session.GetString("CheckoutToken") && model.CheckoutToken != HttpContext.Session.GetString("LastCheckoutToken")) return BadRequest();
        if (model.CheckoutToken == HttpContext.Session.GetString("LastCheckoutToken") && model.CheckoutToken != HttpContext.Session.GetString("CheckoutToken")) {
            var existing = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.CheckoutToken == model.CheckoutToken);
            if (existing is null || existing.InstaPaySenderHandle != InstaPayHandle.FromUsername(model.InstaPayUsername)) return BadRequest();
            return RedirectToAction("Return", "Payment", new { token = existing.PaymentAccessToken });
        }
        model.CustomerUserId=users.GetUserId(User);
        await FillSavedContact(model, prefillAddress: false);
        model.Cart = await cart.ViewAsync();
        model.ShippingMethods = await db.ShippingMethods.AsNoTracking().Where(s => s.IsActive).ToListAsync();
        if (!nilePay.Ready) ModelState.AddModelError("", localizer["Payment_Unavailable"]);
        if (!ModelState.IsValid) return View(model);
        try {
            var order = await checkout.PlaceSelectionsAsync(model, cart.Read().Values.ToList());
            if (model.SaveAddressToProfile && !string.IsNullOrWhiteSpace(model.ShippingAddress)) {
                var user = await users.GetUserAsync(User);
                if (user is not null) {
                    var oldAddr = (await users.GetClaimsAsync(user)).Where(c => c.Type == CustomerProfileViewModel.AddressClaim).ToArray();
                    if (oldAddr.Length > 0) await users.RemoveClaimsAsync(user, oldAddr);
                    await users.AddClaimAsync(user, new System.Security.Claims.Claim(CustomerProfileViewModel.AddressClaim, model.ShippingAddress.Trim()));
                }
            }
            cart.Clear();
            HttpContext.Session.SetString("LastOrder", order.OrderNumber);
            HttpContext.Session.SetString("LastCheckoutToken", model.CheckoutToken);
            HttpContext.Session.Remove("CheckoutToken");
            return RedirectToAction("Return", "Payment", new { token = order.PaymentAccessToken });
        } catch (CheckoutException ex) { ModelState.AddModelError("", localizer[ex.Message]); return View(model); }
    }
    async Task FillSavedContact(CheckoutViewModel model, bool prefillAddress = false)
    {
        var user=await users.GetUserAsync(User);if(user is null)return;
        var claims=await users.GetClaimsAsync(user);
        var name=claims.FirstOrDefault(c=>c.Type==CustomerProfileViewModel.FullNameClaim)?.Value;
        var savedAddress=claims.FirstOrDefault(c=>c.Type==CustomerProfileViewModel.AddressClaim)?.Value;
        if(!string.IsNullOrWhiteSpace(savedAddress)){
            ViewData["SavedAddress"]=savedAddress;
            if(prefillAddress && string.IsNullOrWhiteSpace(model.ShippingAddress))model.ShippingAddress=savedAddress;
        }
        if(string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(user.PhoneNumber)||string.IsNullOrWhiteSpace(user.Email))return;
        model.CustomerName=name;model.CustomerPhone=user.PhoneNumber;model.CustomerEmail=user.Email;
        foreach(var key in new[]{nameof(model.CustomerName),nameof(model.CustomerPhone),nameof(model.CustomerEmail)})ModelState.Remove(key);
        ViewData["SavedContact"]=true;
    }
    public async Task<IActionResult> Confirmation()
    {
        var number = HttpContext.Session.GetString("LastOrder");
        if (number is null) return RedirectToAction(nameof(Index));
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleOrDefaultAsync(o => o.OrderNumber == number);
        return order is null ? NotFound() : RedirectToAction("Return", "Payment", new { token = order.PaymentAccessToken });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAjax(int productId, int quantity = 1, string? size = null, string? color = null, string? selectedSize = null, string? selectedColor = null)
    {
        size ??= selectedSize;
        color ??= selectedColor;
        var p = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId && p.IsActive);
        if (p is null)
            return BadRequest(new { success = false, message = localizer["Error_StockChanged"].Value });
        var items = cart.Read();
        var selection = new CartSelection(productId, quantity, size, color);
        if (quantity is < 1 or > 99
            || items.Values.Where(x => x.ProductId == productId).Sum(x => x.Quantity) + quantity > Math.Min(99, p.StockQuantity)
            || (items.Count >= 50 && !items.ContainsKey(selection.Key)))
            return BadRequest(new { success = false, message = localizer["Error_StockChanged"].Value });
        if (!ProductOptions.Valid(p.Sizes, size) || !ProductOptions.Valid(p.Colors, color))
            return BadRequest(new { success = false, message = localizer["Options_Choose"].Value });
        items[selection.Key] = selection with { Quantity = items.GetValueOrDefault(selection.Key)?.Quantity + quantity ?? quantity };
        cart.Save(items);
        var cartCount = items.Values.Sum(x => x.Quantity);
        return Json(new { success = true, cartCount, message = localizer["Cart_Added"].Value });
    }
    [HttpGet]
    public async Task<IActionResult> MiniCart()
    {
        var items = cart.Read();
        if (items.Count == 0)
            return Json(new { items = Array.Empty<object>(), count = 0, total = 0m });
        var productIds = items.Values.Select(x => x.ProductId).Distinct().ToArray();
        var products = await db.Products.AsNoTracking()
            .Include(p => p.Translations).Include(p => p.Category).ThenInclude(c => c.Translations)
            .Where(p => productIds.Contains(p.Id) && p.IsActive).ToListAsync();
        var cartItems = items.Values
            .Where(x => products.Any(p => p.Id == x.ProductId))
            .Select(x =>
            {
                var prod = products.Single(p => p.Id == x.ProductId);
                return new
                {
                    name = CatalogService.Name(prod),
                    image = prod.ImageUrl ?? "/assets/img/product-placeholder.svg",
                    price = prod.Price,
                    quantity = x.Quantity,
                    size = x.Size,
                    color = x.Color,
                    lineTotal = prod.Price * x.Quantity
                };
            }).ToList();
        return Json(new
        {
            items = cartItems,
            count = cartItems.Sum(x => x.quantity),
            total = cartItems.Sum(x => x.lineTotal)
        });
    }
}
