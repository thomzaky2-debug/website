using System.ComponentModel.DataAnnotations;
namespace NileShop.Models;

public class EmailRateLimit
{
    public int Id { get; set; }
    [Required, MaxLength(256)] public string NormalizedEmail { get; set; } = "";
    public DateTime WindowStartUtc { get; set; } = DateTime.UtcNow;
    public int HourCount { get; set; }
    public DateTime LastSentUtc { get; set; } = DateTime.UtcNow;
}

public class AccountEmailQueue
{
    public int Id { get; set; }
    [Required, MaxLength(256)] public string RecipientEmail { get; set; } = "";
    [Required, MaxLength(200)] public string Subject { get; set; } = "";
    [Required, MaxLength(4000)] public string Body { get; set; } = "";
    [MaxLength(128)] public string? ChallengeKey { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
    [MaxLength(200)] public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int PayloadVersion { get; set; } = 0;
    public bool Quarantined { get; set; } = false;
}
