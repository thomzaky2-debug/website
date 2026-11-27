using System.Globalization;
namespace NileShop.Services;

/// <summary>Wording of every email a customer receives. Plain text on purpose: Arabic renders right to left
/// in every mail app and there is no markup to double-encode. Customers never see the gateway name.</summary>
public static class CustomerMessages
{
    public const string Brand = "Chic & Unique";
    const string SignOff = "مع تحياتنا،\nفريق Chic & Unique";

    public static string FirstName(string? fullName)
    {
        var name = (fullName ?? "").Trim();
        var space = name.IndexOf(' ');
        return space > 0 ? name[..space] : name;
    }

    public static string OrderStatus(string kind) => kind switch
    {
        "Created" => "استلمنا طلبك",
        "Paid" => "تم تأكيد الدفع",
        "Overpaid" => "تم تأكيد الدفع",
        "Confirmed" => "تم تأكيد الطلب",
        "Processing" => "طلبك قيد التجهيز",
        "Shipped" => "طلبك في الطريق",
        "Delivered" => "تم توصيل طلبك",
        "Cancelled" => "تم إلغاء الطلب",
        "Refunded" => "تم رد المبلغ",
        "Underpaid" => "تم إيقاف الطلب",
        _ => "تحديث على طلبك"
    };

    static string OrderLine(string kind) => kind switch
    {
        "Created" => "استلمنا طلبك وبانتظار إتمام الدفع. أول ما يتأكد الدفع هنبدأ نجهّزه.",
        "Paid" => "وصلنا دفعك، شكراً لك. هتلاقي حالة طلبك محدّثة في رابط المتابعة أدناه.",
        "Overpaid" => "وصلنا دفعك، شكراً لك. المبلغ الزائد عن قيمة الطلب أضفناه إلى محفظتك.",
        "Confirmed" => "تم تأكيد طلبك وهنبدأ تجهيزه.",
        "Processing" => "بنجهّز طلبك دلوقتي وهنبلغك أول ما يتشحن.",
        "Shipped" => "طلبك خرج للشحن وفي الطريق إليك. تقدر تتابع حالته من الرابط أدناه.",
        "Delivered" => "تم توصيل طلبك. نتمنى إنه يعجبك، وشكراً لاختيارك Chic & Unique.",
        "Cancelled" => "تم إلغاء طلبك لأن الدفع لم يكتمل. لو استخدمت رصيد المحفظة في الطلب فقد رجع إليها، وتقدر تعمل طلب جديد في أي وقت.",
        "Refunded" => "سجّلنا رد المبلغ المدفوع لطلبك. لو محتاج أي مساعدة، رد على هذه الرسالة وهنكون معاك.",
        "Underpaid" => "المبلغ الذي تم تحويله أقل من إجمالي الطلب، فأوقفنا الطلب وأضفنا المبلغ المحوّل إلى محفظتك في الموقع. تقدر تستخدمه في أي طلب جديد.",
        _ => "في تحديث جديد على طلبك. تقدر تتابعه من الرابط أدناه."
    };

    public static (string Subject, string Body) Order(string kind, string customerName, string orderNumber, decimal total, string trackingUrl)
    {
        var first = FirstName(customerName);
        var greeting = first.Length > 0 ? $"أهلاً {first}،" : "أهلاً بك،";
        var subject = $"{Brand} | {OrderStatus(kind)} · {orderNumber}";
        var body = $"{greeting}\n\n{OrderLine(kind)}\n\n" +
                   $"رقم الطلب: {orderNumber}\n" +
                   $"الإجمالي: {total.ToString("0.00", CultureInfo.InvariantCulture)} ج.م\n\n" +
                   $"تابع طلبك من هنا:\n{trackingUrl}\n\n" +
                   "احتفظ بهذا الرابط، فهو يفتح صفحة طلبك.\n\n" + SignOff;
        return (subject, body);
    }

    static string WhatsAppLine(string kind) => kind switch
    {
        "Refunded" => "سجّلنا رد المبلغ المدفوع لطلبك.",
        _ => OrderLine(kind)
    };

    /// <summary>Short WhatsApp copy of an order update. Paid kinds show the amount paid, the rest the order total.</summary>
    public static string OrderWhatsApp(string kind, string customerName, string orderNumber, decimal total, decimal paidAmount, string trackingUrl)
    {
        var first = FirstName(customerName);
        var greeting = first.Length > 0 ? $"أهلاً {first}،" : "أهلاً بك،";
        var paidKind = kind is "Paid" or "Overpaid";
        var amount = paidKind && paidAmount > 0
            ? $"المبلغ المدفوع: {paidAmount.ToString("0.00", CultureInfo.InvariantCulture)} ج.م"
            : $"الإجمالي: {total.ToString("0.00", CultureInfo.InvariantCulture)} ج.م";
        return $"{greeting}\n{WhatsAppLine(kind)}\n\nرقم الطلب: {orderNumber}\n{amount}\nتابع طلبك: {trackingUrl}\n\nChic & Unique\nهذه رسالة تلقائية.";
    }

    public static (string Subject, string Body) Otp(string code, int minutes) =>
        ($"{Brand} | رمز التحقق",
         $"رمز التحقق: {code}\n\nالرمز صالح لمدة {minutes.ToString(CultureInfo.InvariantCulture)} دقائق، ولا تشاركه مع أي شخص.\nلو لم تطلب هذا الرمز، تجاهل الرسالة.\n\n{SignOff}");

    public static (string Subject, string Body) PasswordReset(string link) =>
        ($"{Brand} | إعادة تعيين كلمة المرور",
         $"أهلاً بك،\n\nوصلنا طلب لإعادة تعيين كلمة المرور لحسابك. افتح الرابط التالي لاختيار كلمة مرور جديدة (صالح لمدة ساعة):\n\n{link}\n\nلو لم تطلب ذلك، تجاهل الرسالة وستبقى كلمة مرورك كما هي.\n\n{SignOff}");
}
