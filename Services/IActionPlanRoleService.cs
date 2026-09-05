using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

/// <summary>
/// Who is authorized to manage a store's Action Plan — add notes, toggle
/// recommendation checkboxes, and be shown as its Responsible Party there.
/// Default is Operation_Consultant if assigned on the store's latest
/// StoreReference row, else Head_Manager; an Admin can override that default
/// per store (e.g. delegate to a Senior Operation Consultant while the
/// Operation Consultant is on leave) via the Action Plan Role settings page.
///
/// This is deliberately separate from IStoreAccessService: that service
/// answers "which stores can this user see" everywhere in the app, and is
/// never touched by this one. This service only answers "who currently owns
/// this store's Action Plan" — nothing else.
/// </summary>
public interface IActionPlanRoleService
{
    /// <summary>Every store from the latest StoreReference period, with its
    /// currently effective Action Plan role, whether that's an Admin
    /// override or the computed default, and which roles are assignable
    /// (i.e. actually have someone assigned) for that store.</summary>
    Task<List<ActionPlanRoleRowDto>> GetAllAsync();

    /// <summary>The role key currently authorized to manage this store's
    /// Action Plan, or null if neither Operation_Consultant nor Head_Manager
    /// is assigned and no valid override exists.</summary>
    Task<string?> GetEffectiveRoleAsync(string storeName);

    /// <summary>Same resolution as GetEffectiveRoleAsync, resolved to a full
    /// ResponsibleParty (name/role/email) for display.</summary>
    Task<ResponsibleParty?> GetEffectiveResponsiblePartyAsync(string storeName);

    /// <summary>Batch form of GetEffectiveResponsiblePartyAsync for listing
    /// many stores at once without a query per store.</summary>
    Task<Dictionary<string, ResponsibleParty?>> GetEffectiveResponsiblePartiesAsync(List<string> storeNames);

    /// <summary>Admin-only: sets the store's Action Plan role override.
    /// Passing null/blank clears the override, reverting the store to its
    /// computed default. Fails if the role has no one assigned on the
    /// store's latest StoreReference row.</summary>
    Task<(bool success, string message)> SetOverrideAsync(string storeName, string? role, string setByName);
}
