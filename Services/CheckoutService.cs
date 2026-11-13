using System.Security.Cryptography;
using BubbleThemeShop.Data;
using BubbleThemeShop.Models;
using BubbleThemeShop.ViewModels;
using Microsoft.EntityFrameworkCore;
namespace BubbleThemeShop.Services;
public class CheckoutException(string key) : Exception(key);
public class CheckoutService(ApplicationDbContext db)
{
    public async Task<Order> PlaceAsync(CheckoutViewModel customer, IReadOnlyDictionary<int, int> cart)
    {
        // The token is a server-issued session secret, unique in the database, so retries cannot place a second order.
        var existing = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.CheckoutToken == customer.CheckoutToken);
        if (existing is not null) return existing;
        if (cart.Count is 0 or > 50 || cart.Values.Any(q => q is < 1 or > 99)) throw new CheckoutException("Error_CartEmpty");
        await using var tx = await db.Database.BeginTransactionAsync();
        var order = new Order {
            OrderNumber = $"BTS-{DateTime.UtcNow:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(6))}",
            CheckoutToken = customer.CheckoutToken, CustomerName = customer.CustomerName.Trim(),
            CustomerEmail = customer.CustomerEmail.Trim(), CustomerPhone = customer.CustomerPhone.Trim(), ShippingAddress = customer.ShippingAddress.Trim()
        };
        foreach (var (id, quantity) in cart.OrderBy(x => x.Key))
        {
            var p = await db.Products.AsNoTracking().Include(p => p.Translations).SingleOrDefaultAsync(p => p.Id == id && p.IsActive);
            if (p is null) throw new CheckoutException("Error_StockChanged");
            // One guarded SQL update prevents overselling even across simultaneous IIS requests.
            var changed = await db.Products.Where(x => x.Id == id && x.IsActive && x.StockQuantity >= quantity && x.Price == p.Price)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.StockQuantity, x => x.StockQuantity - quantity));
            if (changed != 1) throw new CheckoutException("Error_StockChanged");
            order.Items.Add(new OrderItem { ProductId = id, ProductName = CatalogService.Name(p), Quantity = quantity, UnitPrice = p.Price });
        }
        order.TotalAmount = order.Items.Sum(x => x.Quantity * x.UnitPrice);
        if (order.TotalAmount <= 0 || order.TotalAmount > 10000000) throw new CheckoutException("Payment_Limit");
        db.Orders.Add(order);
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); }
        catch (DbUpdateException) {
            await tx.RollbackAsync(); db.ChangeTracker.Clear();
            var duplicate = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.CheckoutToken == customer.CheckoutToken);
            if (duplicate is not null) return duplicate;
            throw;
        }
        return order;
    }
}
