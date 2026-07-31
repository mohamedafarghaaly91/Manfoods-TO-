using MvcApp.Models;

namespace MvcApp.Services;

/// <summary>
/// Single source of truth for "which stores can this user see." Backed
/// entirely by the latest uploaded Store Reference period — there is no
/// separate user-store assignment table, so a monthly re-upload changes
/// access automatically.
/// </summary>
public interface IStoreAccessService
{
    /// <summary>True for roles that are restricted to specific stores (as opposed to Admin/User, which see everything).</summary>
    bool IsRestrictedRole(string role);

    /// <summary>Role keys currently mapped to a StoreReference email column, in a stable display order.</summary>
    IReadOnlyList<string> RestrictedRoles { get; }

    /// <summary>
    /// Store names the given role/email currently has access to, based on the
    /// latest (Month, Year) present in StoreReference. Null means unrestricted
    /// (Admin/User or any role not in the access map).
    /// </summary>
    Task<List<string>?> GetAccessibleStoreNamesAsync(string role, string? email);

    /// <summary>The email value on a StoreReference row for the given restricted role (e.g. for upload validation).</summary>
    string GetEmailForRole(StoreReference store, string role);
}
