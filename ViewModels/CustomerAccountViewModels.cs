using System.ComponentModel.DataAnnotations;
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
    [Required, StringLength(100)] public string FullName { get; set; } = "";
    [Required, Phone, StringLength(30)] public string Phone { get; set; } = "";
    public string? Email { get; set; }
}
