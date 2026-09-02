namespace MvcApp.Models.ViewModels;

/// <summary>
/// One row of an admin-configurable rate→color rule set (Settings page).
/// Rules are evaluated in order; UpTo == null means "everything above every
/// other rule's UpTo" and must be the last rule in the list.
/// Color is one of: none, good, warning, warning_strong, bad.
/// </summary>
public class ColorRule
{
    public double? UpTo { get; set; }
    public string Color { get; set; } = "none";
}
