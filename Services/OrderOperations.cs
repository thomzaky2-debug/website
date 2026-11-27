using System.Data;
using NileShop.Data;
using NileShop.Models;
using Microsoft.EntityFrameworkCore;
namespace NileShop.Services;
public class OrderOperations(ApplicationDbContext db, NilePayClient client, NilePayService payments)
{
    public async Task CancelUnpaidAsync(int id, string reason, CancellationToken ct)
    {
        var attempt = await db.PaymentAttempts.AsNoTracking().SingleOrDefaultAsync(p => p.OrderId == id, ct);
        if (attempt is not null) {
            if (attempt.SessionId is null) throw new NilePayException("Review_Unknown");
            var remote = await client.StatusAsync(attempt.SessionId, ct);
            await payments.ApplyAsync(remote, null, ct);
            if (remote.Status is not ("EXPIRED" or "CANCELLED" or "FAILED")) throw new NilePayException("Review_NotTerminal");
        }
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == id, ct);
        // Recheck mapping inside the transaction: creation may have raced with the initial read.
        var current = await db.PaymentAttempts.AsNoTracking().SingleOrDefaultAsync(p => p.OrderId == id, ct);
        if (current?.Id != attempt?.Id || (current is not null && current.GatewayStatus is not ("EXPIRED" or "CANCELLED" or "FAILED"))) throw new NilePayException("Review_Unknown");
        if (order.PaymentStatus != "Unpaid" || order.PaidAt is not null) throw new NilePayException("Review_NotTerminal");
        if (order.Status == OrderStatuses.Cancelled) { await tx.CommitAsync(ct); return; }
        await ReleaseAsync(order, ct);
        order.Status = OrderStatuses.Cancelled;
        if (order.WalletAmountUsed > 0 && !string.IsNullOrWhiteSpace(order.CustomerUserId)) {
            await new WalletService(db).RecordIdempotentAsync(
                order.CustomerUserId,
                order.WalletAmountUsed,
                WalletTransactionKinds.OrderWalletRefund,
                $"refund-wallet:order:{order.Id}",
                order.Id,
                $"Wallet balance restored from cancelled order {order.OrderNumber}",
                ct);
        }
        order.Updates.Add(new OrderUpdate { Status = "Cancelled" });
        db.OrderEmails.Add(new OrderEmail { OrderId = id, Kind = "Cancelled" });
        db.AuditLogs.Add(new AuditLog { Action = "CANCEL_RECONCILED", EntityName = "Order", Details = $"{order.OrderNumber}: {reason}" });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task RecordRefundAsync(int id, string reference, bool restock, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == id, ct);
        if (order.PaymentStatus == "Refunded") { await tx.CommitAsync(ct); return; }
        if (order.PaymentStatus is not ("Paid" or "Overpaid")) throw new NilePayException("Review_NotPaid");
        order.PaymentStatus = "Refunded"; order.RefundReference = reference; order.RefundedAt = DateTime.UtcNow;
        order.NeedsPaymentReview = false; order.Status = OrderStatuses.Cancelled;
        if (restock) await ReleaseAsync(order, ct);
        order.Updates.Add(new OrderUpdate { Status = "Refunded" });
        db.OrderEmails.Add(new OrderEmail { OrderId = id, Kind = "Refunded" });
        db.AuditLogs.Add(new AuditLog { Action = "REFUND_RECORDED", EntityName = "Order", Details = $"{order.OrderNumber}: external reference {reference}; restock {restock}" });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    /// <summary>Admin confirms the money arrived outside the gateway. Records state only; no gateway call, no money moves.</summary>
    public async Task<decimal> MarkPaidManuallyAsync(int id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var order = await db.Orders.SingleAsync(o => o.Id == id, ct);
        if (order.PaymentStatus != "Unpaid" || order.PaidAt is not null || order.PayableAmount <= 0) throw new NilePayException("Payment_Manual_Unavailable");
        var amount = order.PayableAmount;
        var late = order.InventoryReleased || order.Status == OrderStatuses.Cancelled;
        order.PaymentStatus = "Paid"; order.PaidAt = DateTime.UtcNow; order.DetectedAmountEgp = amount;
        if (late) order.NeedsPaymentReview = true;
        else {
            if (order.Status == OrderStatuses.Pending) order.Status = OrderStatuses.Processing;
            order.Updates.Add(new OrderUpdate { Status = "Paid" });
            if (!await db.OrderEmails.AnyAsync(e => e.OrderId == id && e.Kind == "Paid", ct)) db.OrderEmails.Add(new OrderEmail { OrderId = id, Kind = "Paid" });
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return amount;
    }
    public async Task<string> DismissReviewAsync(int id, CancellationToken ct)
    {
        var order = await db.Orders.SingleAsync(o => o.Id == id, ct);
        if (!order.NeedsPaymentReview) throw new NilePayException("Payment_Manual_Unavailable");
        order.NeedsPaymentReview = false; await db.SaveChangesAsync(ct);
        return order.OrderNumber;
    }
    async Task ReleaseAsync(Order order, CancellationToken ct)
    {
        if (order.InventoryReleased) return;
        foreach (var item in order.Items.OrderBy(i => i.ProductId))
            await db.Products.Where(p => p.Id == item.ProductId).ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity + item.Quantity), ct);
        order.InventoryReleased = true;
    }
}
