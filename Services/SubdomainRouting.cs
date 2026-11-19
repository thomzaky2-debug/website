using Microsoft.Extensions.Options;

namespace NileShop.Services;

public class SubdomainOptions
{
    public bool Enabled { get; set; } = false;
    public string? AdminHost { get; set; }
    public string? ClientHost { get; set; }
    public bool StrictRedirect { get; set; } = true;
}

public static class SubdomainRouting
{
    public static bool IsAdminHost(HttpContext context, SubdomainOptions options)
    {
        if (!options.Enabled) return false;
        var host = context.Request.Host.Host;
        if (!string.IsNullOrWhiteSpace(options.AdminHost))
        {
            return string.Equals(host, options.AdminHost.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        return host.StartsWith("admin.", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsClientHost(HttpContext context, SubdomainOptions options)
    {
        if (!options.Enabled) return false;
        var host = context.Request.Host.Host;
        if (!string.IsNullOrWhiteSpace(options.ClientHost))
        {
            return string.Equals(host, options.ClientHost.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        return !IsAdminHost(context, options);
    }

    public static string BuildTargetUrl(HttpContext context, string targetHost, string? path = null)
    {
        var scheme = context.Request.Scheme;
        var port = context.Request.Host.Port;
        var hostWithPort = port.HasValue && port.Value != 80 && port.Value != 443
            ? $"{targetHost}:{port.Value}"
            : targetHost;
        var targetPath = path ?? context.Request.Path.Value ?? "/";
        var query = context.Request.QueryString.Value ?? "";
        return $"{scheme}://{hostWithPort}{targetPath}{query}";
    }
}

public class SubdomainMiddleware(RequestDelegate next, IOptions<SubdomainOptions> optionsAccessor)
{
    private static readonly string[] AllowedAdminPrefixes =
    [
        "/assets",
        "/favicon",
        "/robots.txt",
        "/.well-known",
        "/Admin",
        "/StoreAdmin",
        "/Account/Login",
        "/Account/Logout",
        "/Account/AccessDenied",
        "/Identity",
        "/Home/SetLanguage",
        "/Home/Error"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var options = optionsAccessor.Value;
        if (!options.Enabled)
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "/";
        var isAdminHost = SubdomainRouting.IsAdminHost(context, options);

        if (isAdminHost)
        {
            // Root on admin host -> redirect to /StoreAdmin or /Account/Login
            if (path == "/" || string.IsNullOrWhiteSpace(path))
            {
                var destination = context.User.Identity?.IsAuthenticated == true && context.User.IsInRole("Admin")
                    ? "/StoreAdmin"
                    : "/Account/Login?returnUrl=%2FStoreAdmin";
                context.Response.Redirect(destination);
                return;
            }

            // Customer registration disallowed on admin subdomain
            if (path.StartsWith("/Account/Register", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(options.ClientHost))
                {
                    context.Response.Redirect(SubdomainRouting.BuildTargetUrl(context, options.ClientHost, "/Account/Register"));
                    return;
                }
                context.Response.Redirect("/Account/Login");
                return;
            }

            // Check if path is an allowed admin or system path
            var isAllowed = AllowedAdminPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (!isAllowed)
            {
                if (options.StrictRedirect && !string.IsNullOrWhiteSpace(options.ClientHost))
                {
                    context.Response.Redirect(SubdomainRouting.BuildTargetUrl(context, options.ClientHost));
                    return;
                }
                context.Response.Redirect("/StoreAdmin");
                return;
            }
        }
        else if (options.StrictRedirect && !string.IsNullOrWhiteSpace(options.AdminHost))
        {
            // Admin path accessed on client host -> redirect to admin host
            var isAdminPath = path.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/StoreAdmin", StringComparison.OrdinalIgnoreCase);

            if (isAdminPath)
            {
                context.Response.Redirect(SubdomainRouting.BuildTargetUrl(context, options.AdminHost));
                return;
            }
        }

        await next(context);
    }
}
