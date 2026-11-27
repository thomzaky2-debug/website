using Microsoft.AspNetCore.Identity;
namespace NileShop.Services;

/// <summary>An admin block is an Identity lockout that ends far in the future. Short lockouts from failed passwords are not blocks.</summary>
public static class AccountBlock
{
    public static readonly DateTimeOffset BlockedUntil = new(2999, 12, 31, 0, 0, 0, TimeSpan.Zero);
    public static bool IsBlockedEnd(DateTimeOffset? end) => end.HasValue && end.Value > DateTimeOffset.UtcNow.AddYears(10);
    public static bool IsBlocked(IdentityUser user) => user.LockoutEnabled && IsBlockedEnd(user.LockoutEnd);
}
