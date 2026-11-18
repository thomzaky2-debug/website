using System.ComponentModel.DataAnnotations;
using NileShop.Models;
namespace NileShop.ViewModels;
public class CustomerLoginViewModel
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password), StringLength(128)] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
public class CustomerRegisterViewModel : CustomerLoginViewModel
{
    [Required, StringLength(100)] public string FullName { get; set; } = "";
    [Required, Phone, StringLength(30)] public string Phone { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(Password), ErrorMessage="Password_Mismatch")]
    public string ConfirmPassword { get; set; } = "";
}

public class CustomerProfileViewModel
{
    public const string FullNameClaim = "nileshop:full_name";
    public const string AddressClaim = "nileshop:delivery_address";
    [Required, StringLength(100)] public string FullName { get; set; } = "";
    [Required, Phone, StringLength(30)] public string Phone { get; set; } = "";
    [StringLength(1000)] public string? Address { get; set; } = "";
    public string? Email { get; set; }
    public int OrdersCount { get; set; }
    public int SavedCount { get; set; }
    public int ReviewsCount { get; set; }
    public IReadOnlyList<Order> RecentOrders { get; set; } = [];
}

public class CustomerPasswordViewModel
{
    [Required, DataType(DataType.Password), StringLength(128)] public string CurrentPassword { get; set; } = "";
    [Required, DataType(DataType.Password), StringLength(128)] public string NewPassword { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage="Password_Mismatch")]
    public string ConfirmNewPassword { get; set; } = "";
}
