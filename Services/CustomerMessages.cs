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
        "Overpaid" => "تم الدفع بزيادة",
        "Confirmed" => "تم تأكيد الطلب",
        "Processing" => "طلبك قيد التجهيز",
        "Shipped" => "طلبك في الطريق",
        "Delivered" => "تم توصيل طلبك",
        "Cancelled" => "تم إلغاء الطلب",
        "Refunded" => "تم رد المبلغ",
        "Underpaid" => "تم إيقاف الطلب",
        _ => "تحديث على طلبك"
    };

    static string OrderLine(string kind, decimal walletCredit = 0) => kind switch
    {
        "Created" => "استلمنا طلبك وبانتظار إتمام الدفع. أول ما يتأكد الدفع هنبدأ نجهّزه.",
        "Paid" => "وصلنا دفعك، شكراً لك. هتلاقي حالة طلبك محدّثة في رابط المتابعة أدناه.",
        "Overpaid" when walletCredit > 0 => "وصلنا دفعك، شكراً لك. المبلغ الذي دفعته أكبر من إجمالي الطلب، وأضفنا الزيادة إلى محفظتك في الموقع. تقدر تستخدمها في أي طلب جديد.",
        "Overpaid" => "وصلنا دفعك، شكراً لك. المبلغ الذي دفعته أكبر من إجمالي الطلب، وهنتواصل معك بخصوص المبلغ الزائد.",
        "Confirmed" => "تم تأكيد طلبك وهنبدأ تجهيزه.",
        "Processing" => "بنجهّز طلبك دلوقتي وهنبلغك أول ما يتشحن.",
        "Shipped" => "طلبك خرج للشحن وفي الطريق إليك. تقدر تتابع حالته من الرابط أدناه.",
        "Delivered" => "تم توصيل طلبك. نتمنى إنه يعجبك، وشكراً لاختيارك Chic & Unique.",
        "Cancelled" => "تم إلغاء طلبك لأن الدفع لم يكتمل. لو استخدمت رصيد المحفظة في الطلب فقد رجع إليها، وتقدر تعمل طلب جديد في أي وقت.",
        "Refunded" => "سجّلنا رد المبلغ المدفوع لطلبك. لو محتاج أي مساعدة، رد على هذه الرسالة وهنكون معاك.",
        "Underpaid" => "المبلغ الذي تم تحويله أقل من إجمالي الطلب، فأوقفنا الطلب وأضفنا المبلغ المحوّل إلى محفظتك في الموقع. تقدر تستخدمه في أي طلب جديد.",
        _ => "في تحديث جديد على طلبك. تقدر تتابعه من الرابط أدناه."
    };

    static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture) + " ج.م";

    /// <summary>Amounts a payment notice states: what the customer paid, and the extra credited to the wallet.
    /// The wallet credit is only claimed for an account customer when the stored amounts show an excess, which is
    /// exactly when the payment code credits the wallet.</summary>
    public static (decimal Paid, decimal WalletCredit) PaymentAmounts(string kind, Models.Order order)
    {
        var payable = order.PayableAmount;
        if (kind != "Overpaid") return (payable, 0m);
        var detected = order.DetectedAmountEgp ?? payable;
        var credit = !string.IsNullOrWhiteSpace(order.CustomerUserId) && detected > payable ? decimal.Round(detected - payable, 2) : 0m;
        return (detected, credit);
    }

    static string AmountLines(string kind, decimal total, decimal paid, decimal walletCredit)
    {
        if (kind == "Overpaid" && paid > 0)
            return walletCredit > 0
                ? $"المبلغ المدفوع: {Money(paid)}\nالزيادة المضافة إلى محفظتك: {Money(walletCredit)}"
                : $"المبلغ المدفوع: {Money(paid)}\nإجمالي الطلب: {Money(total)}";
        if (kind == "Paid" && paid > 0) return $"المبلغ المدفوع: {Money(paid)}";
        return $"الإجمالي: {Money(total)}";
    }

    public static (string Subject, string Body) Order(string kind, string customerName, string orderNumber, decimal total, string trackingUrl, decimal paidAmount = 0, decimal walletCredit = 0)
    {
        var first = FirstName(customerName);
        var greeting = first.Length > 0 ? $"أهلاً {first}،" : "أهلاً بك،";
        var subject = $"{Brand} | {OrderStatus(kind)} · {orderNumber}";
        var body = $"{greeting}\n\n{OrderLine(kind, walletCredit)}\n\n" +
                   $"رقم الطلب: {orderNumber}\n" +
                   $"{AmountLines(kind, total, paidAmount, walletCredit)}\n\n" +
                   $"تابع طلبك من هنا:\n{trackingUrl}\n\n" +
                   "احتفظ بهذا الرابط، فهو يفتح صفحة طلبك.\n\n" + SignOff;
        return (subject, body);
    }

    static string WhatsAppLine(string kind, decimal walletCredit) => kind switch
    {
        "Refunded" => "سجّلنا رد المبلغ المدفوع لطلبك.",
        _ => OrderLine(kind, walletCredit)
    };

    /// <summary>Short WhatsApp copy of an order update, same wording and amounts as the email.</summary>
    public static string OrderWhatsApp(string kind, string customerName, string orderNumber, decimal total, decimal paidAmount, string trackingUrl, decimal walletCredit = 0)
    {
        var first = FirstName(customerName);
        var greeting = first.Length > 0 ? $"أهلاً {first}،" : "أهلاً بك،";
        return $"{greeting}\n{WhatsAppLine(kind, walletCredit)}\n\nرقم الطلب: {orderNumber}\n{AmountLines(kind, total, paidAmount, walletCredit)}\nتابع طلبك: {trackingUrl}\n\nChic & Unique\nهذه رسالة تلقائية.";
    }

    public static (string Subject, string Body) Otp(string code, int minutes) =>
        ($"{Brand} | رمز التحقق",
         $"رمز التحقق: {code}\n\nالرمز صالح لمدة {minutes.ToString(CultureInfo.InvariantCulture)} دقائق، ولا تشاركه مع أي شخص.\nلو لم تطلب هذا الرمز، تجاهل الرسالة.\n\n{SignOff}");

    public static (string Subject, string Body) PasswordReset(string link) =>
        ($"{Brand} | إعادة تعيين كلمة المرور",
         $"أهلاً بك،\n\nوصلنا طلب لإعادة تعيين كلمة المرور لحسابك. افتح الرابط التالي لاختيار كلمة مرور جديدة (صالح لمدة ساعة):\n\n{link}\n\nلو لم تطلب ذلك، تجاهل الرسالة وستبقى كلمة مرورك كما هي.\n\n{SignOff}");
}
