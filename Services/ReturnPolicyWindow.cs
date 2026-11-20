using System.Globalization;
using System.Text.RegularExpressions;

namespace NileShop.Services;

public static class ReturnPolicyWindow
{
    // Match the return-request sentence, leaving separate delivery and refund deadlines intact.
    public const string EnglishPattern = @"(\b(?:request\s+a\s+return(?:\s+or\s+exchange)?|request\s+an\s+exchange)\b[^.!?\r\n]*?\bwithin\s+)[0-9]+(?=\s+days?\b)";
    public const string ArabicPattern = @"((?:طلب\s+)?(?:استرجاع|إرجاع|استبدال)[^.!؟\r\n]*?خلال\s+)[0-9٠-٩۰-۹]+(?=\s+(?:يوم|أيام))";

    public static string? Apply(string? policy, int days, bool arabic)
    {
        if (policy is null || days is < 1 or > 90) return policy;
        return Regex.Replace(policy, arabic ? ArabicPattern : EnglishPattern,
            match => match.Groups[1].Value + days.ToString(CultureInfo.InvariantCulture),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
