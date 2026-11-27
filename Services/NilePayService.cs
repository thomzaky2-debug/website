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
        if (currentOrder.Status == OrderStatuses.Cancelled || currentOrder.InventoryReleased || currentOrder.IsSettled) throw new NilePayException("Review_NotTerminal");
        var payable = currentOrder.PayableAmount;
        var rolledBack = false;
        var attempt = await db.PaymentAttempts.SingleOrDefaultAsync(p => p.OrderId == order.Id, ct);
        if (attempt is null) {
            if (string.IsNullOrWhiteSpace(order.InstaPaySenderHandle)) throw new NilePayException("InstaPay_Missing");
            if (payable <= 0 || payable > 10000000 || order.Items.Count is 0 or > 100) throw new NilePayException("Payment_Limit");
            object linesPayload;
            if (currentOrder.WalletAmountUsed > 0 || order.DiscountAmount > 0) {
                linesPayload = new[] {
                    new {
                        name = Limit($"NileShop order {order.OrderNumber} (after {currentOrder.WalletAmountUsed + order.DiscountAmount:0.00} EGP discounts and wallet credit)", 200),
                        quantity = 1,
                        unitPrice = payable,
                        category = "Retail products"
                    }
                };
            } else {
                var productIds = order.Items.Select(i => i.ProductId).Distinct().ToArray();
                var categories = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
                    .Include(p => p.Category).ThenInclude(c => c.Translations)
                    .ToDictionaryAsync(p => p.Id, p => p.Category.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Name ?? "Products", ct);
                var lines = order.Items.OrderBy(i => i.Id).Select(i => new {
                    name = ItemName(i), quantity = i.Quantity, unitPrice = i.UnitPrice,
                    category = Limit(categories.GetValueOrDefault(i.ProductId) ?? "Products", 100)
                }).ToList();
                if (order.ShippingFee > 0) lines.Add(new { name = "Delivery - " + (order.ShippingMethodName ?? "Shipping"), quantity = 1, unitPrice = order.ShippingFee, category = "Delivery" });
                if (lines.Count > 100 || lines.Sum(i => i.quantity * i.unitPrice) != payable) throw new NilePayException("Payment_Mismatch");
                linesPayload = lines;
            }
            var lifetimeMinutes = await db.StoreSettings.AsNoTracking().OrderBy(s => s.Id).Select(s => s.CheckoutSessionLifetimeMinutes).FirstOrDefaultAsync(ct);
            if (lifetimeMinutes <= 0) lifetimeMinutes = 15;
            var rawNote = $"Order #{order.OrderNumber}"
                + (string.IsNullOrWhiteSpace(order.ShippingMethodName) ? "" : " | " + order.ShippingMethodName)
                + (currentOrder.WalletAmountUsed > 0 ? $" | Wallet credit -{currentOrder.WalletAmountUsed:0.00} EGP" : "");
            attempt = new PaymentAttempt { OrderId = order.Id, IdempotencyKey = "bts-" + order.CheckoutToken,
                RequestJson = JsonSerializer.Serialize(new { amountEgp = payable, senderHandle = order.InstaPaySenderHandle,
                    orderNumber = order.OrderNumber,
                    orderNo = order.OrderNumber,
                    lifetimeMinutes = lifetimeMinutes,
                    expiresInMinutes = lifetimeMinutes,
                    note = rawNote[..Math.Min(1000, rawNote.Length)],
                    productCategory = "Retail products", items = linesPayload,
                    returnUrl = options.Value.PublicUrl.TrimEnd('/') + "/payment/return?token=" + order.PaymentAccessToken }) };
            db.PaymentAttempts.Add(attempt);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { await creation.RollbackAsync(ct); rolledBack = true; db.ChangeTracker.Clear(); attempt = await db.PaymentAttempts.SingleAsync(p => p.OrderId == order.Id, ct); }
        }
        if (!rolledBack) await creation.CommitAsync(ct);
        if (attempt.SessionId is null) {
            // Persisted request and key are reused after every timeout, restart, or uncertain outcome.
            var remote = await client.CreateAsync(attempt.RequestJson, attempt.IdempotencyKey, ct);
            if (string.IsNullOrWhiteSpace(remote.SessionId) || remote.SessionId.Length > 200 || remote.AmountEgp != payable || remote.Currency != "EGP" || !client.ValidCheckoutUrl(remote.CheckoutUrl) || remote.CheckoutUrl!.Length > 2000) throw new NilePayException("Payment_Retry");
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
        if (string.IsNullOrWhiteSpace(transaction.SessionId) || transaction.SessionId.Length > 200 || string.IsNullOrWhiteSpace(transaction.Status) || transaction.Status.Length > 30)
            throw new NilePayException("Payment_Mismatch");
        var wallet = new WalletService(db);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (eventId is not null && await db.PaymentEvents.AnyAsync(e => e.EventId == eventId, ct)) { await tx.CommitAsync(ct); return true; }
        var attempt = await db.PaymentAttempts.AsNoTracking().Include(p => p.Order).SingleOrDefaultAsync(p => p.SessionId == transaction.SessionId, ct);
        if (attempt is null) return false; // Receiver returns 503 until creation has saved its session mapping.
        var expectedAmount = attempt.Order.PayableAmount;
        if (transaction.AmountEgp != expectedAmount || transaction.Currency != "EGP") throw new NilePayException("Payment_Mismatch");
        if (string.IsNullOrWhiteSpace(transaction.Status) || transaction.Status.Length > 30) throw new NilePayException("Payment_Mismatch");
        if (transaction.DetectedAmountEgp.HasValue && (transaction.DetectedAmountEgp.Value <= 0 || transaction.DetectedAmountEgp.Value > 10000000m || decimal.Round(transaction.DetectedAmountEgp.Value, 2) != transaction.DetectedAmountEgp.Value))
            throw new NilePayException("Payment_Mismatch");
        if (transaction.Status == "CONFIRMED" && transaction.DetectedAmountEgp < expectedAmount)
            throw new NilePayException("Payment_Mismatch");
        if (transaction.Status == "OVERPAID" && transaction.DetectedAmountEgp.HasValue && transaction.DetectedAmountEgp <= expectedAmount)
            throw new NilePayException("Payment_Mismatch");
        if (transaction.Status == "UNDERPAID" && transaction.DetectedAmountEgp.HasValue && transaction.DetectedAmountEgp >= expectedAmount)
            throw new NilePayException("Payment_Mismatch");

        if (eventId is not null) db.PaymentEvents.Add(new PaymentEvent { EventId = eventId, PaymentAttemptId = attempt.Id });

        var ownerId = attempt.Order.CustomerUserId;
        var detected = transaction.DetectedAmountEgp;

        // 1. Overpayment for a customer with an account: complete order as Overpaid and credit remaining excess to Wallet.
        if (!string.IsNullOrWhiteSpace(ownerId) && detected.HasValue && detected.Value > expectedAmount && transaction.Status is "OVERPAID" or "CONFIRMED")
        {
            var excess = decimal.Round(detected.Value - expectedAmount, 2);
            var changed = await db.Orders.Where(o => o.Id == attempt.OrderId && o.PaidAt == null && o.PaymentStatus == "Unpaid")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.PaymentStatus, "Overpaid")
                    .SetProperty(o => o.PaidAt, DateTime.UtcNow)
                    .SetProperty(o => o.DetectedAmountEgp, detected.Value)
                    .SetProperty(o => o.NeedsPaymentReview, o => o.InventoryReleased || o.Status == OrderStatuses.Cancelled)
                    .SetProperty(o => o.Status, o => o.Status == OrderStatuses.Pending && !o.InventoryReleased ? OrderStatuses.Processing : o.Status), ct);

            // A late or conflicting event must not credit an already refunded/underpaid order.
            if (changed == 1 || (attempt.Order.PaymentStatus == "Overpaid" && attempt.Order.DetectedAmountEgp == detected.Value))
                await wallet.RecordIdempotentAsync(
                ownerId,
                excess,
                WalletTransactionKinds.OverpaidCredit,
                $"overpaid:order:{attempt.OrderId}",
                attempt.OrderId,
                $"Overpayment excess from order {attempt.Order.OrderNumber} (transferred {detected.Value:0.00} EGP, required {expectedAmount:0.00} EGP)",
                ct);
            else
                await db.Orders.Where(o => o.Id == attempt.OrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.NeedsPaymentReview, true), ct);

            if (changed == 1)
            {
                db.AuditLogs.Add(new AuditLog { Action = "PAYMENT_OVERPAID_WALLET", EntityName = "Order", Details = $"{attempt.Order.OrderNumber}: +{excess:0.00} EGP credited to wallet", UserEmail = "NilePay" });
                db.OrderUpdates.Add(new OrderUpdate { OrderId = attempt.OrderId, Status = attempt.Order.InventoryReleased ? "PaymentReview" : "Overpaid" });
                // One customer notice for this payment: the "Overpaid" one (it also confirms the payment and states the wallet credit).
                if (!await db.OrderEmails.AnyAsync(e => e.OrderId == attempt.OrderId && (e.Kind == "Paid" || e.Kind == "Overpaid"), ct))
                    db.OrderEmails.Add(new OrderEmail { OrderId = attempt.OrderId, Kind = "Overpaid" });
            }
            await db.PaymentAttempts.Where(p => p.Id == attempt.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.GatewayStatus, "OVERPAID"), ct);
        }
        // 2. Underpayment for a customer with an account: stop order as Underpaid, release stock once, and credit transferred amount to Wallet.
        else if (!string.IsNullOrWhiteSpace(ownerId) && detected.HasValue && detected.Value < expectedAmount && transaction.Status == "UNDERPAID")
        {
            var transferred = decimal.Round(detected.Value, 2);
            var trackedOrder = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == attempt.OrderId, ct);
            if (trackedOrder.PaidAt == null && trackedOrder.PaymentStatus == "Unpaid")
            {
                if (!trackedOrder.InventoryReleased)
                {
                    foreach (var item in trackedOrder.Items.OrderBy(i => i.ProductId))
                        await db.Products.Where(p => p.Id == item.ProductId)
                            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity + item.Quantity), ct);
                    trackedOrder.InventoryReleased = true;
                }
                trackedOrder.Status = OrderStatuses.Cancelled;
                trackedOrder.PaymentStatus = "Underpaid";
                trackedOrder.DetectedAmountEgp = transferred;
                trackedOrder.NeedsPaymentReview = false;

                await wallet.RecordIdempotentAsync(
                    ownerId,
                    transferred,
                    WalletTransactionKinds.UnderpaidCredit,
                    $"underpaid:order:{trackedOrder.Id}",
                    trackedOrder.Id,
                    $"Underpaid transfer from stopped order {trackedOrder.OrderNumber} (transferred {transferred:0.00} EGP, required {expectedAmount:0.00} EGP)",
                    ct);

                if (trackedOrder.WalletAmountUsed > 0)
                {
                    await wallet.RecordIdempotentAsync(
                        ownerId,
                        trackedOrder.WalletAmountUsed,
                        WalletTransactionKinds.OrderWalletRefund,
                        $"refund-wallet:order:{trackedOrder.Id}",
                        trackedOrder.Id,
                        $"Wallet balance restored from stopped underpaid order {trackedOrder.OrderNumber}",
                        ct);
                }

                db.AuditLogs.Add(new AuditLog { Action = "PAYMENT_UNDERPAID_WALLET", EntityName = "Order", Details = $"{trackedOrder.OrderNumber}: stopped as underpaid; +{transferred:0.00} EGP credited to wallet", UserEmail = "NilePay" });
                db.OrderUpdates.Add(new OrderUpdate { OrderId = trackedOrder.Id, Status = "Underpaid" });
                if (!await db.OrderEmails.AnyAsync(e => e.OrderId == trackedOrder.Id && e.Kind == "Underpaid", ct))
                    db.OrderEmails.Add(new OrderEmail { OrderId = trackedOrder.Id, Kind = "Underpaid" });
            }
            await db.PaymentAttempts.Where(p => p.Id == attempt.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.GatewayStatus, "UNDERPAID"), ct);
        }
        // 3. Exact confirmation
        else if (transaction.Status == "CONFIRMED")
        {
            var changed = await db.Orders.Where(o => o.Id == attempt.OrderId && o.PaidAt == null && o.PaymentStatus == "Unpaid")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.PaymentStatus, "Paid")
                    .SetProperty(o => o.PaidAt, DateTime.UtcNow)
                    .SetProperty(o => o.DetectedAmountEgp, detected ?? expectedAmount)
                    .SetProperty(o => o.NeedsPaymentReview, o => o.InventoryReleased || o.Status == OrderStatuses.Cancelled)
                    .SetProperty(o => o.Status, o => o.Status == OrderStatuses.Pending && !o.InventoryReleased ? OrderStatuses.Processing : o.Status), ct);
            if (changed == 1)
            {
                db.AuditLogs.Add(new AuditLog { Action = "PAYMENT_CONFIRMED", EntityName = "Order", Details = attempt.Order.OrderNumber, UserEmail = "NilePay" });
                db.OrderUpdates.Add(new OrderUpdate { OrderId = attempt.OrderId, Status = attempt.Order.InventoryReleased ? "PaymentReview" : "Paid" });
                if (!await db.OrderEmails.AnyAsync(e => e.OrderId == attempt.OrderId && e.Kind == "Paid", ct))
                    db.OrderEmails.Add(new OrderEmail { OrderId = attempt.OrderId, Kind = "Paid" });
            }
            await db.PaymentAttempts.Where(p => p.Id == attempt.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.GatewayStatus, "CONFIRMED"), ct);
        }
        else
        {
            await db.PaymentAttempts.Where(p => p.Id == attempt.Id && p.GatewayStatus != "CONFIRMED" && p.GatewayStatus != "OVERPAID")
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.GatewayStatus, transaction.Status), ct);

            if (transaction.Status is "EXPIRED" or "CANCELLED" or "FAILED" or "UNDERPAID")
            {
                var trackedOrder = await db.Orders.Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == attempt.OrderId, ct);
                if (trackedOrder is not null && trackedOrder.PaidAt == null && trackedOrder.PaymentStatus == "Unpaid")
                {
                    if (!trackedOrder.InventoryReleased)
                    {
                        foreach (var item in trackedOrder.Items.OrderBy(i => i.ProductId))
                        {
                            await db.Products.Where(p => p.Id == item.ProductId)
                                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity + item.Quantity), ct);
                        }
                        trackedOrder.InventoryReleased = true;
                    }
                    trackedOrder.Status = OrderStatuses.Cancelled;
                    if (transaction.Status == "UNDERPAID")
                    {
                        trackedOrder.PaymentStatus = "Underpaid";
                        trackedOrder.DetectedAmountEgp = detected;
                        trackedOrder.NeedsPaymentReview = true;
                    }

                    if (trackedOrder.WalletAmountUsed > 0 && !string.IsNullOrWhiteSpace(trackedOrder.CustomerUserId))
                    {
                        await wallet.RecordIdempotentAsync(
                            trackedOrder.CustomerUserId,
                            trackedOrder.WalletAmountUsed,
                            WalletTransactionKinds.OrderWalletRefund,
                            $"refund-wallet:order:{trackedOrder.Id}",
                            trackedOrder.Id,
                            $"Wallet balance restored from cancelled order {trackedOrder.OrderNumber}",
                            ct);
                    }

                    db.AuditLogs.Add(new AuditLog {
                        Action = "INVENTORY_RELEASED",
                        EntityName = "Order",
                        Details = $"{trackedOrder.OrderNumber}: Restocked {trackedOrder.Items.Sum(i => i.Quantity)} items back into stock ({transaction.Status})"
                    });
                    db.OrderUpdates.Add(new OrderUpdate { OrderId = trackedOrder.Id, Status = "Cancelled" });
                }
            }
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
    }

    public async Task<bool> ReleaseInventoryIfUnpaidAsync(int orderId, string reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var guarded = await db.Orders
            .Where(o => o.Id == orderId && !o.InventoryReleased && o.PaidAt == null && o.PaymentStatus == "Unpaid" && o.Status != OrderStatuses.Cancelled)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.InventoryReleased, true).SetProperty(o => o.Status, OrderStatuses.Cancelled), ct);
        if (guarded != 1)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        var order = await db.Orders.Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) { await tx.RollbackAsync(ct); return false; }

        foreach (var item in order.Items.OrderBy(i => i.ProductId))
        {
            await db.Products.Where(p => p.Id == item.ProductId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity + item.Quantity), ct);
        }

        if (order.WalletAmountUsed > 0 && !string.IsNullOrWhiteSpace(order.CustomerUserId))
        {
            var wallet = new WalletService(db);
            await wallet.RecordIdempotentAsync(
                order.CustomerUserId,
                order.WalletAmountUsed,
                WalletTransactionKinds.OrderWalletRefund,
                $"refund-wallet:order:{order.Id}",
                order.Id,
                $"Wallet balance restored from cancelled/expired order {order.OrderNumber}",
                ct);
        }

        order.Updates.Add(new OrderUpdate { Status = "Cancelled" });
        db.AuditLogs.Add(new AuditLog {
            Action = "INVENTORY_RELEASED",
            EntityName = "Order",
            Details = $"{order.OrderNumber}: Restocked {order.Items.Sum(i => i.Quantity)} items back into product stock ({reason})"
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
