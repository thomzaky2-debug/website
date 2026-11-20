using System.Security.Cryptography;

namespace NileShop.Services;

public class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        context.Items["CspNonce"] = nonce;
        context.Response.OnStarting(() => {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            if (!headers.ContainsKey("Referrer-Policy")) headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            if (new[] { "/Account", "/Identity", "/Admin", "/StoreAdmin", "/payment", "/orders", "/Cart" }.Any(path => context.Request.Path.StartsWithSegments(path))) {
                headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
                headers["Referrer-Policy"] = "no-referrer";
                headers.CacheControl = "no-store";
            }
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            // HTTPS form redirects are required by NilePay hosted checkout.
            headers["Content-Security-Policy"] = $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; script-src-attr 'none'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self' https:";
            if (context.User.Identity?.IsAuthenticated == true) headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });
        await next(context);
    }
}
