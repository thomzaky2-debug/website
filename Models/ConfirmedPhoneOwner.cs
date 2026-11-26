namespace NileShop.Models;

// Atomic ownership registry for confirmed phones: the primary key on
// NormalizedPhone is enforced by the database itself, so two concurrent
// confirmations of the same number cannot both succeed across instances.
public class ConfirmedPhoneOwner
{
    public string NormalizedPhone { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTime ClaimedAtUtc { get; set; } = DateTime.UtcNow;
}
