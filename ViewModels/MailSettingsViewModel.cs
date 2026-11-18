using System.ComponentModel.DataAnnotations;
namespace NileShop.ViewModels;
public class MailSettingsViewModel
{
    public bool Enabled { get; set; }
    [MaxLength(255)] public string? Host { get; set; } = "";
    [Range(1,65535)] public int Port { get; set; } = 587;
    [MaxLength(255)] public string? Username { get; set; } = "";
    public string? NewPassword { get; set; }
    public bool HasPassword { get; set; }
    [EmailAddress, MaxLength(254)] public string? From { get; set; } = "";
    public bool EnableSsl { get; set; } = true;
}
