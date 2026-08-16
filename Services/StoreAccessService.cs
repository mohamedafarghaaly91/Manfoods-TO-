using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class StoreAccessService : IStoreAccessService
{
    private readonly AppDbContext _db;

    public StoreAccessService(AppDbContext db) => _db = db;

    // The one place that knows which StoreReference email column each
    // restricted role is matched against. Adding a future role (once its
    // column exists on StoreReference) is a single line here — no other
    // file needs to change.
    private static readonly Dictionary<string, Expression<Func<StoreReference, string>>> RoleEmailColumns = new()
    {
        ["Operation_Manager"] = s => s.OperationManagerEmail,
        ["Operation_Consultant"] = s => s.OperationConsultantEmail,
        ["Head_Manager"] = s => s.HeadManagerEmail,
        ["Senior_Operation_Consultant"] = s => s.SeniorOperationConsultantEmail,
        ["Operation_Director"] = s => s.OperationDirectorEmail,
    };

    public IReadOnlyList<string> RestrictedRoles { get; } = RoleEmailColumns.Keys.ToList();

    public bool IsRestrictedRole(string role) => RoleEmailColumns.ContainsKey(role);

    public string GetEmailForRole(StoreReference store, string role) =>
        RoleEmailColumns.TryGetValue(role, out var column) ? column.Compile()(store) : "";

    public async Task<List<string>?> GetAccessibleStoreNamesAsync(string role, string? email)
    {
        if (!RoleEmailColumns.TryGetValue(role, out var emailColumn)) return null;

        var normalized = (email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized)) return new List<string>();

        // Access always follows the latest uploaded period, regardless of
        // which month a report happens to be displaying — a rotation takes
        // effect for all historical data the moment the new period lands.
        var latest = await _db.StoreReferences
            .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
            .Select(s => new { s.Month, s.Year })
            .FirstOrDefaultAsync();
        if (latest == null) return new List<string>();

        var q = _db.StoreReferences.Where(s => s.Month == latest.Month && s.Year == latest.Year);
        q = q.Where(BuildEmailEqualsPredicate(emailColumn, normalized));

        return await q.Select(s => s.StoreName).Distinct().ToListAsync();
    }

    private static Expression<Func<StoreReference, bool>> BuildEmailEqualsPredicate(
        Expression<Func<StoreReference, string>> emailColumn, string normalizedEmail)
    {
        var lowerCall = Expression.Call(emailColumn.Body, nameof(string.ToLower), Type.EmptyTypes);
        var equals = Expression.Equal(lowerCall, Expression.Constant(normalizedEmail));
        return Expression.Lambda<Func<StoreReference, bool>>(equals, emailColumn.Parameters);
    }

    public async Task<bool> CanAccessStoreAsync(string role, string? email, string storeName)
    {
        if (!IsRestrictedRole(role)) return true;

        var accessible = await GetAccessibleStoreNamesAsync(role, email);
        return accessible != null && accessible.Any(s => string.Equals(s, storeName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ResponsibleParty?> GetResponsiblePartyAsync(string storeName)
    {
        // Same "latest uploaded period" convention as GetAccessibleStoreNamesAsync,
        // so responsibility and access always agree on which period is current.
        var latest = await _db.StoreReferences
            .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
            .Select(s => new { s.Month, s.Year })
            .FirstOrDefaultAsync();
        if (latest == null) return null;

        var reference = await _db.StoreReferences
            .FirstOrDefaultAsync(s => s.StoreName == storeName && s.Month == latest.Month && s.Year == latest.Year)
            ?? await _db.StoreReferences
                .Where(s => s.StoreName == storeName)
                .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
                .FirstOrDefaultAsync();
        if (reference == null) return null;

        return ResolveResponsible(reference);
    }

    public ResponsibleParty ResolveResponsible(StoreReference reference)
    {
        if (!string.IsNullOrWhiteSpace(reference.HeadManager) || !string.IsNullOrWhiteSpace(reference.HeadManagerEmail))
        {
            return new ResponsibleParty { Name = reference.HeadManager, Role = "Head_Manager", Email = reference.HeadManagerEmail };
        }

        return new ResponsibleParty { Name = reference.OperationConsultant, Role = "Operation_Consultant", Email = reference.OperationConsultantEmail };
    }
}
