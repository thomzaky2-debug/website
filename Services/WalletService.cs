using Microsoft.EntityFrameworkCore;
using NileShop.Data;
using NileShop.Models;
namespace NileShop.Services;

public class WalletService(ApplicationDbContext db)
{
    public async Task<decimal> GetBalanceAsync(string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return 0m;
        var sum = db.Database.IsSqlite()
            ? (decimal)await db.WalletTransactions.AsNoTracking()
                .Where(w => w.UserId == userId)
                .SumAsync(w => (double)w.Amount, ct)
            : await db.WalletTransactions.AsNoTracking()
                .Where(w => w.UserId == userId)
                .SumAsync(w => (decimal?)w.Amount, ct) ?? 0m;
        return Math.Max(0m, decimal.Round(sum, 2));
    }

    public async Task<IReadOnlyList<WalletTransaction>> GetTransactionsAsync(string userId, int take = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return [];
        return await db.WalletTransactions.AsNoTracking()
            .Include(w => w.Order)
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<bool> RecordIdempotentAsync(
        string userId,
        decimal amount,
        string kind,
        string referenceKey,
        int? orderId,
        string description,
        CancellationToken ct = default)
    {
        var rounded = decimal.Round(amount, 2);
        if (rounded == 0m || string.IsNullOrWhiteSpace(userId)) return false;
        if (await db.WalletTransactions.AnyAsync(w => w.ReferenceKey == referenceKey, ct))
            return false;

        db.WalletTransactions.Add(new WalletTransaction
        {
            UserId = userId,
            OrderId = orderId,
            Amount = rounded,
            Kind = kind,
            ReferenceKey = referenceKey,
            Description = description.Length > 300 ? description[..300] : description
        });
        return true;
    }
}

