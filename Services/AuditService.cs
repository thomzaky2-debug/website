using BubbleThemeShop.Data;
using BubbleThemeShop.Models;
namespace BubbleThemeShop.Services;
public class AuditService(ApplicationDbContext db, IHttpContextAccessor accessor)
{
    static string? Cut(string? value, int length) => value is null ? null : value[..Math.Min(value.Length, length)];
    public async Task LogAsync(string action, string entityName, string details)
    {
        db.AuditLogs.Add(new AuditLog {
            UserEmail = Cut(accessor.HttpContext?.User.Identity?.Name ?? "System", 100),
            Action = Cut(action, 50)!, EntityName = Cut(entityName, 100), Details = Cut(details, 500),
            IpAddress = Cut(accessor.HttpContext?.Connection.RemoteIpAddress?.ToString(), 45)
        });
        await db.SaveChangesAsync();
    }
}
