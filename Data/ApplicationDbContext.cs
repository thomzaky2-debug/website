using BubbleThemeShop.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace BubbleThemeShop.Data;
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategoryTranslation> CategoryTranslations => Set<CategoryTranslation>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductTranslation> ProductTranslations => Set<ProductTranslation>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<PaymentAttempt>().HasIndex(p => p.OrderId).IsUnique();
        b.Entity<PaymentAttempt>().HasIndex(p => p.IdempotencyKey).IsUnique();
        b.Entity<PaymentAttempt>().HasIndex(p => p.SessionId).IsUnique().HasFilter("[SessionId] IS NOT NULL");
        b.Entity<PaymentAttempt>().HasOne(p => p.Order).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<PaymentEvent>().HasIndex(p => p.EventId).IsUnique();
        b.Entity<Order>().HasIndex(p => p.PaymentAccessToken);
        if (Database.IsSqlite()) {
            b.Entity<Product>().Property(x => x.Price).HasConversion<double>();
            b.Entity<Product>().Property(x => x.OldPrice).HasConversion<double>();
            b.Entity<Order>().Property(x => x.TotalAmount).HasConversion<double>();
            b.Entity<OrderItem>().Property(x => x.UnitPrice).HasConversion<double>();
        }
        b.Entity<Category>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<CategoryTranslation>().HasIndex(x => new { x.CategoryId, x.LanguageCode }).IsUnique();
        b.Entity<Product>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<Product>().HasIndex(x => new { x.IsActive, x.IsFeatured, x.CreatedAt });
        b.Entity<Product>().Property(x => x.Price).HasPrecision(18, 2);
        b.Entity<Product>().Property(x => x.OldPrice).HasPrecision(18, 2);
        b.Entity<Product>().Property(x => x.IsActive).HasDefaultValue(true);
        b.Entity<Product>().Property(x => x.IsFeatured).HasDefaultValue(false);
        b.Entity<Product>().HasOne(x => x.Category).WithMany(x => x.Products).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProductTranslation>().HasIndex(x => new { x.ProductId, x.LanguageCode }).IsUnique();
        b.Entity<ProductTranslation>().HasOne(x => x.Product).WithMany(x => x.Translations).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Order>().HasIndex(x => x.OrderNumber).IsUnique();
        b.Entity<Order>().HasIndex(x => x.CheckoutToken).IsUnique();
        b.Entity<Order>().HasIndex(x => x.CreatedAt);
        b.Entity<Order>().Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Entity<OrderItem>().Property(x => x.UnitPrice).HasPrecision(18, 2);
        b.Entity<OrderItem>().HasOne(x => x.Product).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<AuditLog>().HasIndex(x => x.Timestamp);
        b.Entity<CategoryTranslation>().ToTable(t => t.HasCheckConstraint("CK_CategoryTranslation_Language", "[LanguageCode] IN ('en','ar')"));
        b.Entity<ProductTranslation>().ToTable(t => t.HasCheckConstraint("CK_ProductTranslation_Language", "[LanguageCode] IN ('en','ar')"));
        b.Entity<Product>().ToTable(t => {
            t.HasCheckConstraint("CK_Product_Price", "[Price] >= 0 AND ([OldPrice] IS NULL OR [OldPrice] >= [Price])");
            t.HasCheckConstraint("CK_Product_Stock", "[StockQuantity] >= 0");
        });
        b.Entity<Order>().ToTable(t => t.HasCheckConstraint("CK_Order_Status", "[Status] IN ('Pending','Processing','Shipped','Cancelled')"));
        b.Entity<OrderItem>().ToTable(t => t.HasCheckConstraint("CK_OrderItem_Quantity", "[Quantity] > 0 AND [UnitPrice] >= 0"));
        foreach (var p in b.Model.GetEntityTypes().SelectMany(e => e.GetProperties()).Where(p => p.ClrType == typeof(string))) p.SetIsUnicode(true);
    }
}
