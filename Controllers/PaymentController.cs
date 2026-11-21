using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NileShop.Data;
using NileShop.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace NileShop.Controllers;
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class PaymentController(ApplicationDbContext db, NilePayService payments, NilePayClient client, IOptions<NilePayOptions> options, ILogger<PaymentController> logger) : Controller
{
    async Task<Models.Order?> Owned(string? token) => token?.Length == 64 ? await db.Orders.AsNoTracking().Include(o => o.Items).SingleOrDefaultAsync(o => o.PaymentAccessToken == token) : null;
    [HttpGet("/payment/return")]
    public async Task<IActionResult> Return(string token)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var order = await Owned(token); if (order is null) return NotFound();
        var attempt = await db.PaymentAttempts.AsNoTracking().Where(p => p.OrderId == order.Id).OrderByDescending(p => p.Id).FirstOrDefaultAsync();
        var lifetimeMinutes = await db.StoreSettings.AsNoTracking().OrderBy(s => s.Id).Select(s => s.CheckoutSessionLifetimeMinutes).FirstOrDefaultAsync();
        if (lifetimeMinutes <= 0) lifetimeMinutes = 15;

        var createdAt = attempt?.CreatedAt ?? order.CreatedAt;
        var expiresAt = createdAt.AddMinutes(lifetimeMinutes);
        var remainingSeconds = Math.Max(0, (int)(expiresAt - DateTime.UtcNow).TotalSeconds);
        var isSessionExpired = remainingSeconds <= 0 && order.PaymentStatus == "Unpaid";

        var gateway = attempt?.GatewayStatus ?? "NOT_CREATED";
        if (isSessionExpired && gateway is not ("CONFIRMED" or "PAID")) gateway = "EXPIRED";

        if ((isSessionExpired || gateway is "EXPIRED" or "CANCELLED" or "FAILED" or "UNDERPAID") && !order.InventoryReleased && order.PaymentStatus == "Unpaid" && order.PaidAt == null)
        {
            await payments.ReleaseInventoryIfUnpaidAsync(order.Id, $"Session {gateway.ToLowerInvariant()} on return", HttpContext.RequestAborted);
            order = await Owned(token) ?? order;
        }

        ViewData["GatewayStatus"] = gateway;
        ViewData["ExpiresAt"] = expiresAt.ToString("o");
        ViewData["RemainingSeconds"] = remainingSeconds;
        ViewData["LifetimeMinutes"] = lifetimeMinutes;
        ViewData["Attempt"] = attempt;
        return View(order);
    }
    [HttpPost("/payment/pay"), ValidateAntiForgeryToken, Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("payment-status")]
    public async Task<IActionResult> Pay(string token, CancellationToken ct)
    {
        var order = await Owned(token); if (order is null) return NotFound();
        if (order.PaymentStatus != "Unpaid" || order.Status == Models.OrderStatuses.Cancelled) return RedirectToAction(nameof(Return), new { token });
        try {
            var attempt = await payments.StartAsync(order, ct);
            await payments.ReconcileAsync(attempt.Id, ct);
            var fresh = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id, ct);
            var state = await db.PaymentAttempts.AsNoTracking().SingleAsync(p => p.Id == attempt.Id, ct);
            if (fresh.PaymentStatus != "Unpaid" || state.GatewayStatus is "EXPIRED" or "CANCELLED" or "FAILED") return RedirectToAction(nameof(Return), new { token });
            if (!client.ValidCheckoutUrl(attempt.CheckoutUrl)) throw new NilePayException("Payment_Retry");
            return Redirect(attempt.CheckoutUrl!);
        } catch (NilePayException ex) { TempData["Error"] = ex.Message; return RedirectToAction(nameof(Return), new { token }); }
    }
    [HttpPost("/payment/check"), ValidateAntiForgeryToken, Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("payment-status")]
    public async Task<IActionResult> Check(string token, CancellationToken ct)
    {
        var order = await Owned(token); if (order is null) return NotFound();
        try {
            var attempt = await db.PaymentAttempts.AsNoTracking().SingleOrDefaultAsync(p => p.OrderId == order.Id, ct);
            if (attempt is not null && order.PaymentStatus == "Unpaid") await payments.ReconcileAsync(attempt.Id, ct);
        } catch (NilePayException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Return), new { token });
    }
    [HttpPost("/payment/status"), ValidateAntiForgeryToken, Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("payment-status")]
    public async Task<IActionResult> Status(string token, CancellationToken ct)
    {
        var order = await Owned(token); if (order is null) return NotFound();
        var attempt = await db.PaymentAttempts.AsNoTracking().SingleOrDefaultAsync(p => p.OrderId == order.Id, ct);
        if (attempt is not null && attempt.SessionId is not null && order.PaymentStatus == "Unpaid" && attempt.GatewayStatus is not ("EXPIRED" or "CANCELLED" or "FAILED"))
            try { await payments.ReconcileAsync(attempt.Id, ct); } catch (NilePayException) { logger.LogWarning("Payment status refresh failed for order {OrderId}", order.Id); }
        var fresh = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id, ct);
        var gateway = attempt is null ? "NOT_CREATED" : await db.PaymentAttempts.AsNoTracking().Where(p => p.Id == attempt.Id).Select(p => p.GatewayStatus).SingleAsync(ct);
        var lifetimeMinutes = await db.StoreSettings.AsNoTracking().OrderBy(s => s.Id).Select(s => s.CheckoutSessionLifetimeMinutes).FirstOrDefaultAsync(ct);
        if (lifetimeMinutes <= 0) lifetimeMinutes = 15;
        var createdAt = attempt?.CreatedAt ?? fresh.CreatedAt;
        var expiresAt = createdAt.AddMinutes(lifetimeMinutes);
        var remainingSeconds = Math.Max(0, (int)(expiresAt - DateTime.UtcNow).TotalSeconds);
        if (remainingSeconds <= 0 && fresh.PaymentStatus == "Unpaid" && gateway is not ("CONFIRMED" or "PAID")) gateway = "EXPIRED";

        if ((remainingSeconds <= 0 || gateway is "EXPIRED" or "CANCELLED" or "FAILED" or "UNDERPAID") && !fresh.InventoryReleased && fresh.PaymentStatus == "Unpaid" && fresh.PaidAt == null)
        {
            await payments.ReleaseInventoryIfUnpaidAsync(fresh.Id, $"Session {gateway.ToLowerInvariant()} on status check", ct);
            fresh = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id, ct);
        }

        return Json(new { paymentStatus = fresh.PaymentStatus, orderStatus = fresh.Status, gatewayStatus = gateway, review = fresh.NeedsPaymentReview, remainingSeconds });
    }
    [HttpPost("/webhooks/nilepay"), IgnoreAntiforgeryToken, RequestSizeLimit(65536)]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        if (!client.Ready) return StatusCode(503);
        using var body = new MemoryStream();
        var buffer = new byte[8192]; int read;
        while ((read = await Request.Body.ReadAsync(buffer, ct)) > 0) {
            if (body.Length + read > 65536) return StatusCode(StatusCodes.Status413PayloadTooLarge);
            await body.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        var timestamp = Request.Headers["X-Instapay-Timestamp"].ToString();
        var signature = Request.Headers["X-Instapay-Signature"].ToString();
        var eventHeader = Request.Headers["X-Instapay-Event-Id"].ToString();
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds < DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 300 || seconds > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300 || signature.Length != 67 || !signature.StartsWith("v1=") || string.IsNullOrEmpty(eventHeader)) return Unauthorized();
        var prefix = Encoding.UTF8.GetBytes(timestamp + "."); var raw = body.ToArray();
        var signed = new byte[prefix.Length + raw.Length]; prefix.CopyTo(signed, 0); raw.CopyTo(signed, prefix.Length);
        byte[] provided; try { provided = Convert.FromHexString(signature[3..]); } catch (FormatException) { return Unauthorized(); }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.WebhookSecret), signed);
        if (!CryptographicOperations.FixedTimeEquals(expected, provided)) return Unauthorized();
        try {
            using var json = JsonDocument.Parse(raw);
            var eventId = json.RootElement.GetProperty("eventId").GetString();
            if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 200 || eventId != eventHeader) return BadRequest();
            var transaction = NilePayClient.Parse(json.RootElement.GetProperty("transaction"));
            if (string.IsNullOrWhiteSpace(transaction.SessionId) || transaction.SessionId.Length > 200) return BadRequest();
            return await payments.ApplyAsync(transaction, eventId, ct) ? Ok() : StatusCode(503);
        } catch (NilePayException) { logger.LogWarning("Rejected NilePay event with mismatched order values"); return BadRequest(); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return BadRequest(); }
        catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException) { logger.LogWarning(ex, "NilePay event database commit failed; delivery must retry"); return StatusCode(503); } // Concurrent delivery retries after the winning transaction commits.
    }
}
