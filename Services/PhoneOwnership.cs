using Microsoft.EntityFrameworkCore;
using NileShop.Data;
using NileShop.Models;

namespace NileShop.Services;

// Confirmed-phone ownership, enforced atomically by the ConfirmedPhoneOwners
// primary key. Claim = INSERT (a duplicate key means another account owns the
// number; check-then-write is never used), release = DELETE by owner.
public static class PhoneOwnership
{
    public static async Task<bool> TryClaimAsync(ApplicationDbContext db, string userId, string normalizedPhone, CancellationToken ct = default)
    {
        var existing = await db.ConfirmedPhoneOwners.AsNoTracking()
            .FirstOrDefaultAsync(x => x.NormalizedPhone == normalizedPhone, ct);
        if (existing is not null) return existing.UserId == userId;
        var row = new ConfirmedPhoneOwner { NormalizedPhone = normalizedPhone, UserId = userId, ClaimedAtUtc = DateTime.UtcNow };
        db.ConfirmedPhoneOwners.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Lost a concurrent claim race: the database rejected the duplicate key.
            db.Entry(row).State = EntityState.Detached;
            var winner = await db.ConfirmedPhoneOwners.AsNoTracking()
                .FirstOrDefaultAsync(x => x.NormalizedPhone == normalizedPhone, ct);
            return winner is not null && winner.UserId == userId;
        }
    }

    public static async Task ReleaseForUserAsync(ApplicationDbContext db, string userId, CancellationToken ct = default)
    {
        await db.ConfirmedPhoneOwners.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
    }

    public static async Task<ConfirmedPhoneOwner?> GetOwnerAsync(ApplicationDbContext db, string normalizedPhone, CancellationToken ct = default)
    {
        return await db.ConfirmedPhoneOwners.AsNoTracking()
            .FirstOrDefaultAsync(x => x.NormalizedPhone == normalizedPhone, ct);
    }
}
