using NileShop.Data;
using NileShop.Models;
using Microsoft.EntityFrameworkCore;
namespace NileShop.Services;

public static class OrderStatusPresentation
{
    public static string Key(Order order)
    {
        if (order.NeedsPaymentReview) return "Payment_Review";
        if (order.PaymentStatus is "Refunded" or "Underpaid") return order.PaymentStatus;
        if (order.IsSettled) return order.Status;
        if (order.Status == OrderStatuses.Cancelled && order.PaymentSessionStatus is not ("EXPIRED" or "CANCELLED" or "FAILED")) return order.Status;
        if (order.Status != OrderStatuses.Pending && order.Status != OrderStatuses.Cancelled) return order.Status;
        return order.PaymentSessionStatus switch {
            "EXPIRED" => "Order_Payment_Expired",
            "CANCELLED" => "Order_Payment_Cancelled",
            "FAILED" => "Order_Payment_Failed",
            "UNDERPAID" => "Underpaid",
            "OVERPAID" => "Overpaid",
            null or "NOT_CREATED" => "Order_Checkout_Incomplete",
            _ => "Order_Awaiting_Payment"
        };
    }
    public static bool IsWarning(Order order) => !order.IsSettled || order.NeedsPaymentReview || order.Status == OrderStatuses.Cancelled;

    public static async Task LoadAsync(ApplicationDbContext db, IEnumerable<Order> orders)
    {
        var list = orders.ToList();
        if (list.Count == 0) return;
        var ids = list.Select(order => order.Id).ToArray();
        var states = await db.PaymentAttempts.AsNoTracking().Where(attempt => ids.Contains(attempt.OrderId))
            .Select(attempt => new { attempt.OrderId, State = attempt.SessionId == null ? "NOT_CREATED" : attempt.GatewayStatus == "NOT_CREATED" ? "PENDING" : attempt.GatewayStatus })
            .ToDictionaryAsync(attempt => attempt.OrderId, attempt => attempt.State);
        foreach (var order in list) order.PaymentSessionStatus = states.GetValueOrDefault(order.Id);
    }

    public static IQueryable<Order> Filter(ApplicationDbContext db, IQueryable<Order> orders, string status)
    {
        var pending = orders.Where(order => order.Status == OrderStatuses.Pending && order.PaymentStatus == "Unpaid" && !order.NeedsPaymentReview);
        if (status == "Incomplete") return pending.Where(order => !db.PaymentAttempts.Any(attempt => attempt.OrderId == order.Id && attempt.SessionId != null));
        if (status == "AwaitingPayment") return pending.Where(order => db.PaymentAttempts.Any(attempt => attempt.OrderId == order.Id && attempt.SessionId != null && attempt.GatewayStatus != "EXPIRED" && attempt.GatewayStatus != "CANCELLED" && attempt.GatewayStatus != "FAILED" && attempt.GatewayStatus != "UNDERPAID" && attempt.GatewayStatus != "OVERPAID"));
        var gateway = status switch { "Expired" => "EXPIRED", "PaymentCancelled" => "CANCELLED", "PaymentFailed" => "FAILED", _ => null };
        var unpaidTerminalEligible = orders.Where(order => (order.Status == OrderStatuses.Pending || order.Status == OrderStatuses.Cancelled) && order.PaymentStatus == "Unpaid" && !order.NeedsPaymentReview);
        return gateway == null ? orders : unpaidTerminalEligible.Where(order => db.PaymentAttempts.Any(attempt => attempt.OrderId == order.Id && attempt.SessionId != null && attempt.GatewayStatus == gateway));
    }
}
