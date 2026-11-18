using System.ComponentModel.DataAnnotations;
namespace NileShop.Models;
public class Category
{
    public int Id { get; set; }
    [Required, MaxLength(150)] public string Slug { get; set; } = "";
    public ICollection<CategoryTranslation> Translations { get; set; } = new List<CategoryTranslation>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
public class CategoryTranslation
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    [Required, MaxLength(5)] public string LanguageCode { get; set; } = "en";
    [Required, MaxLength(150)] public string Name { get; set; } = "";
}
public class Product
{
    [MaxLength(500)] public string? Sizes { get; set; }
    [MaxLength(500)] public string? Colors { get; set; }
    [MaxLength(6000)] public string? GalleryImages { get; set; }
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Slug { get; set; } = "";
    public decimal Price { get; set; }
    public decimal? OldPrice { get; set; }
    public int StockQuantity { get; set; }
    [MaxLength(500)] public string? ImageUrl { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<ProductTranslation> Translations { get; set; } = new List<ProductTranslation>();
}
public class ProductTranslation
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    [Required, MaxLength(5)] public string LanguageCode { get; set; } = "en";
    [Required, MaxLength(250)] public string Name { get; set; } = "";
    [MaxLength(500)] public string? ShortDescription { get; set; }
    [Required, MaxLength(20000)] public string FullDescription { get; set; } = "";
}
public static class OrderStatuses
{
    public const string Pending = "Pending", Processing = "Processing", Shipped = "Shipped", Cancelled = "Cancelled";
    public static readonly string[] All = [Pending, Processing, Shipped, Cancelled];
    public static bool CanTransition(string from, string to) => from == to || (from, to) is (Pending, Processing) or (Pending, Cancelled) or (Processing, Shipped) or (Processing, Cancelled);
}
public class Order
{
    [MaxLength(450)] public string? CustomerUserId { get; set; }
    public decimal ShippingFee { get; set; }
    public int DeliveryMinDays { get; set; }
    public int DeliveryMaxDays { get; set; }
    [MaxLength(100)] public string? ShippingMethodName { get; set; }
    [MaxLength(100)] public string? Carrier { get; set; }
    [MaxLength(100)] public string? TrackingNumber { get; set; }
    [MaxLength(800)] public string? TrackingUrl { get; set; }
    public bool InventoryReleased { get; set; }
    public bool NeedsPaymentReview { get; set; }
    [MaxLength(200)] public string? RefundReference { get; set; }
    public DateTime? RefundedAt { get; set; }
    public ICollection<OrderUpdate> Updates { get; set; } = new List<OrderUpdate>();
    [MaxLength(109)] public string? InstaPaySenderHandle { get; set; }
    [MaxLength(64)] public string PaymentAccessToken { get; set; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    [MaxLength(20)] public string PaymentStatus { get; set; } = "Unpaid";
    public DateTime? PaidAt { get; set; }
    public int Id { get; set; }
    [Required, MaxLength(40)] public string OrderNumber { get; set; } = "";
    [Required, MaxLength(64)] public string CheckoutToken { get; set; } = "";
    [Required, MaxLength(150)] public string CustomerName { get; set; } = "";
    [Required, MaxLength(254)] public string CustomerEmail { get; set; } = "";
    [Required, MaxLength(30)] public string CustomerPhone { get; set; } = "";
    [Required, MaxLength(1000)] public string ShippingAddress { get; set; } = "";
    public decimal TotalAmount { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = OrderStatuses.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
public class OrderItem
{
    [MaxLength(100)] public string? SelectedSize { get; set; }
    [MaxLength(100)] public string? SelectedColor { get; set; }
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    [Required, MaxLength(250)] public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
public class AuditLog
{
    public int Id { get; set; }
    [MaxLength(100)] public string? UserEmail { get; set; }
    [Required, MaxLength(50)] public string Action { get; set; } = "";
    [MaxLength(100)] public string? EntityName { get; set; }
    [MaxLength(500)] public string? Details { get; set; }
    [MaxLength(45)] public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
