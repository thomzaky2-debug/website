using System.ComponentModel.DataAnnotations;
namespace NileShop.Models;
public class SavedProduct
{
    [MaxLength(450)] public string UserId { get; set; } = "";
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class ProductReview
{
    public int Id { get; set; }
    [MaxLength(450)] public string UserId { get; set; } = "";
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    [Range(1,5)] public int Rating { get; set; }
    [Required,MaxLength(100)] public string DisplayName { get; set; } = "";
    [Required,MaxLength(1000)] public string Text { get; set; } = "";
    public bool IsApproved { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public static class WalletTransactionKinds
{
    public const string OverpaidCredit = "OverpaidCredit";
    public const string UnderpaidCredit = "UnderpaidCredit";
    public const string OrderPayment = "OrderPayment";
    public const string OrderWalletRefund = "OrderWalletRefund";
}
public class WalletTransaction
{
    public int Id { get; set; }
    [Required, MaxLength(450)] public string UserId { get; set; } = "";
    public int? OrderId { get; set; }
    public Order? Order { get; set; }
    public decimal Amount { get; set; }
    [Required, MaxLength(40)] public string Kind { get; set; } = "";
    [MaxLength(120)] public string? ReferenceKey { get; set; }
    [MaxLength(300)] public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
