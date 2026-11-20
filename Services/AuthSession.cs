namespace NileShop.Services;

public static class AuthSession
{
    public static void ClearCheckout(HttpContext context)
    {
        // Keep shopping selections, but don't carry private order capabilities between accounts.
        foreach (var key in new[] { "CheckoutToken", "LastCheckoutToken", "LastOrder" }) context.Session.Remove(key);
    }
}
