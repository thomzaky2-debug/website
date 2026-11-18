using NileShop.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace NileShop.Data;
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        static void Check(IdentityResult result) { if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description))); }
        if (!await roles.RoleExistsAsync("Admin")) Check(await roles.CreateAsync(new IdentityRole("Admin")));
        var email = config["AdminSettings:Email"];
        if (string.IsNullOrWhiteSpace(email)) throw new InvalidOperationException("Configure AdminSettings:Email and AdminSettings:Password before first launch.");
        var admin = await users.FindByEmailAsync(email);
        if (admin is null) {
            var password = config["AdminSettings:Password"];
            if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("Configure a strong AdminSettings:Password before first launch.");
            admin = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            Check(await users.CreateAsync(admin, password));
        }
        if (!await users.IsInRoleAsync(admin, "Admin")) Check(await users.AddToRoleAsync(admin, "Admin"));
        if (!await db.StoreMailSettings.AnyAsync()) db.StoreMailSettings.Add(new StoreMailSettings());
        if (!await db.StoreSettings.AnyAsync()) db.StoreSettings.Add(new StoreSettings { DeliveryPolicyEn = "Delivery estimates begin after payment confirmation. Shipping fees are shown before checkout.", DeliveryPolicyAr = "تبدأ مدة التوصيل المتوقعة بعد تأكيد الدفع. تظهر رسوم التوصيل قبل إتمام الطلب.", ReturnsPolicyEn = "Request a return within 14 days of delivery. Contact NileShop with your order number and item details; eligibility is reviewed before approval.", ReturnsPolicyAr = "اطلب الاسترجاع خلال ١٤ يومًا من التوصيل. تواصل مع نايل شوب برقم الطلب وتفاصيل المنتج، ثم تتم مراجعة الأهلية قبل الموافقة." });
        if (!await db.ShippingMethods.AnyAsync()) db.ShippingMethods.AddRange(
            new ShippingMethod { NameEn = "Cairo & Giza", NameAr = "القاهرة والجيزة", Fee = 50, MinDays = 3, MaxDays = 5 },
            new ShippingMethod { NameEn = "Other governorates", NameAr = "المحافظات الأخرى", Fee = 80, MinDays = 5, MaxDays = 7 });
        await db.SaveChangesAsync();
        if (await db.Categories.AnyAsync()) return;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var shoes = Category("shoes", "Shoes", "أحذية");
        var women = Category("women", "Women's fashion", "أزياء نسائية");
        var men = Category("men", "Men's fashion", "أزياء رجالية");
        var home = Category("home-living", "Home & living", "المنزل والمعيشة");
        var tech = Category("electronics", "Electronics", "إلكترونيات");
        db.Categories.AddRange(shoes, women, men, home, tech);
        db.Products.AddRange(
            Product(shoes, "everyday-runner", "Everyday knit runner", "حذاء رياضي يومي", "shoe1.jpg", 1299, 1599, true),
            Product(women, "soft-knit-cardigan", "Soft knit cardigan", "كارديجان ناعم", "apparel4.jpg", 899, 1099, true),
            Product(men, "classic-cotton-shirt", "Classic cotton shirt", "قميص قطني كلاسيكي", "bajulakilaki.jpg", 649, null, true),
            Product(tech, "gaming-headphones", "Studio gaming headphones", "سماعات ألعاب ستوديو", "headphonegaming.jpg", 1499, 1799, true),
            Product(shoes, "street-sneakers", "Everyday street sneakers", "حذاء سنيكرز عصري", "shoe2.jpg", 1199, null, false),
            Product(women, "essential-hoodie", "The essential hoodie", "هودي أساسي", "hoodiewanita.jpg", 799, 999, false),
            Product(women, "city-shoulder-bag", "City shoulder bag", "حقيبة كتف للمدينة", "tas23.jpg", 999, null, false),
            Product(home, "living-room-collection", "Modern living collection", "مجموعة المنزل العصرية", "home3.jpg", 1899, 2299, false),
            Product(men, "tailored-jacket", "The tailored jacket", "جاكيت بتصميم أنيق", "jas345.jpg", 2199, null, false),
            Product(women, "everyday-trousers", "Relaxed everyday trousers", "بنطال يومي مريح", "celanawanita.jpg", 699, null, false),
            Product(shoes, "weekend-trainers", "Weekend trainers", "حذاء رياضي لعطلة الأسبوع", "shoe3.jpg", 1099, 1299, false),
            Product(home, "home-essentials", "Thoughtful home essentials", "أساسيات منزلية مختارة", "home5.jpg", 599, null, false));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
    static Category Category(string slug, string en, string ar) => new() { Slug = slug, Translations = [new() { LanguageCode = "en", Name = en }, new() { LanguageCode = "ar", Name = ar }] };
    static Product Product(Category c, string slug, string en, string ar, string image, decimal price, decimal? old, bool featured) => new() {
        Category = c, Slug = slug, ImageUrl = "/assets/img/products/" + image, Price = price, OldPrice = old, StockQuantity = 24, IsFeatured = featured,
        Translations = [
            new() { LanguageCode = "en", Name = en, ShortDescription = "A considered essential for your everyday. Simple design, lasting comfort.", FullDescription = "Made for the way you live. This carefully selected piece combines a clean silhouette with everyday practicality. Explore the details in the product preview and choose your quantity below." },
            new() { LanguageCode = "ar", Name = ar, ShortDescription = "قطعة أساسية مختارة ليومك. تصميم بسيط وراحة تدوم.", FullDescription = "مصممة لتناسب أسلوب حياتك. تجمع هذه القطعة المختارة بعناية بين التصميم الأنيق والاستخدام العملي اليومي. استكشف التفاصيل في معاينة المنتج واختر الكمية المناسبة لك." }
        ]
    };
}
