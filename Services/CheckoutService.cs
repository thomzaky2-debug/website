using System.Data;
using System.Security.Cryptography;
using NileShop.Data;
using NileShop.Models;
using NileShop.ViewModels;
using Microsoft.EntityFrameworkCore;
namespace NileShop.Services;
public class CheckoutException(string key) : Exception(key);
public class CheckoutService(ApplicationDbContext db, IConfiguration? config = null, IHttpContextAccessor? http = null)
{
    public Task<Order> PlaceAsync(CheckoutViewModel customer, IReadOnlyDictionary<int, int> cart) => PlaceSelectionsAsync(customer, cart.Select(x => new CartSelection(x.Key, x.Value)).ToList());
    async Task<bool> IsBlockedBuyerAsync(CheckoutViewModel customer)
    {
        var email = (customer.CustomerEmail ?? "").Trim().ToUpperInvariant();
        var userId = customer.CustomerUserId;
        if (email.Length == 0 && string.IsNullOrWhiteSpace(userId)) return false;
        var ends = await db.Users.AsNoTracking().Where(u => u.LockoutEnabled && ((userId != null && u.Id == userId) || (email != "" && u.NormalizedEmail == email)))
            .Select(u => u.LockoutEnd).ToListAsync();
        return ends.Any(AccountBlock.IsBlockedEnd);
    }
    public async Task<Order> PlaceSelectionsAsync(CheckoutViewModel customer, IReadOnlyList<CartSelection> cart)
    {
        var senderHandle = InstaPayHandle.FromUsername(customer.InstaPayUsername);
        // The token is a server-issued session secret, unique in the database, so retries cannot place a second order.
        var existing = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.CheckoutToken == customer.CheckoutToken);
        if (existing is not null) {
            if (existing.CustomerUserId != customer.CustomerUserId) throw new CheckoutException("Error");
            if (existing.PayableAmount > 0 && existing.InstaPaySenderHandle != senderHandle) throw new CheckoutException("InstaPay_Locked");
            return existing;
        }
        if (await IsBlockedBuyerAsync(customer)) throw new CheckoutException("Account_Blocked");
        if (cart.Count is 0 or > 50 || cart.Any(q => q.Quantity is < 1 or > 99)) throw new CheckoutException("Error_CartEmpty");
        var maxPerAccount = config?.GetValue("Checkout:MaxOpenUnpaidOrdersPerAccount", 3) ?? 3;
        if (maxPerAccount > 0 && !string.IsNullOrWhiteSpace(customer.CustomerUserId)) {
            var openCount = await db.Orders.AsNoTracking().CountAsync(o => o.CustomerUserId == customer.CustomerUserId && o.PaymentStatus == "Unpaid" && !o.InventoryReleased && o.Status == OrderStatuses.Pending);
            if (openCount >= maxPerAccount) throw new CheckoutException("Checkout_TooManyUnpaid");
        }
        var liveOffers = await new OfferService(db).LiveAsync();
        var clientIp = http?.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var maxPerIp = config?.GetValue("Checkout:MaxOpenUnpaidOrdersPerIp", 5) ?? 5;
        if (maxPerIp > 0 && !string.IsNullOrWhiteSpace(clientIp)) {
            var recentCutoff = DateTime.UtcNow.AddMinutes(-30);
            var ipCount = await db.AuditLogs.AsNoTracking().CountAsync(a => a.Action == "ORDER_PLACED" && a.IpAddress == clientIp && a.Timestamp >= recentCutoff);
            if (ipCount >= maxPerIp) throw new CheckoutException("Checkout_TooManyUnpaid");
        }
        var shipping = await db.ShippingMethods.AsNoTracking().SingleOrDefaultAsync(s => s.Id == customer.ShippingMethodId && s.IsActive);
        if (shipping is null && await db.ShippingMethods.AnyAsync()) throw new CheckoutException("Shipping_Choose");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var order = new Order {
            CustomerUserId = customer.CustomerUserId,
            ShippingFee = shipping?.Fee ?? 0, ShippingMethodName = shipping?.NameEn, DeliveryMinDays = shipping?.MinDays ?? 0, DeliveryMaxDays = shipping?.MaxDays ?? 0,
            OrderNumber = $"NILE-{CairoTime.Now:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(6))}",
            CheckoutToken = customer.CheckoutToken, InstaPaySenderHandle = senderHandle, CustomerName = customer.CustomerName.Trim(),
            CustomerEmail = customer.CustomerEmail.Trim(), CustomerPhone = customer.CustomerPhone.Trim(), ShippingAddress = customer.ShippingAddress.Trim()
        };
        foreach (var selection in cart.OrderBy(x => x.ProductId).ThenBy(x => x.Key))
        {
            var id = selection.ProductId; var quantity = selection.Quantity;
            var p = await db.Products.AsNoTracking().Include(p => p.Translations).SingleOrDefaultAsync(p => p.Id == id && p.IsActive && !p.Category.IsHidden);
            if (p is not null && (!ProductOptions.Valid(p.Sizes, selection.Size) || !ProductOptions.Valid(p.Colors, selection.Color))) throw new CheckoutException("Options_Choose");
            if (p is null) throw new CheckoutException("Error_StockChanged");
            // One guarded SQL update prevents overselling even across simultaneous IIS requests.
            var changed = await db.Products.Where(x => x.Id == id && x.IsActive && !x.Category.IsHidden && x.StockQuantity >= quantity && x.Price == p.Price)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.StockQuantity, x => x.StockQuantity - quantity));
            if (changed != 1) throw new CheckoutException("Error_StockChanged");
            order.Items.Add(new OrderItem { SelectedSize = selection.Size, SelectedColor = selection.Color, ProductName = CatalogService.Name(p), ProductId = id, Quantity = quantity, UnitPrice = OfferPricing.Best(p.Id, p.CategoryId, p.Price, liveOffers)?.Price ?? p.Price });
        }
        var itemsTotal = order.Items.Sum(x => x.Quantity * x.UnitPrice);
        var storeSettings = await db.StoreSettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync();
        var freeDelivery = OfferPricing.FreeDelivery(storeSettings?.FreeDeliveryOfferEnabled == true, storeSettings?.FreeDeliveryOfferThreshold ?? 0m, liveOffers);
        var qualifiesForFreeDelivery = freeDelivery.Enabled && itemsTotal >= freeDelivery.Threshold;
        order.ShippingFee = qualifiesForFreeDelivery ? 0m : (shipping?.Fee ?? 0);
        var orderDiscount = OfferPricing.OrderDiscount(itemsTotal, liveOffers);
        order.DiscountAmount = orderDiscount.Amount; order.DiscountLabel = orderDiscount.Offer?.TitleEn;
        order.TotalAmount = itemsTotal - order.DiscountAmount + order.ShippingFee;
        if (order.TotalAmount <= 0 || order.TotalAmount > 10000000) throw new CheckoutException("Payment_Limit");

        var wallet = new WalletService(db);
        decimal usableWallet = 0m;
        if (customer.UseWallet && !string.IsNullOrWhiteSpace(customer.CustomerUserId))
        {
            var balance = await wallet.GetBalanceAsync(customer.CustomerUserId);
            usableWallet = decimal.Round(Math.Min(balance, order.TotalAmount), 2);
            order.WalletAmountUsed = usableWallet;
        }

        if (order.PayableAmount > 0 && string.IsNullOrWhiteSpace(senderHandle))
            throw new CheckoutException("InstaPay_Invalid");

        if (order.PayableAmount == 0m)
        {
            order.PaymentStatus = "Paid";
            order.PaidAt = DateTime.UtcNow;
            order.Status = OrderStatuses.Processing;
            order.Updates.Add(new OrderUpdate { Status = "Paid" });
            db.OrderEmails.Add(new OrderEmail { Order = order, Kind = "Paid" });
            db.AuditLogs.Add(new AuditLog { Action = "PAYMENT_WALLET_FULL", EntityName = "Order", Details = $"{order.OrderNumber}: paid {usableWallet:0.00} EGP from wallet", UserEmail = order.CustomerEmail });
        }
        else
        {
            order.Updates.Add(new OrderUpdate { Status = "Pending" });
            db.OrderEmails.Add(new OrderEmail { Order = order, Kind = "Created" });
        }

        db.Orders.Add(order);
        try
        {
            await db.SaveChangesAsync();
            if (usableWallet > 0 && !string.IsNullOrWhiteSpace(customer.CustomerUserId))
            {
                await wallet.RecordIdempotentAsync(
                    customer.CustomerUserId,
                    -usableWallet,
                    WalletTransactionKinds.OrderPayment,
                    $"spend:order:{order.Id}",
                    order.Id,
                    $"Applied to order {order.OrderNumber}");
                await db.SaveChangesAsync();
            }
            await tx.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(); db.ChangeTracker.Clear();
            var duplicate = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.CheckoutToken == customer.CheckoutToken);
            if (duplicate is not null) {
                if (duplicate.CustomerUserId != customer.CustomerUserId) throw new CheckoutException("Error");
                if (duplicate.PayableAmount > 0 && duplicate.InstaPaySenderHandle != senderHandle) throw new CheckoutException("InstaPay_Locked");
                return duplicate;
            }
            throw;
        }
        return order;
    }
}
