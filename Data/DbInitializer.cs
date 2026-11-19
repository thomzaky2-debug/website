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
        if (!await db.StoreSettings.AnyAsync()) db.StoreSettings.Add(new StoreSettings {
            DeliveryPolicyEn = """
                Order Processing & Confirmation
                • Orders are processed after NilePay payment confirmation is received.
                • Delivery timeframes are counted in business days (Sunday–Thursday, excluding Egyptian public holidays).

                Delivery Areas & Timeframes
                {{DELIVERY_AREAS}}
                • Exact shipping fees are calculated and displayed clearly before checkout.

                Order Tracking & Receiving Your Order
                • Once your order is dispatched, your private tracking link and account order page are updated with the delivery company and tracking number.
                • Please make sure your phone number, WhatsApp number (if provided), and full delivery address are accurate so the courier can reach you smoothly.
                • If a delivery attempt is missed, the courier will contact you to reschedule. Orders returned due to unreachable contact details may incur a re-delivery fee.

                Inspection Upon Delivery
                • Please check your package upon arrival and contact us immediately with your order number if an item arrives damaged, incomplete, or incorrect.
                """,
            DeliveryPolicyAr = """
                تجهيز الطلب وتأكيد الدفع
                • يبدأ تجهيز وشحن الطلب بعد تأكيد الدفع عبر نايل باي (NilePay).
                • تُحتسب مدد التوصيل بأيام العمل الرسمية (من الأحد إلى الخميس، باستثناء العطلات الرسمية في مصر).

                مناطق ومدة التوصيل
                {{DELIVERY_AREAS}}
                • تظهر رسوم التوصيل النهائية بوضوح قبل إتمام الطلب.

                تتبع الطلب واستلامه
                • فور شحن الطلب، يتم تحديث رابط التتبع الخاص بك وصفحة «طلباتي» باسم شركة الشحن ورقم التتبع.
                • يُرجى التأكد من صحة رقم الهاتف ورقم واتساب (إن وُجد) وعنوان التوصيل بالكامل لتسهيل تواصل المندوب معك.
                • في حال تعذّر التسليم في المحاولة الأولى، سيتواصل معك المندوب لتحديد موعد آخر.

                الفحص عند الاستلام
                • يُرجى فحص الشحنة عند الاستلام، والتواصل معنا فورًا برقم الطلب في حال وجود أي تلف أو نقص أو خطأ في المنتج.
                """,
            ReturnsPolicyEn = """
                Return Window
                • You may request a return or exchange within 14 days from the date your order is delivered.

                Eligibility Conditions
                • Items must be unused, unwashed, unworn, and in their original condition with all tags, accessories, and original packaging intact.
                • Defective, damaged, or incorrect items reported promptly after delivery are eligible for a full refund or replacement at no extra shipping cost.
                • For hygiene and safety reasons, opened personal care items, fragrances, or items showing signs of wear or damage caused by misuse are not eligible for return.

                How to Request a Return
                • Contact us via our Contact page with your order number, item name, and reason for return (include photos if the item arrived damaged or defective).
                • Our team will review your request and share return pickup or drop-off instructions once approved.

                Inspection & Refunds
                • Once the returned item is received and inspected, approved refunds are processed back via your agreed refund method (such as InstaPay transfer or store wallet credit).
                • Original delivery fees are non-refundable unless the return is due to a defective, damaged, or wrong item sent by NileShop.
                """,
            ReturnsPolicyAr = """
                مدة الاسترجاع والاستبدال
                • يمكنك طلب استرجاع أو استبدال المنتجات خلال 14 يومًا من تاريخ استلام الطلب.

                شروط قبول الاسترجاع
                • يجب أن يكون المنتج غير مستخدم وغير مغسول وبحالته الأصلية مع جميع البطاقات والملحقات والتغليف الأصلي.
                • في حال وصول منتج تالف أو به عيب صناعة أو غير مطابق للطلب، يحق لك الاسترجاع أو الاستبدال الكامل دون تحمل أي رسوم شحن إضافية.
                • حرصًا على السلامة والنظافة، لا تُقبل استرجاعات منتجات العناية الشخصية أو العطور بعد فتحها، أو المنتجات التي يظهر عليها سوء استخدام.

                خطوات طلب الاسترجاع
                • تواصل معنا عبر صفحة «تواصل معنا» مع ذكر رقم الطلب واسم المنتج وسبب الاسترجاع (مع إرفاق صور في حال وجود تلف أو خطأ).
                • سيقوم فريقنا بمراجعة الطلب والتواصل معك بترتيبات الاستلام أو الإرجاع بعد الموافقة.

                الفحص واسترداد المبلغ
                • بعد استلام المنتج المرتجع وفحصه والتأكد من مطابقته للشروط، يتم رد المبلغ وفق وسيلة الاسترداد المتفق عليها (مثل التحويل عبر إنستاباي أو إضافته لرصيد المحفظة).
                • رسوم التوصيل الأصلية غير قابلة للاسترداد إلا في حال كان الاسترجاع بسبب عيب أو تلف أو خطأ في الطلب من قِبل نايل شوب.
                """
        });
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
