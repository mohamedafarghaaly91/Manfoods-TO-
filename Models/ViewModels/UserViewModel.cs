using System.ComponentModel.DataAnnotations;

namespace MvcApp.Models.ViewModels;

public class UserViewModel
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Role { get; set; } = "";
    public string AssignedName { get; set; } = "";
    public bool HasPassword { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Number of stores this user's email currently matches in the latest
    /// StoreReference upload — null for roles that aren't store-restricted (Admin/User).
    /// Lets an admin catch a typo'd email before the user ever complains everything
    /// is blank.</summary>
    public int? MatchedStoreCount { get; set; }
}

public class CreateUserViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Phone { get; set; } = "";

    /// <summary>Shown across the portal instead of the email (Action Center notes,
    /// the Home-area header) — optional, but strongly recommended for any
    /// store-restricted role.</summary>
    public string? AssignedName { get; set; }

    [Required]
    [MinLength(8, ErrorMessage = "Val_PasswordMin8")]
    public string Password { get; set; } = "";

    [Required]
    [Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = "";

    [Required]
    public string Role { get; set; } = "";
}

public class EditUserViewModel
{
    public int Id { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Phone { get; set; } = "";

    public string? AssignedName { get; set; }

    [MinLength(8, ErrorMessage = "Val_PasswordMin8")]
    public string? Password { get; set; }

    [Compare(nameof(Password))]
    public string? ConfirmPassword { get; set; }

    [Required]
    public string Role { get; set; } = "";
}
