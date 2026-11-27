using System.ComponentModel.DataAnnotations;
namespace NileShop.Models;

public static class OfferKinds
{
    public const string Percent = "Percent", Fixed = "Fixed", FreeDelivery = "FreeDelivery";
    public static readonly string[] All = [Percent, Fixed, FreeDelivery];
}
public static class OfferScopes
{
    public const string All = "All", Category = "Category", Products = "Products";
    public static readonly string[] AllScopes = [All, Category, Products];
}
/// <summary>A store offer. Percent/Fixed reduce product prices (scope All, one Category, or chosen Products). FreeDelivery waives shipping from a minimum subtotal.</summary>
public class Offer
{
    public int Id { get; set; }
    [Required, MaxLength(150)] public string TitleEn { get; set; } = "";
    [Required, MaxLength(150)] public string TitleAr { get; set; } = "";
    [MaxLength(40)] public string? BadgeEn { get; set; }
    [MaxLength(40)] public string? BadgeAr { get; set; }
    [Required, MaxLength(20)] public string Kind { get; set; } = OfferKinds.Percent;
    public decimal Value { get; set; }
    [Required, MaxLength(20)] public string Scope { get; set; } = OfferScopes.All;
    public int? CategoryId { get; set; }
    /// <summary>Comma separated product ids when Scope is Products.</summary>
    [MaxLength(2000)] public string? ProductIds { get; set; }
    public decimal? MinSubtotal { get; set; }
    /// <summary>Store local time (Africa/Cairo).</summary>
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
