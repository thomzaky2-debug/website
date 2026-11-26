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
    [Required, RegularExpression("^(en|ar)$")] public string PreferredLanguage { get; set; } = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    [Required, StringLength(100)] public string FullName { get; set; } = "";
    [Required, Phone, StringLength(30)] public string Phone { get; set; } = "";
    // Password is optional when phone-first signup is active (WhatsApp OTP configured);
    // the account can add one later from profile settings.
    [DataType(DataType.Password), StringLength(128)] public new string? Password { get; set; }
    [DataType(DataType.Password), StringLength(128), Compare(nameof(Password), ErrorMessage="Password_Mismatch")]
    public string? ConfirmPassword { get; set; }
}

public class CustomerProfileViewModel
{
    public const string FullNameClaim = "nileshop:full_name";
    public const string AddressClaim = "nileshop:delivery_address";
    public const string WhatsAppClaim = "nileshop:whatsapp_phone";
    [Required, StringLength(100)] public string FullName { get; set; } = "";
    [Required, Phone, StringLength(30)] public string Phone { get; set; } = "";
    [Phone, StringLength(30)] public string? WhatsAppPhone { get; set; }
    [StringLength(1000)] public string? Address { get; set; } = "";
    public string? Email { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool HasPassword { get; set; }
    public int OrdersCount { get; set; }
    public int SavedCount { get; set; }
    public int ReviewsCount { get; set; }
    public decimal WalletBalance { get; set; }
    public IReadOnlyList<Order> RecentOrders { get; set; } = [];
}

public class CustomerWalletViewModel
{
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public decimal Balance { get; set; }
    public IReadOnlyList<WalletTransaction> Transactions { get; set; } = [];
}

public class CustomerAddPasswordViewModel
{
    [Required, DataType(DataType.Password), StringLength(128)] public string NewPassword { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage="Password_Mismatch")]
    public string ConfirmNewPassword { get; set; } = "";
}

public class CustomerPasswordViewModel
{
    [Required, DataType(DataType.Password), StringLength(128)] public string CurrentPassword { get; set; } = "";
    [Required, DataType(DataType.Password), StringLength(128)] public string NewPassword { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage="Password_Mismatch")]
    public string ConfirmNewPassword { get; set; } = "";
}

public class CustomerPhoneLoginViewModel
{
    [Required, Phone, StringLength(30)] public string Phone { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
