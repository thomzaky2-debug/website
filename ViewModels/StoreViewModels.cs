using System.ComponentModel.DataAnnotations;
using NileShop.Models;
using NileShop.Services;
namespace NileShop.ViewModels;
public record ProductCard(int Id, string Slug, string Name, string Category, string ImageUrl, decimal Price, decimal? OldPrice, int StockQuantity, bool IsFeatured, bool HasOptions = false, bool IsNew = false);
public record CategoryLink(int Id, string Slug, string Name);
public record CategoryProductGroup(CategoryLink Category, int Count, IReadOnlyList<ProductCard> Products);
public class CatalogViewModel
{
    public IReadOnlyList<CategoryProductGroup> CategoryGroups { get; set; } = [];
    public bool GroupByCategory { get; set; }
    public int TotalMatches { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string Sort { get; set; } = "relevance";
    public bool InStock { get; set; }
    public IReadOnlyList<ProductCard> Products { get; set; } = [];
    public IReadOnlyList<ProductCard> Featured { get; set; } = [];
    public IReadOnlyList<CategoryLink> Categories { get; set; } = [];
    public string? Search { get; set; }
    public string? Category { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
}
public record ProductDetailsViewModel(ProductCard Product, string ShortDescription, string FullDescription, IReadOnlyList<ProductCard> Related, string[]? Gallery = null, string[]? Sizes = null, string[]? Colors = null, string CategorySlug = "", int ReturnDays = 14, IReadOnlyList<ShippingMethod>? Shipping = null);
public record CartLine(ProductCard Product, int Quantity, string Key = "", string? Size = null, string? Color = null) { public decimal Total => Product.Price * Quantity; }
public class CartViewModel
{
    public IReadOnlyList<CartLine> Lines { get; set; } = [];
    public decimal Total => Lines.Sum(x => x.Total);
}
public class CheckoutViewModel
{
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public string? CustomerUserId { get; set; }
    public int ShippingMethodId { get; set; }
    public IReadOnlyList<ShippingMethod> ShippingMethods { get; set; } = [];
    public decimal ShippingFee => ShippingMethods.FirstOrDefault(s => s.Id == ShippingMethodId)?.Fee ?? 0;
    public decimal TotalWithShipping => Cart.Total + ShippingFee;
    private string instaPayUsername = "";
    [Required(ErrorMessage = "InstaPay_Required"), StringLength(100), RegularExpression(InstaPayHandle.UsernamePattern, ErrorMessage = "InstaPay_Invalid")]
    public string InstaPayUsername { get => instaPayUsername; set => instaPayUsername = value?.Trim() ?? ""; }
    [Required, StringLength(150)] public string CustomerName { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string CustomerEmail { get; set; } = "";
    [Required, Phone, StringLength(30)] public string CustomerPhone { get; set; } = "";
    [Required, StringLength(1000)] public string ShippingAddress { get; set; } = "";
    [Required, StringLength(64)] public string CheckoutToken { get; set; } = "";
    public CartViewModel Cart { get; set; } = new();
}
public class ProductFormViewModel : IValidatableObject
{
    [StringLength(500)] public string? Sizes { get; set; }
    [StringLength(500)] public string? Colors { get; set; }
    [StringLength(6000)] public string? GalleryImages { get; set; }
    public List<IFormFile> Uploads { get; set; } = [];
    public bool ReplacePrimaryImage { get; set; } = true;
    public int OriginalStockQuantity { get; set; }
    public int Id { get; set; }
    [Required, StringLength(200), RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Validation_Slug")] public string Slug { get; set; } = "";
    [Range(typeof(decimal), "0.01", "99999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal Price { get; set; }
    [Range(typeof(decimal), "0.01", "99999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal? OldPrice { get; set; }
    [Range(0, 1000000)] public int StockQuantity { get; set; }
    [StringLength(500), RegularExpression(@"^/assets/[a-zA-Z0-9_./-]+\.(jpg|jpeg|png|webp|gif)$", ErrorMessage = "Validation_Image")] public string? ImageUrl { get; set; }
    [Range(1, int.MaxValue)] public int CategoryId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    [Required, StringLength(250)] public string NameEn { get; set; } = "";
    [Required, StringLength(250)] public string NameAr { get; set; } = "";
    [StringLength(500)] public string? ShortDescriptionEn { get; set; }
    [StringLength(500)] public string? ShortDescriptionAr { get; set; }
    [Required, StringLength(20000)] public string FullDescriptionEn { get; set; } = "";
    [Required, StringLength(20000)] public string FullDescriptionAr { get; set; } = "";
    public IReadOnlyList<CategoryLink> Categories { get; set; } = [];
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (decimal.Round(Price,2)!=Price || (OldPrice.HasValue && decimal.Round(OldPrice.Value,2)!=OldPrice.Value)) yield return new ValidationResult("Validation_PricePrecision", [nameof(Price),nameof(OldPrice)]);
        if (OldPrice < Price) yield return new ValidationResult("Validation_OldPrice", [nameof(OldPrice)]);
        if (ImageUrl?.Contains("..") == true) yield return new ValidationResult("Validation_Image", [nameof(ImageUrl)]);
    }
}
public class CategoryFormViewModel
{
    public int Id { get; set; }
    [Required, StringLength(150), RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Validation_Slug")] public string Slug { get; set; } = "";
    [Required, StringLength(150)] public string NameEn { get; set; } = "";
    [Required, StringLength(150)] public string NameAr { get; set; } = "";
}
public class DashboardViewModel
{
    public decimal Revenue { get; set; }
    public int TotalOrders { get; set; }
    public int ActiveProducts { get; set; }
    public int LowStock { get; set; }
    public int TotalLogs { get; set; }
    public IReadOnlyList<Product> Products { get; set; } = [];
    public IReadOnlyList<Category> Categories { get; set; } = [];
    public IReadOnlyList<Order> Orders { get; set; } = [];
    public IReadOnlyList<AuditLog> Logs { get; set; } = [];
    public int Page { get; set; }
    public int TotalPages { get; set; }
}

public class LandingViewModel
{
    public IReadOnlyList<CategoryProductGroup> Categories { get; set; } = [];
    public IReadOnlyList<ProductCard> Featured { get; set; } = [];
    public IReadOnlyList<ProductCard> Latest { get; set; } = [];
}
