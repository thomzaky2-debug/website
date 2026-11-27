using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NileShop.Services;

public sealed class WhatsAppOtpOptions
{
    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    // Customer notices (api/notify on the same service). Off until the service has that endpoint.
    public bool NotifyEnabled { get; set; }
}

// Thin client for the WhatsApp OTP service (send/verify challenge API).
// The API key only ever travels in the x-api-key header to the configured
// BaseUrl; it is never logged. Code values are never logged either.
public sealed class WhatsAppOtpClient(HttpClient http, IOptions<WhatsAppOtpOptions> options, ILogger<WhatsAppOtpClient> logger)
{
    public bool Configured => options.Value.Enabled
        && !string.IsNullOrWhiteSpace(options.Value.BaseUrl)
        && Uri.TryCreate(options.Value.BaseUrl, UriKind.Absolute, out var baseUri)
        && baseUri.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public bool NotifyReady => Configured && options.Value.NotifyEnabled;

    public enum NoticeResult { Sent, Retry, Rejected }

    // Plain customer notice through the service's api/notify. Never throws: Retry for any transient or
    // missing-endpoint problem (the caller backs off), Rejected only when the number is not on WhatsApp or
    // the service refuses the request itself.
    public async Task<NoticeResult> SendNoticeAsync(string phone, string message, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Url("api/notify"))
            { Content = JsonContent.Create(new { phoneNumber = phone, message }) };
            request.Headers.Add("x-api-key", options.Value.ApiKey);
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return NoticeResult.Sent;
            var status = (int)response.StatusCode;
            logger.LogWarning("WhatsApp notice rejected: HTTP {Status}", status);
            return status is 400 or 422 ? NoticeResult.Rejected : NoticeResult.Retry;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning("WhatsApp notice could not complete ({ErrorType})", error.GetType().Name);
            return NoticeResult.Retry;
        }
    }

    private Uri Url(string path) => new(options.Value.BaseUrl!.TrimEnd('/') + "/" + path);

    // Returns true when the service accepted the challenge (challengeId returned).
    public async Task<bool> SendAsync(string phone, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Url("api/otp/send"))
            { Content = JsonContent.Create(new { phoneNumber = phone }) };
            request.Headers.Add("x-api-key", options.Value.ApiKey);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("WhatsApp OTP send rejected: HTTP {Status}", (int)response.StatusCode);
                return false;
            }
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.TryGetProperty("challengeId", out _);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            logger.LogWarning("WhatsApp OTP send could not complete ({ErrorType})", error.GetType().Name);
            return false;
        }
    }

    // True = verified, false = rejected code, null = transport/service error (attempt not consumed locally).
    public async Task<bool?> VerifyAsync(string phone, string code, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Url("api/otp/verify"))
            { Content = JsonContent.Create(new { phoneNumber = phone, code }) };
            request.Headers.Add("x-api-key", options.Value.ApiKey);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("WhatsApp OTP verify rejected: HTTP {Status}", (int)response.StatusCode);
                return null;
            }
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.TryGetProperty("verified", out var verified) && verified.GetBoolean();
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            logger.LogWarning("WhatsApp OTP verify could not complete ({ErrorType})", error.GetType().Name);
            return null;
        }
    }

    // Egyptian local mobile (01xxxxxxxxx) and 00-prefixed numbers are normalized to
    // E.164; anything already international is kept as-is.
    public static string NormalizePhone(string phone)
    {
        var trimmed = phone.Trim().Replace(" ", "").Replace("-", "");
        if (trimmed.StartsWith('+')) return trimmed;
        if (trimmed.StartsWith("00")) return "+" + trimmed[2..];
        if (trimmed.Length == 11 && trimmed.StartsWith("01")) return "+2" + trimmed;
        return trimmed;
    }
}
