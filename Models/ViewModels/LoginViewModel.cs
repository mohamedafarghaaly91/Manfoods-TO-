using System.ComponentModel.DataAnnotations;

// ErrorMessage values below are resx keys, not literal text: Program.cs wires
// AddDataAnnotationsLocalization to resolve them against Resources/SharedResource.

namespace MvcApp.Models.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Val_EmailRequired")]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Val_PasswordRequired")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";
}

public class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Val_CurrentPasswordRequired")]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "Val_NewPasswordRequired")]
    [StrongPassword]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Val_ConfirmNewPassword")]
    [DataType(DataType.Password)]
    [Compare("NewPassword", ErrorMessage = "Val_PasswordsDoNotMatch")]
    public string ConfirmPassword { get; set; } = "";
}

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Val_PhoneOrEmailRequired")]
    public string Identifier { get; set; } = "";

    [Required(ErrorMessage = "Val_OtpRequired")]
    public string OtpCode { get; set; } = "";

    [Required(ErrorMessage = "Val_NewPasswordRequired")]
    [StrongPassword]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Val_ConfirmNewPassword")]
    [DataType(DataType.Password)]
    [Compare("NewPassword", ErrorMessage = "Val_PasswordsDoNotMatch")]
    public string ConfirmPassword { get; set; } = "";
}

public class AdminRecoveryViewModel
{
    [Required(ErrorMessage = "Val_AdminEmailRequired")]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Val_RecoveryKeyRequired")]
    public string RecoveryKey { get; set; } = "";

    [Required(ErrorMessage = "Val_NewPasswordRequired")]
    [StrongPassword]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Val_ConfirmNewPassword")]
    [DataType(DataType.Password)]
    [Compare("NewPassword", ErrorMessage = "Val_PasswordsDoNotMatch")]
    public string ConfirmPassword { get; set; } = "";
}
