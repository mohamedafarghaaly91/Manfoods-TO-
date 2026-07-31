using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

public class StoreService : IStoreService
{
    private readonly AppDbContext _db;

    public StoreService(AppDbContext db) => _db = db;

    public async Task<List<StoreReference>> GetStoresAsync(int? month, int? year, string role, string? assignedName)
    {
        var q = _db.StoreReferences.AsQueryable();
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (year.HasValue) q = q.Where(s => s.Year == year);

        // For Operation Manager/Consultant accounts, "assignedName" here is
        // actually the logged-in user's email (see HttpContext.Session.GetEmail()
        // at call sites) — matched against the store's OM/OC email column so the
        // visible list follows the latest monthly upload with no manual re-linking.
        var email = (assignedName ?? "").Trim().ToLowerInvariant();
        if (role == "Operation_Manager")
            q = string.IsNullOrEmpty(email) ? q.Where(s => false) : q.Where(s => s.OperationManagerEmail.ToLower() == email);
        else if (role == "Operation_Consultant")
            q = string.IsNullOrEmpty(email) ? q.Where(s => false) : q.Where(s => s.OperationConsultantEmail.ToLower() == email);

        return await q.OrderBy(s => s.StoreName).ToListAsync();
    }
}
