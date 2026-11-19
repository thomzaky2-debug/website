using NileShop.Models;
namespace NileShop.ViewModels;
public class OperationsViewModel
{
    public IReadOnlyList<Order> Orders { get; set; } = [];
    public IReadOnlyList<Product> LowStock { get; set; } = [];
    public int PendingEmails { get; set; }
    public int FailedEmails { get; set; }
    public bool EmailConfigured { get; set; }
    public string? Search { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
}
public record SalesDay(DateTime Date, int Orders, decimal Collected);
public record ReportViewModel(DateTime From, DateTime To, IReadOnlyList<SalesDay> Days, decimal Refunds);
public record CustomerAdminRow(string Id, string Email, string Phone, string Name, string WhatsApp,
    string Address, string PreferredLanguage, bool EmailConfirmed, bool IsLocked, DateTimeOffset? LockoutEnd,
    int Orders, int PaidOrders, decimal PaidOrderValue, DateTime? LastOrderAt);
public record CustomersAdminViewModel(IReadOnlyList<CustomerAdminRow> Customers, string? Search, int Page, int Pages, int TotalMatches, string Segment);
public record CustomerDetailAdminViewModel(CustomerAdminRow Customer, decimal WalletBalance, int SavedCount, int ReviewCount,
    IReadOnlyList<Order> Orders, int Page, int Pages, IReadOnlyList<WalletTransaction> WalletTransactions, IReadOnlyList<ProductReview> Reviews);
