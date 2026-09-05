using System.ComponentModel.DataAnnotations;
using MvcApp.Services;

namespace MvcApp.Models.ViewModels;

/// <summary>
/// At least 12 characters, one uppercase letter, one lowercase letter, one
/// digit, and one symbol — see PasswordPolicy. A null/empty value is treated
/// as valid (skipped) so this composes with [Required] on required fields
/// and stays a no-op on optional ones (e.g. EditUserViewModel.Password,
/// which is only set when an Admin actually chooses to reset it).
/// </summary>
public class StrongPasswordAttribute : ValidationAttribute
{
    public StrongPasswordAttribute() : base("Val_PasswordPolicy") { }

    public override bool IsValid(object? value)
    {
        var password = value as string;
        return string.IsNullOrEmpty(password) || PasswordPolicy.IsValid(password);
    }
}
