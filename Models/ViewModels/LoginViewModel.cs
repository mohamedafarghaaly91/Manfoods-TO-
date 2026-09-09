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
    // Not [Required] here — a forced first-login password change (temporary/
    // OTP-issued password) skips this field entirely. The controller enforces
    // it as required for a voluntary change instead, where the session's
    // MustChangePassword flag says which case this is.
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

/// <summary>Posted (as a fetch/AJAX call, not a page form) from the
/// Update Password popup shown directly on the login page for an account
/// still on a system-generated temporary password — see
/// AccountController.SetupPassword in both Home and Admin areas. Token comes
/// from PasswordSetupExtensions.BeginPasswordSetup, issued right after the
/// temporary-password login check succeeds; deliberately never establishes a
/// session, so the user still has to sign in fresh afterward.</summary>
public class SetupPasswordViewModel
{
    [Required]
    public string Token { get; set; } = "";

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
