using System.ComponentModel.DataAnnotations;

namespace NileShop.ViewModels;

public sealed class ForgotPasswordViewModel
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";
}

public sealed class ResetPasswordViewModel
{
    [Required, StringLength(450)]
    public string UserId { get; set; } = "";

    [Required, StringLength(2048)]
    public string Token { get; set; } = "";

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; set; } = "";

    [Required, StringLength(128), Compare(nameof(Password), ErrorMessage = "Password_Mismatch")]
    public string ConfirmPassword { get; set; } = "";
}
