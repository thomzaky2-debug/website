using System.ComponentModel.DataAnnotations;
namespace NileShop.Models;
public class ClientNotification
{
    public int Id { get; set; }
    public Guid SendKey { get; set; }
    [MaxLength(450)] public string? RecipientId { get; set; }
    [Required, MaxLength(160)] public string Title { get; set; } = "";
    [Required, MaxLength(2000)] public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<NotificationRead> Reads { get; set; } = new List<NotificationRead>();
}
public class NotificationRead
{
    public int NotificationId { get; set; }
    public ClientNotification Notification { get; set; } = null!;
    [MaxLength(450)] public string UserId { get; set; } = "";
}
public class ComposeNotification
{
    public Guid SendKey { get; set; } = Guid.NewGuid();
    [EmailAddress, MaxLength(254)] public string? RecipientEmail { get; set; }
    [Required, StringLength(160)] public string Title { get; set; } = "";
    [Required, StringLength(2000)] public string Body { get; set; } = "";
}
