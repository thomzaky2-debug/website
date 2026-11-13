using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BubbleThemeShop.Data;
using BubbleThemeShop.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace BubbleThemeShop.Controllers;
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class PaymentController(ApplicationDbContext db, NilePayService payments, NilePayClient client, IOptions<NilePayOptions> options, ILogger<PaymentController> logger) : Controller
{
    async Task<Models.Order?> Owned(string? token) => token?.Length == 64 ? await db.Orders.AsNoTracking().Include(o => o.Items).SingleOrDefaultAsync(o => o.PaymentAccessToken == token) : null;
    [HttpGet("/payment/return")]
    public async Task<IActionResult> Return(string token)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var order = await Owned(token); if (order is null) return NotFound();
        ViewData["GatewayStatus"] = await db.PaymentAttempts.Where(p => p.OrderId == order.Id).Select(p => p.GatewayStatus).SingleOrDefaultAsync() ?? "NOT_CREATED";
        return View(order);
    }
    [HttpPost("/payment/pay"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(string token, CancellationToken ct)
    {
        var order = await Owned(token); if (order is null) return NotFound();
        if (order.PaymentStatus == "Paid" || order.Status == Models.OrderStatuses.Cancelled) return RedirectToAction(nameof(Return), new { token });
        try {
            var attempt = await payments.StartAsync(order, ct);
            await payments.ReconcileAsync(attempt.Id, ct);
            var fresh = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id, ct);
            var state = await db.PaymentAttempts.AsNoTracking().SingleAsync(p => p.Id == attempt.Id, ct);
            if (fresh.PaymentStatus == "Paid" || state.GatewayStatus is "EXPIRED" or "CANCELLED" or "FAILED") return RedirectToAction(nameof(Return), new { token });
            if (!client.ValidCheckoutUrl(attempt.CheckoutUrl)) throw new NilePayException("Payment_Retry");
            return Redirect(attempt.CheckoutUrl!);
        } catch (NilePayException ex) { TempData["Error"] = ex.Message; return RedirectToAction(nameof(Return), new { token }); }
    }
    [HttpPost("/payment/check"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Check(string token, CancellationToken ct)
    {
        var order = await Owned(token); if (order is null) return NotFound();
        try {
            var attempt = await db.PaymentAttempts.AsNoTracking().SingleOrDefaultAsync(p => p.OrderId == order.Id, ct);
            if (attempt is not null && order.PaymentStatus != "Paid") await payments.ReconcileAsync(attempt.Id, ct);
        } catch (NilePayException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Return), new { token });
    }
    [HttpPost("/webhooks/nilepay"), IgnoreAntiforgeryToken, RequestSizeLimit(65536)]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        if (!client.Ready) return StatusCode(503);
        using var body = new MemoryStream(); await Request.Body.CopyToAsync(body, ct);
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
