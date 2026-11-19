using System.ComponentModel.DataAnnotations;
namespace NileShop.Models;
public class StoreSettings
{
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever, MaxLength(500)] public string? HeroImageUrl { get; set; }
    public int Id { get; set; }
    [EmailAddress, MaxLength(254)] public string? ContactEmail { get; set; }
    [Phone, MaxLength(30)] public string? ContactPhone { get; set; }
    [MaxLength(500)] public string? AddressEn { get; set; }
    [MaxLength(500)] public string? AddressAr { get; set; }
    [Range(1, 90)] public int ReturnDays { get; set; } = 14;
    [MaxLength(6000)] public string? DeliveryPolicyEn { get; set; }
    [MaxLength(6000)] public string? DeliveryPolicyAr { get; set; }
    [MaxLength(6000)] public string? ReturnsPolicyEn { get; set; }
    [MaxLength(6000)] public string? ReturnsPolicyAr { get; set; }
    [MaxLength(500)] public string? FacebookUrl { get; set; }
    [MaxLength(500)] public string? InstagramUrl { get; set; }
    [MaxLength(500)] public string? TwitterUrl { get; set; }
    [MaxLength(500)] public string? WhatsAppUrl { get; set; }
}
public class ShippingMethod
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string NameEn { get; set; } = "";
    [Required, MaxLength(100)] public string NameAr { get; set; } = "";
    [Range(typeof(decimal), "0", "10000", ParseLimitsInInvariantCulture = true)] public decimal Fee { get; set; }
    [Range(1, 60)] public int MinDays { get; set; } = 3;
    [Range(1, 60)] public int MaxDays { get; set; } = 5;
    public bool IsActive { get; set; } = true;
}
public class OrderEmail
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    [MaxLength(30)] public string Kind { get; set; } = "Created";
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    [MaxLength(100)] public string? LastError { get; set; }
}
public class OrderUpdate
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    [MaxLength(40)] public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
