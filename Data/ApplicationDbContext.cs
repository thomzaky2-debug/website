using NileShop.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace NileShop.Data;
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<ClientNotification> ClientNotifications => Set<ClientNotification>();
    public DbSet<NotificationRead> NotificationReads => Set<NotificationRead>();
    public DbSet<EmailRateLimit> EmailRateLimits => Set<EmailRateLimit>();
    public DbSet<NileShop.Models.ConfirmedPhoneOwner> ConfirmedPhoneOwners => Set<NileShop.Models.ConfirmedPhoneOwner>();
    public DbSet<AccountEmailQueue> AccountEmailQueues => Set<AccountEmailQueue>();
    public DbSet<SavedProduct> SavedProducts => Set<SavedProduct>();
    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<StoreMailSettings> StoreMailSettings => Set<StoreMailSettings>();
    public DbSet<StoreSettings> StoreSettings => Set<StoreSettings>();
    public DbSet<ShippingMethod> ShippingMethods => Set<ShippingMethod>();
    public DbSet<OrderEmail> OrderEmails => Set<OrderEmail>();
    public DbSet<OrderUpdate> OrderUpdates => Set<OrderUpdate>();
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
        b.Entity<ClientNotification>().HasIndex(n => n.SendKey).IsUnique();
        b.Entity<ClientNotification>().HasIndex(n => new { n.RecipientId, n.Id });
        b.Entity<ClientNotification>().HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(n => n.RecipientId).OnDelete(DeleteBehavior.Restrict);
        if (Database.IsSqlite())
            b.Entity<NotificationRead>().HasKey(n => new { n.NotificationId, n.UserId });
        else
        {
            // Keep the clustered/primary key below SQL Server's 900-byte limit.
            b.Entity<NotificationRead>().Property<int>("Id").ValueGeneratedOnAdd();
            b.Entity<NotificationRead>().HasKey("Id");
            b.Entity<NotificationRead>().HasIndex(n => new { n.NotificationId, n.UserId }).IsUnique();
        }
        b.Entity<NotificationRead>().HasOne(n => n.Notification).WithMany(n => n.Reads).HasForeignKey(n => n.NotificationId);
        b.Entity<NotificationRead>().HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SavedProduct>().HasKey(x=>new{x.UserId,x.ProductId});
        b.Entity<SavedProduct>().HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SavedProduct>().HasOne(x=>x.Product).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProductReview>().HasIndex(x=>new{x.UserId,x.ProductId}).IsUnique();
        b.Entity<ProductReview>().HasIndex(x=>new{x.ProductId,x.IsApproved});
        b.Entity<ProductReview>().HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProductReview>().HasOne(x=>x.Product).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<Order>().HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x=>x.CustomerUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WalletTransaction>().HasIndex(x => new { x.UserId, x.CreatedAt });
        b.Entity<WalletTransaction>().HasIndex(x => x.ReferenceKey).IsUnique().HasFilter("[ReferenceKey] IS NOT NULL");
        b.Entity<WalletTransaction>().HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WalletTransaction>().HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WalletTransaction>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<Order>().Property(x => x.WalletAmountUsed).HasPrecision(18, 2);
        b.Entity<Order>().Property(x => x.DetectedAmountEgp).HasPrecision(18, 2);
        b.Entity<ProductReview>().ToTable(t=>t.HasCheckConstraint("CK_Review_Rating","[Rating] >= 1 AND [Rating] <= 5"));
        b.Entity<ShippingMethod>().Property(x => x.Fee).HasPrecision(18,2);
        b.Entity<Order>().Property(x => x.ShippingFee).HasPrecision(18,2);
        b.Entity<OrderEmail>().HasIndex(x => new { x.OrderId, x.Kind }).IsUnique();
        b.Entity<OrderEmail>().HasIndex(x => new { x.SentAt, x.NextAttemptAt });
        b.Entity<ShippingMethod>().ToTable(t => t.HasCheckConstraint("CK_Shipping_Fee", "[Fee] >= 0 AND [MaxDays] >= [MinDays]"));
        b.Entity<PaymentAttempt>().HasIndex(p => p.OrderId).IsUnique();
        b.Entity<PaymentAttempt>().HasIndex(p => p.IdempotencyKey).IsUnique();
        b.Entity<PaymentAttempt>().HasIndex(p => p.SessionId).IsUnique().HasFilter("[SessionId] IS NOT NULL");
        b.Entity<PaymentAttempt>().HasOne(p => p.Order).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<PaymentEvent>().HasIndex(p => p.EventId).IsUnique();
        b.Entity<Order>().HasIndex(p => p.PaymentAccessToken);
        if (Database.IsSqlite()) {
            b.Entity<ShippingMethod>().Property(x => x.Fee).HasConversion<double>();
            b.Entity<Order>().Property(x => x.ShippingFee).HasConversion<double>();
            b.Entity<Product>().Property(x => x.Price).HasConversion<double>();
            b.Entity<Product>().Property(x => x.OldPrice).HasConversion<double>();
            b.Entity<Order>().Property(x => x.TotalAmount).HasConversion<double>();
            b.Entity<Order>().Property(x => x.WalletAmountUsed).HasConversion<double>();
            b.Entity<Order>().Property(x => x.DetectedAmountEgp).HasConversion<double>();
            b.Entity<WalletTransaction>().Property(x => x.Amount).HasConversion<double>();
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
        b.Entity<Order>().ToTable(t => t.HasCheckConstraint("CK_Order_Status", "[Status] IN ('Pending','Processing','Shipped','Delivered','Cancelled')"));
        b.Entity<OrderItem>().ToTable(t => t.HasCheckConstraint("CK_OrderItem_Quantity", "[Quantity] > 0 AND [UnitPrice] >= 0"));
        b.Entity<EmailRateLimit>().HasIndex(x => x.NormalizedEmail).IsUnique();
        b.Entity<NileShop.Models.ConfirmedPhoneOwner>(e => {
            e.HasKey(x => x.NormalizedPhone);
            e.Property(x => x.NormalizedPhone).HasMaxLength(20);
            e.Property(x => x.UserId).HasMaxLength(450);
        });
        b.Entity<AccountEmailQueue>().HasIndex(x => new { x.SentAtUtc, x.NextAttemptAtUtc });
        foreach (var p in b.Model.GetEntityTypes().SelectMany(e => e.GetProperties()).Where(p => p.ClrType == typeof(string))) p.SetIsUnicode(true);
    }
}
