using System.Data;
using System.Text.Json;
using BubbleThemeShop.Data;
using BubbleThemeShop.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace BubbleThemeShop.Services;
public class NilePayService(ApplicationDbContext db, NilePayClient client, IOptions<NilePayOptions> options)
{
    public async Task<PaymentAttempt> StartAsync(Order order, CancellationToken ct)
    {
        var attempt = await db.PaymentAttempts.SingleOrDefaultAsync(p => p.OrderId == order.Id, ct);
        if (attempt is null) {
            if (order.TotalAmount <= 0 || order.TotalAmount > 10000000 || order.Items.Count is 0 or > 100) throw new NilePayException("Payment_Limit");
            attempt = new PaymentAttempt { OrderId = order.Id, IdempotencyKey = "bts-" + order.CheckoutToken,
                RequestJson = JsonSerializer.Serialize(new { amountEgp = order.TotalAmount, note = "Order " + order.OrderNumber,
                    items = order.Items.OrderBy(i => i.Id).Select(i => new { name = i.ProductName[..Math.Min(200, i.ProductName.Length)], quantity = i.Quantity, unitPrice = i.UnitPrice }),
                    returnUrl = options.Value.PublicUrl.TrimEnd('/') + "/payment/return?token=" + order.PaymentAccessToken }) };
            db.PaymentAttempts.Add(attempt);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); attempt = await db.PaymentAttempts.SingleAsync(p => p.OrderId == order.Id, ct); }
        }
        if (attempt.SessionId is null) {
            // Persisted request and key are reused after every timeout, restart, or uncertain outcome.
            var remote = await client.CreateAsync(attempt.RequestJson, attempt.IdempotencyKey, ct);
            if (string.IsNullOrWhiteSpace(remote.SessionId) || remote.SessionId.Length > 200 || remote.AmountEgp != order.TotalAmount || remote.Currency != "EGP" || !client.ValidCheckoutUrl(remote.CheckoutUrl) || remote.CheckoutUrl!.Length > 2000) throw new NilePayException("Payment_Retry");
            attempt.SessionId = remote.SessionId; attempt.CheckoutUrl = remote.CheckoutUrl;
            await db.SaveChangesAsync(ct);
        }
        return attempt;
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
            var changed = await db.Orders.Where(o => o.Id == attempt.OrderId && o.PaymentStatus != "Paid")
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentStatus, "Paid").SetProperty(o => o.PaidAt, DateTime.UtcNow)
                    .SetProperty(o => o.Status, o => o.Status == OrderStatuses.Pending ? OrderStatuses.Processing : o.Status), ct);
            if (changed == 1) db.AuditLogs.Add(new AuditLog { Action = "PAYMENT_CONFIRMED", EntityName = "Order", Details = attempt.Order.OrderNumber, UserEmail = "NilePay" });
            await db.PaymentAttempts.Where(p => p.Id == attempt.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.GatewayStatus, "CONFIRMED"), ct);
        } else {
            await db.PaymentAttempts.Where(p => p.Id == attempt.Id && p.GatewayStatus != "CONFIRMED").ExecuteUpdateAsync(s => s.SetProperty(p => p.GatewayStatus, transaction.Status), ct);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
    }
}
