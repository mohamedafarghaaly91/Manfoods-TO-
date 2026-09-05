using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class ActionPlanRoleService : IActionPlanRoleService
{
    private readonly AppDbContext _db;
    private readonly IStoreAccessService _storeAccess;

    // Default resolution order when no Admin override exists for a store:
    // Operation_Consultant first, Head_Manager only if that's not assigned.
    private static readonly string[] DefaultRoleOrder = { "Operation_Consultant", "Head_Manager" };

    public ActionPlanRoleService(AppDbContext db, IStoreAccessService storeAccess)
    {
        _db = db;
        _storeAccess = storeAccess;
    }

    private async Task<(int Month, int Year)?> GetLatestPeriodAsync()
    {
        var latest = await _db.StoreReferences
            .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
            .Select(s => new { s.Month, s.Year })
            .FirstOrDefaultAsync();
        return latest == null ? null : (latest.Month, latest.Year);
    }

    private async Task<StoreReference?> GetLatestReferenceAsync(string storeName)
    {
        var latest = await GetLatestPeriodAsync();
        if (latest == null) return null;
        return await _db.StoreReferences.FirstOrDefaultAsync(
            s => s.StoreName == storeName && s.Month == latest.Value.Month && s.Year == latest.Value.Year);
    }

    private List<string> AssignedRoles(StoreReference reference) =>
        _storeAccess.RestrictedRoles
            .Where(r => !string.IsNullOrWhiteSpace(_storeAccess.GetEmailForRole(reference, r)))
            .ToList();

    private static string? DefaultRole(StoreReference reference, List<string> assignedRoles) =>
        DefaultRoleOrder.FirstOrDefault(assignedRoles.Contains);

    public async Task<string?> GetEffectiveRoleAsync(string storeName)
    {
        var reference = await GetLatestReferenceAsync(storeName);
        if (reference == null) return null;

        var assignedRoles = AssignedRoles(reference);
        var overrideRow = await _db.StoreActionPlanRoleAssignments
            .FirstOrDefaultAsync(a => a.StoreName == storeName);
        if (overrideRow != null && assignedRoles.Contains(overrideRow.Role)) return overrideRow.Role;

        return DefaultRole(reference, assignedRoles);
    }

    public async Task<ResponsibleParty?> GetEffectiveResponsiblePartyAsync(string storeName)
    {
        var reference = await GetLatestReferenceAsync(storeName);
        if (reference == null) return null;
        var role = await GetEffectiveRoleAsync(storeName);
        if (role == null) return null;

        return new ResponsibleParty
        {
            Name = _storeAccess.GetNameForRole(reference, role),
            Role = role,
            Email = _storeAccess.GetEmailForRole(reference, role),
        };
    }

    public async Task<Dictionary<string, ResponsibleParty?>> GetEffectiveResponsiblePartiesAsync(List<string> storeNames)
    {
        var result = new Dictionary<string, ResponsibleParty?>(StringComparer.OrdinalIgnoreCase);
        if (storeNames.Count == 0) return result;

        var latest = await GetLatestPeriodAsync();
        if (latest == null)
        {
            foreach (var s in storeNames) result[s] = null;
            return result;
        }

        var refsByStore = (await _db.StoreReferences
                .Where(s => s.Month == latest.Value.Month && s.Year == latest.Value.Year && storeNames.Contains(s.StoreName))
                .ToListAsync())
            .GroupBy(s => s.StoreName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var overridesByStore = (await _db.StoreActionPlanRoleAssignments
                .Where(a => storeNames.Contains(a.StoreName))
                .ToListAsync())
            .ToDictionary(a => a.StoreName, a => a.Role, StringComparer.OrdinalIgnoreCase);

        foreach (var storeName in storeNames)
        {
            if (!refsByStore.TryGetValue(storeName, out var reference)) { result[storeName] = null; continue; }

            var assignedRoles = AssignedRoles(reference);
            string? role = overridesByStore.TryGetValue(storeName, out var overrideRole) && assignedRoles.Contains(overrideRole)
                ? overrideRole
                : DefaultRole(reference, assignedRoles);

            result[storeName] = role == null ? null : new ResponsibleParty
            {
                Name = _storeAccess.GetNameForRole(reference, role),
                Role = role,
                Email = _storeAccess.GetEmailForRole(reference, role),
            };
        }

        return result;
    }

    public async Task<List<ActionPlanRoleRowDto>> GetAllAsync()
    {
        var latest = await GetLatestPeriodAsync();
        if (latest == null) return new List<ActionPlanRoleRowDto>();

        var refs = await _db.StoreReferences
            .Where(s => s.Month == latest.Value.Month && s.Year == latest.Value.Year)
            .OrderBy(s => s.StoreName)
            .ToListAsync();

        var overridesByStore = (await _db.StoreActionPlanRoleAssignments.ToListAsync())
            .ToDictionary(a => a.StoreName, a => a.Role, StringComparer.OrdinalIgnoreCase);

        var result = new List<ActionPlanRoleRowDto>();
        foreach (var reference in refs)
        {
            var assignedRoles = AssignedRoles(reference);
            var hasOverride = overridesByStore.TryGetValue(reference.StoreName, out var overrideRole) && assignedRoles.Contains(overrideRole);
            var effectiveRole = hasOverride ? overrideRole : DefaultRole(reference, assignedRoles);

            result.Add(new ActionPlanRoleRowDto
            {
                StoreName = reference.StoreName,
                EffectiveRole = effectiveRole,
                EffectiveName = effectiveRole == null ? null : _storeAccess.GetNameForRole(reference, effectiveRole),
                IsOverridden = hasOverride,
                AssignableRoles = assignedRoles.Select(r => new ActionPlanRoleAssignableOptionDto
                {
                    Role = r,
                    Name = _storeAccess.GetNameForRole(reference, r),
                }).ToList(),
            });
        }

        return result;
    }

    public async Task<(bool success, string message)> SetOverrideAsync(string storeName, string? role, string setByName)
    {
        var reference = await GetLatestReferenceAsync(storeName);
        if (reference == null) return (false, "Store not found.");

        var existing = await _db.StoreActionPlanRoleAssignments.FirstOrDefaultAsync(a => a.StoreName == storeName);

        if (string.IsNullOrWhiteSpace(role))
        {
            if (existing != null)
            {
                _db.StoreActionPlanRoleAssignments.Remove(existing);
                await _db.SaveChangesAsync();
            }
            return (true, "Reverted to default.");
        }

        if (!AssignedRoles(reference).Contains(role))
            return (false, "That role has no one assigned on this store.");

        if (existing != null)
        {
            existing.Role = role;
            existing.SetByName = setByName;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.StoreActionPlanRoleAssignments.Add(new StoreActionPlanRoleAssignment
            {
                StoreName = storeName,
                Role = role,
                SetByName = setByName,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync();
        return (true, "Saved.");
    }
}
