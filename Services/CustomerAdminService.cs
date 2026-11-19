using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NileShop.Data;
using NileShop.Models;
using NileShop.ViewModels;
namespace NileShop.Services;

public class CustomerAdminService(ApplicationDbContext db)
{
    public IQueryable<IdentityUser> Customers() => db.Users.AsNoTracking().Where(user =>
        !db.UserRoles.Any(role => role.UserId == user.Id && db.Roles.Any(r => r.Id == role.RoleId && r.Name == "Admin")));

    public async Task<IReadOnlyList<CustomerAdminRow>> RowsAsync(IReadOnlyList<IdentityUser> users)
    {
        if (users.Count == 0) return [];
        var ids = users.Select(user => user.Id).ToArray();
        var types = new[] { CustomerProfileViewModel.FullNameClaim, CustomerProfileViewModel.WhatsAppClaim,
            CustomerProfileViewModel.AddressClaim, LanguagePreferences.ClaimType(false) };
        var claims = await db.UserClaims.AsNoTracking().Where(claim => ids.Contains(claim.UserId) && types.Contains(claim.ClaimType!))
            .OrderByDescending(claim => claim.Id).Select(claim => new { claim.UserId, claim.ClaimType, claim.ClaimValue }).ToListAsync();
        var statistics = await db.Orders.AsNoTracking().Where(order => order.CustomerUserId != null && ids.Contains(order.CustomerUserId))
            .GroupBy(order => order.CustomerUserId!).Select(group => new {
                UserId = group.Key, Orders = group.Count(),
                PaidOrders = group.Count(order => (order.PaymentStatus == "Paid" || order.PaymentStatus == "Overpaid") && order.Status != OrderStatuses.Cancelled && !order.NeedsPaymentReview),
                LastOrderAt = group.Max(order => order.CreatedAt)
            }).ToDictionaryAsync(row => row.UserId);
        var paid = db.Orders.AsNoTracking().Where(order => order.CustomerUserId != null && ids.Contains(order.CustomerUserId)
            && (order.PaymentStatus == "Paid" || order.PaymentStatus == "Overpaid") && order.Status != OrderStatuses.Cancelled && !order.NeedsPaymentReview);
        Dictionary<string, decimal> totals;
        if (db.Database.IsSqlite()) {
            var values = await paid.GroupBy(order => order.CustomerUserId!).Select(group => new { UserId = group.Key, Total = group.Sum(order => (double)order.TotalAmount) }).ToListAsync();
            totals = values.ToDictionary(row => row.UserId, row => decimal.Round((decimal)row.Total, 2));
        } else totals = await paid.GroupBy(order => order.CustomerUserId!).Select(group => new { UserId = group.Key, Total = group.Sum(order => order.TotalAmount) }).ToDictionaryAsync(row => row.UserId, row => row.Total);
        var now = DateTimeOffset.UtcNow;
        return users.Select(user => {
            string Value(string type) => claims.FirstOrDefault(claim => claim.UserId == user.Id && claim.ClaimType == type)?.ClaimValue ?? "";
            var stats = statistics.GetValueOrDefault(user.Id);
            return new CustomerAdminRow(user.Id, user.Email ?? "", user.PhoneNumber ?? "", Value(CustomerProfileViewModel.FullNameClaim),
                Value(CustomerProfileViewModel.WhatsAppClaim), Value(CustomerProfileViewModel.AddressClaim), Value(LanguagePreferences.ClaimType(false)),
                user.EmailConfirmed, user.LockoutEnabled && user.LockoutEnd > now, user.LockoutEnd,
                stats?.Orders ?? 0, stats?.PaidOrders ?? 0, totals.GetValueOrDefault(user.Id), stats?.LastOrderAt);
        }).ToList();
    }
}
