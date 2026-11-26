using System.Globalization;

namespace NileShop.Services;

public static class MoneyText
{
    // One presentation format for every amount on the storefront: invariant culture,
    // dot decimal separator, two decimals, thousands grouping. Layout/RTL isolation
    // is handled in the views (bdi), not here.
    public static string Format(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
}
