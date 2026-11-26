using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NileShop.Data;
using NileShop.Models;
namespace NileShop.Controllers;
[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class NotificationsController(ApplicationDbContext db, UserManager<IdentityUser> users) : Controller
{
    string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    IQueryable<ClientNotification> Visible => db.ClientNotifications.Where(n => n.RecipientId == null || n.RecipientId == UserId);
    [HttpGet("/Account/Notifications")]
    public async Task<IActionResult> Index(int page = 1)
    {
        page = Math.Clamp(page, 1, 100000);
        ViewBag.Page = page;
        var items = await Visible.AsNoTracking().OrderByDescending(n => n.Id).Skip((page - 1) * 20).Take(21).ToListAsync();
        var ids = items.Select(n => n.Id).ToArray();
        ViewBag.ReadIds = await db.NotificationReads.Where(r => r.UserId == UserId && ids.Contains(r.NotificationId)).Select(r => r.NotificationId).ToListAsync();
        return View(items);
    }
    [HttpGet("/Account/Notifications/Unread")]
    public async Task<IActionResult> Unread() => Json(new { count = await Visible.CountAsync(n => !n.Reads.Any(r => r.UserId == UserId)) });
    [HttpPost("/Account/Notifications/Read")]
    public async Task<IActionResult> Read(int id)
    {
        if (!await Visible.AnyAsync(n => n.Id == id)) return NotFound();
        if (!await db.NotificationReads.AnyAsync(r => r.NotificationId == id && r.UserId == UserId))
        {
            db.NotificationReads.Add(new NotificationRead { NotificationId = id, UserId = UserId });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { if (!await db.NotificationReads.AsNoTracking().AnyAsync(r => r.NotificationId == id && r.UserId == UserId)) throw; }
        }
        if (Request.Headers.Accept.ToString().Contains("application/json")) return Json(new { success = true });
        return RedirectToAction(nameof(Index));
    }
    async Task LoadHistory()
    {
        var history = await db.ClientNotifications.AsNoTracking().OrderByDescending(n => n.Id).Take(30).ToListAsync();
        var ids = history.Where(n => n.RecipientId != null).Select(n => n.RecipientId!).Distinct().ToArray();
        ViewBag.History = history;
        ViewBag.Recipients = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email ?? "");
        var historyIds = history.Select(n => n.Id).ToArray();
        ViewBag.ReadCounts = await db.NotificationReads.AsNoTracking().Where(r => historyIds.Contains(r.NotificationId))
            .GroupBy(r => r.NotificationId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count);
        ViewBag.CustomerCount = await db.Users.AsNoTracking().CountAsync(u =>
            !db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == "Admin")));
    }
    [Authorize(Roles = "Admin"), HttpGet("/Admin/Notifications/Customers")]
    public async Task<IActionResult> Customers(string? q)
    {
        q = q?.Trim();
        if (q?.Length > 100) return BadRequest();
        var customers = db.Users.AsNoTracking().Where(u => u.Email != null &&
            !db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == "Admin")));
        if (!string.IsNullOrEmpty(q)) customers = customers.Where(u => u.Email!.Contains(q) ||
            db.UserClaims.Any(c => c.UserId == u.Id && c.ClaimType == NileShop.ViewModels.CustomerProfileViewModel.FullNameClaim && c.ClaimValue != null && c.ClaimValue.Contains(q)));
        return Json(await customers.OrderBy(u => u.Email).Take(20).Select(u => new {
            email = u.Email,
            name = db.UserClaims.Where(c => c.UserId == u.Id && c.ClaimType == NileShop.ViewModels.CustomerProfileViewModel.FullNameClaim).OrderBy(c => c.Id).Select(c => c.ClaimValue).FirstOrDefault() ?? u.Email
        }).ToListAsync(HttpContext.RequestAborted));
    }
    [Authorize(Roles = "Admin"), HttpGet("/Admin/Notifications")]
    public async Task<IActionResult> Manage(string? title, string? body, string? audience)
    {
        await LoadHistory();
        var model = new ComposeNotification();
        if (!string.IsNullOrWhiteSpace(title) && title.Length <= 160 && !string.IsNullOrWhiteSpace(body) && body.Length <= 2000
            && (audience == "customer" || audience == "everyone"))
        {
            model.Title = title;
            model.Body = body;
            model.Audience = audience;
        }
        return View(model);
    }
    [Authorize(Roles = "Admin"), HttpPost("/Admin/Notifications/Delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var notification = await db.ClientNotifications.FirstOrDefaultAsync(n => n.Id == id);
        if (notification is null) return NotFound();
        db.NotificationReads.RemoveRange(db.NotificationReads.Where(r => r.NotificationId == id));
        db.ClientNotifications.Remove(notification);
        await db.SaveChangesAsync();
        TempData["Success"] = "Notification_Deleted";
        return RedirectToAction(nameof(Manage));
    }
    [Authorize(Roles = "Admin"), HttpPost("/Admin/Notifications")]
    public async Task<IActionResult> Send(ComposeNotification model)
    {
        IdentityUser? recipient = null;
        if (model.SendKey == Guid.Empty) ModelState.AddModelError("", "Invalid submission. Reload the page.");
        if (model.Audience == "everyone") model.RecipientEmail = null;
        if (model.Audience == "customer" && string.IsNullOrWhiteSpace(model.RecipientEmail))
            ModelState.AddModelError(nameof(model.RecipientEmail), "Choose a customer. / اختر عميلاً");
        if (model.Audience == "customer" && !string.IsNullOrWhiteSpace(model.RecipientEmail))
        {
            recipient = await users.FindByEmailAsync(model.RecipientEmail.Trim());
            if (recipient is null || await users.IsInRoleAsync(recipient, "Admin")) ModelState.AddModelError(nameof(model.RecipientEmail), "Customer email was not found. / البريد الإلكتروني للعميل غير موجود");
        }
        if (!ModelState.IsValid)
        {
            await LoadHistory();
            return View("Manage", model);
        }
        if (!await db.ClientNotifications.AnyAsync(n => n.SendKey == model.SendKey))
        {
            db.ClientNotifications.Add(new ClientNotification { SendKey = model.SendKey, RecipientId = recipient?.Id, Title = model.Title.Trim(), Body = model.Body.Trim() });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { if (!await db.ClientNotifications.AsNoTracking().AnyAsync(n => n.SendKey == model.SendKey)) throw; }
        }
        TempData["Success"] = "Notification_Sent";
        return RedirectToAction(nameof(Manage));
    }
}
