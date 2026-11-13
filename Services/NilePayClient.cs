using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
namespace BubbleThemeShop.Services;
public class NilePayOptions
{
    public bool Enabled { get; set; }
    public string ApiUrl { get; set; } = "https://nilepay-api.runasp.net";
    public string ApiKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string PublicUrl { get; set; } = "";
}
public record NileCheckout(string SessionId, string Status, decimal AmountEgp, string Currency, string? CheckoutUrl);
public class NilePayException(string message) : Exception(message);
public class NilePayClient(HttpClient http, IOptions<NilePayOptions> settings, IHostEnvironment environment)
{
    readonly NilePayOptions options = settings.Value;
    public bool Ready => options.Enabled && !string.IsNullOrWhiteSpace(options.ApiKey) && !string.IsNullOrWhiteSpace(options.WebhookSecret) && Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var url) && url.Scheme == "https" && string.IsNullOrEmpty(url.UserInfo) && url.AbsolutePath == "/" && string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.Fragment);
    public bool ValidCheckoutUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && string.IsNullOrEmpty(url.UserInfo) && (url.Scheme == "https" || (environment.IsDevelopment() && url.Scheme == "http" && url.IsLoopback));
    public async Task<NileCheckout> CreateAsync(string payload, string key, CancellationToken ct) => await SendAsync(HttpMethod.Post, "/api/v1/checkout/create", payload, key, ct);
    public async Task<NileCheckout> StatusAsync(string sessionId, CancellationToken ct) => await SendAsync(HttpMethod.Get, "/api/v1/checkout/status?sessionId=" + Uri.EscapeDataString(sessionId), null, null, ct);
    async Task<NileCheckout> SendAsync(HttpMethod method, string path, string? body, string? key, CancellationToken ct)
    {
        if (!Ready) throw new NilePayException("Payment_Unavailable");
        if (!Uri.TryCreate(options.ApiUrl, UriKind.Absolute, out var origin) || !string.IsNullOrEmpty(origin.UserInfo) || (origin.Scheme != "https" && !(environment.IsDevelopment() && origin.Scheme == "http" && origin.IsLoopback))) throw new NilePayException("Payment_Unavailable");
        try {
            for (int retry = 0; ; retry++) {
                using var request = new HttpRequestMessage(method, new Uri(origin, path));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                if (key is not null) request.Headers.Add("Idempotency-Key", key);
                if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await http.SendAsync(request, ct);
                if ((response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && retry < 1) { await Task.Delay(500, ct); continue; }
                if (!response.IsSuccessStatusCode) throw new NilePayException(response.StatusCode == System.Net.HttpStatusCode.Conflict ? "Payment_Conflict" : "Payment_Retry");
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (!json.RootElement.GetProperty("ok").GetBoolean()) throw new NilePayException("Payment_Retry");
                return Parse(json.RootElement.GetProperty("checkout"));
            }
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { throw new NilePayException("Payment_Retry"); }
    }
    public static NileCheckout Parse(JsonElement data) => new(data.GetProperty("sessionId").GetString()!, data.GetProperty("status").GetString()!, data.GetProperty("amountEgp").GetDecimal(), data.GetProperty("currency").GetString()!, data.TryGetProperty("checkoutUrl", out var url) ? url.GetString() : null);
}
