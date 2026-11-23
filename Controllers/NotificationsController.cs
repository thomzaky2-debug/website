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
    [Authorize(Roles = "Admin"), HttpGet("/Admin/Notifications")]
    public async Task<IActionResult> Manage()
    {
        ViewBag.History = await db.ClientNotifications.AsNoTracking().OrderByDescending(n => n.Id).Take(30).ToListAsync();
        return View(new ComposeNotification());
    }
    [Authorize(Roles = "Admin"), HttpPost("/Admin/Notifications")]
    public async Task<IActionResult> Send(ComposeNotification model)
    {
        IdentityUser? recipient = null;
        if (model.SendKey == Guid.Empty) ModelState.AddModelError("", "Invalid submission. Reload the page.");
        if (!string.IsNullOrWhiteSpace(model.RecipientEmail))
        {
            recipient = await users.FindByEmailAsync(model.RecipientEmail.Trim());
            if (recipient is null || await users.IsInRoleAsync(recipient, "Admin")) ModelState.AddModelError(nameof(model.RecipientEmail), "Customer email was not found. / البريد الإلكتروني للعميل غير موجود");
        }
        if (!ModelState.IsValid)
        {
            ViewBag.History = await db.ClientNotifications.AsNoTracking().OrderByDescending(n => n.Id).Take(30).ToListAsync();
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
