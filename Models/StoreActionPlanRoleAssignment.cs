using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// Admin override of which role is authorized to manage a store's Action Plan
/// (add notes, toggle recommendation checkboxes, and shown as its Responsible
/// Party there) — independent of StoreAccessService's general store-visibility
/// rules. One row per store; a store with no row here uses the computed
/// default (Operation_Consultant if assigned, else Head_Manager).
/// </summary>
[Table("store_action_plan_role_assignments")]
public class StoreActionPlanRoleAssignment
{
    [Column("id")]
    public int Id { get; set; }

    [Column("store_name")]
    public string StoreName { get; set; } = "";

    // One of the role keys StoreAccessService.RestrictedRoles recognizes
    // (Operation_Manager, Operation_Consultant, Head_Manager,
    // Senior_Operation_Consultant, Operation_Director).
    [Column("role")]
    public string Role { get; set; } = "";

    [Column("set_by_name")]
    public string? SetByName { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
