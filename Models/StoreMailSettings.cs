using System.ComponentModel.DataAnnotations;
namespace NileShop.Models;
public class StoreMailSettings
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; }
    [MaxLength(255)] public string Host { get; set; } = "";
    [Range(1, 65535)] public int Port { get; set; } = 587;
    [MaxLength(255)] public string Username { get; set; } = "";
    public string EncryptedPassword { get; set; } = "";
    [EmailAddress, MaxLength(254)] public string From { get; set; } = "";
    public bool EnableSsl { get; set; } = true;
}
