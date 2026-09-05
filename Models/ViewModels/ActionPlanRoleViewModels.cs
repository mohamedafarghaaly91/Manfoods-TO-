namespace MvcApp.Models.ViewModels;

/// <summary>One role option that can be assigned as a store's Action Plan
/// owner — only roles that actually have someone assigned on the store's
/// latest StoreReference row are ever offered.</summary>
public class ActionPlanRoleAssignableOptionDto
{
    public string Role { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>One store row for the Action Plan Role settings page.</summary>
public class ActionPlanRoleRowDto
{
    public string StoreName { get; set; } = "";
    public string? EffectiveRole { get; set; }
    public string? EffectiveName { get; set; }
    public bool IsOverridden { get; set; }
    public List<ActionPlanRoleAssignableOptionDto> AssignableRoles { get; set; } = new();
}
