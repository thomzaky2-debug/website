using System.Text.RegularExpressions;
namespace NileShop.Services;
public static class InstaPayHandle
{
    public const string UsernamePattern = @"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$";
    public static string? FromUsername(string? username)
    {
        var value = username?.Trim();
        return value is not null && Regex.IsMatch(value, UsernamePattern, RegexOptions.CultureInvariant) ? value + "@instapay" : null;
    }
}
