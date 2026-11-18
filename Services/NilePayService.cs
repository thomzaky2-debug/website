using System.Data;
using System.Text.Json;
using NileShop.Data;
using NileShop.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace NileShop.Services;
public class NilePayService(ApplicationDbContext db, NilePayClient client, IOptions<NilePayOptions> options)
{
    public async Task<PaymentAttempt> StartAsync(Order order, CancellationToken ct)
    {
        await using var creation = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var currentOrder = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id, ct);
        if (currentOrder.Status == OrderStatuses.Cancelled || currentOrder.InventoryReleased) throw new NilePayException("Review_NotTerminal");
        var rolledBack = false;
        var attempt = await db.PaymentAttempts.SingleOrDefaultAsync(p => p.OrderId == order.Id, ct);
        if (attempt is null) {
            if (string.IsNullOrWhiteSpace(order.InstaPaySenderHandle)) throw new NilePayException("InstaPay_Missing");
            if (order.TotalAmount <= 0 || order.TotalAmount > 10000000 || order.Items.Count is 0 or > 100) throw new NilePayException("Payment_Limit");
            var productIds = order.Items.Select(i => i.ProductId).Distinct().ToArray();
            var categories = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
                .Include(p => p.Category).ThenInclude(c => c.Translations)
                .ToDictionaryAsync(p => p.Id, p => p.Category.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Name ?? "Products", ct);
            var lines = order.Items.OrderBy(i => i.Id).Select(i => new {
                name = ItemName(i), quantity = i.Quantity, unitPrice = i.UnitPrice,
                category = Limit(categories.GetValueOrDefault(i.ProductId) ?? "Products", 100)
            }).ToList();
            if (order.ShippingFee > 0) lines.Add(new { name = "Delivery - " + (order.ShippingMethodName ?? "Shipping"), quantity = 1, unitPrice = order.ShippingFee, category = "Delivery" });
            if (lines.Count > 100 || lines.Sum(i => i.quantity * i.unitPrice) != order.TotalAmount) throw new NilePayException("Payment_Mismatch");
            attempt = new PaymentAttempt { OrderId = order.Id, IdempotencyKey = "bts-" + order.CheckoutToken,
                RequestJson = JsonSerializer.Serialize(new { amountEgp = order.TotalAmount, senderHandle = order.InstaPaySenderHandle,
                    note = ("NileShop order " + order.OrderNumber + (string.IsNullOrWhiteSpace(order.ShippingMethodName) ? "" : " | " + order.ShippingMethodName))[..Math.Min(1000, ("NileShop order " + order.OrderNumber + (string.IsNullOrWhiteSpace(order.ShippingMethodName) ? "" : " | " + order.ShippingMethodName)).Length)],
                    productCategory = "Retail products", items = lines,
                    returnUrl = options.Value.PublicUrl.TrimEnd('/') + "/payment/return?token=" + order.PaymentAccessToken }) };
            db.PaymentAttempts.Add(attempt);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { await creation.RollbackAsync(ct); rolledBack = true; db.ChangeTracker.Clear(); attempt = await db.PaymentAttempts.SingleAsync(p => p.OrderId == order.Id, ct); }
        }
        if (!rolledBack) await creation.CommitAsync(ct);
        if (attempt.SessionId is null) {
            // Persisted request and key are reused after every timeout, restart, or uncertain outcome.
            var remote = await client.CreateAsync(attempt.RequestJson, attempt.IdempotencyKey, ct);
            if (string.IsNullOrWhiteSpace(remote.SessionId) || remote.SessionId.Length > 200 || remote.AmountEgp != order.TotalAmount || remote.Currency != "EGP" || !client.ValidCheckoutUrl(remote.CheckoutUrl) || remote.CheckoutUrl!.Length > 2000) throw new NilePayException("Payment_Retry");
            attempt.SessionId = remote.SessionId; attempt.CheckoutUrl = remote.CheckoutUrl;
            await db.SaveChangesAsync(ct);
        }
        return attempt;
    }
    static string Limit(string value, int length) => value[..Math.Min(length,value.Length)];
    static string ItemName(OrderItem item)
    {
        var details = new[] { item.SelectedSize, item.SelectedColor }.Where(x => !string.IsNullOrWhiteSpace(x));
        var name = item.ProductName.Trim() + (details.Any() ? " (" + string.Join(", ", details) + ")" : "");
        return name[..Math.Min(200, name.Length)];
    }
    public async Task ReconcileAsync(int attemptId, CancellationToken ct)
    {
        var attempt = await db.PaymentAttempts.AsNoTracking().SingleAsync(p => p.Id == attemptId, ct);
        if (attempt.SessionId is null) return;
        var now = DateTime.UtcNow;
        var claimed = await db.PaymentAttempts.Where(p => p.Id == attemptId && (p.LastCheckedAt == null || p.LastCheckedAt < now.AddSeconds(-10)))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastCheckedAt, now), ct);
        if (claimed == 0) return;
        await ApplyAsync(await client.StatusAsync(attempt.SessionId, ct), null, ct);
    }
    public async Task<bool> ApplyAsync(NileCheckout transaction, string? eventId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (eventId is not null && await db.PaymentEvents.AnyAsync(e => e.EventId == eventId, ct)) { await tx.CommitAsync(ct); return true; }
        var attempt = await db.PaymentAttempts.AsNoTracking().Include(p => p.Order).SingleOrDefaultAsync(p => p.SessionId == transaction.SessionId, ct);
        if (attempt is null) return false; // Receiver returns 503 until creation has saved its session mapping.
        if (transaction.AmountEgp != attempt.Order.TotalAmount || transaction.Currency != "EGP") throw new NilePayException("Payment_Mismatch");
        if (string.IsNullOrWhiteSpace(transaction.Status) || transaction.Status.Length > 30) throw new NilePayException("Payment_Mismatch");
        if (eventId is not null) db.PaymentEvents.Add(new PaymentEvent { EventId = eventId, PaymentAttemptId = attempt.Id });
        if (transaction.Status == "CONFIRMED") {
            var changed = await db.Orders.Where(o => o.Id == attempt.OrderId && o.PaidAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentStatus, "Paid").SetProperty(o => o.PaidAt, DateTime.UtcNow)
                    .SetProperty(o => o.NeedsPaymentReview, o => o.InventoryReleased || o.Status == OrderStatuses.Cancelled)
                    .SetProperty(o => o.Status, o => o.Status == OrderStatuses.Pending && !o.InventoryReleased ? OrderStatuses.Processing : o.Status), ct);
            if (changed == 1) {
                db.AuditLogs.Add(new AuditLog { Action = "PAYMENT_CONFIRMED", EntityName = "Order", Details = attempt.Order.OrderNumber, UserEmail = "NilePay" });
                db.OrderUpdates.Add(new OrderUpdate { OrderId = attempt.OrderId, Status = attempt.Order.InventoryReleased ? "PaymentReview" : "Paid" });
                db.OrderEmails.Add(new OrderEmail { OrderId = attempt.OrderId, Kind = "Paid" });
            }
            await db.PaymentAttempts.Where(p => p.Id == attempt.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.GatewayStatus, "CONFIRMED"), ct);
        } else {
            await db.PaymentAttempts.Where(p => p.Id == attempt.Id && p.GatewayStatus != "CONFIRMED").ExecuteUpdateAsync(s => s.SetProperty(p => p.GatewayStatus, transaction.Status), ct);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
    }
}
