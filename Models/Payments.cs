using System.ComponentModel.DataAnnotations;
namespace NileShop.Models;
public class PaymentAttempt
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    [MaxLength(128)] public string IdempotencyKey { get; set; } = "";
    public string RequestJson { get; set; } = "";
    [MaxLength(200)] public string? SessionId { get; set; }
    [MaxLength(2000)] public string? CheckoutUrl { get; set; }
    [MaxLength(30)] public string GatewayStatus { get; set; } = "NOT_CREATED";
    public DateTime? LastCheckedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class PaymentEvent
{
    public int Id { get; set; }
    [MaxLength(200)] public string EventId { get; set; } = "";
    public int PaymentAttemptId { get; set; }
    public PaymentAttempt PaymentAttempt { get; set; } = null!;
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
