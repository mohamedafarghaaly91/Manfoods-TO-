using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IStoreAccessService _storeAccess;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public DashboardService(AppDbContext db, IMemoryCache cache, IStoreAccessService storeAccess)
    {
        _db = db;
        _cache = cache;
        _storeAccess = storeAccess;
    }

    // Returns the store names a restricted role (Operation Manager/Consultant,
    // Head Manager, ...) is limited to, or null for unrestricted access
    // (Admin/User). "assignedName" here is actually the logged-in user's email
    // (see HttpContext.Session.GetEmail() at call sites). Delegates to
    // IStoreAccessService, the single source of truth — always resolved
    // against the latest uploaded period regardless of which month/year this
    // particular call is displaying, so callers keep passing month/year
    // unchanged (kept for signature compatibility with ~18 call sites) even
    // though the access check itself no longer scopes by them.
    private Task<List<string>?> GetAccessibleStoresAsync(string role, string? assignedName, int? month, int? year) =>
        _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);

    // Expands a from/to month-year range (inclusive) into "YYYYMM" sortable int keys.
    internal static List<int> ExpandRangeKeys(int fromMonth, int fromYear, int toMonth, int toYear)
    {
        var start = new DateTime(fromYear, fromMonth, 1);
        var end = new DateTime(toYear, toMonth, 1);
        if (end < start) (start, end) = (end, start);
        var keys = new List<int>();
        for (var d = start; d <= end; d = d.AddMonths(1))
            keys.Add(d.Year * 100 + d.Month);
        return keys;
    }

    // Resolves the explicit set of (month, year) periods a request should aggregate over.
    // When "months" (a CSV of month numbers, e.g. "1,3,5") is given together with "year", those
    // discrete months are used as-is (no requirement that they be contiguous). Otherwise falls
    // back to the legacy contiguous from/to range behavior.
    internal static List<(int Month, int Year)> ResolvePeriods(int? month, int? year, int? fromMonth, int? fromYear, string? months)
    {
        if (year.HasValue && !string.IsNullOrWhiteSpace(months))
        {
            var parsed = months.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var m) ? m : (int?)null)
                .Where(m => m.HasValue && m.Value is >= 1 and <= 12)
                .Select(m => m!.Value)
                .Distinct()
                .OrderBy(m => m)
                .Select(m => (Month: m, Year: year.Value))
                .ToList();
            if (parsed.Count > 0) return parsed;
        }

        var toMonth = month ?? DateTime.Now.Month;
        var toYear  = year  ?? DateTime.Now.Year;
        return ExpandRangeKeys(fromMonth ?? toMonth, fromYear ?? toYear, toMonth, toYear)
            .Select(k => (Month: k % 100, Year: k / 100)).ToList();
    }

    // New Hires: an ActiveEmployees row counts as a new hire for its OWN (Month, Year)
    // snapshot only when that same row's HireDate falls in that exact month — strictly
    // HireDate-driven, never a roster diff against the prior month's active list. Scoping
    // to the row's own snapshot period (rather than just "HireDate falls somewhere in the
    // selected range") also prevents the same employee being counted once per monthly
    // snapshot they still appear in when a multi-month range is selected.
    private IQueryable<ActiveEmployee> NewHiresQuery(IEnumerable<int> periodKeys)
    {
        var keys = periodKeys as ICollection<int> ?? periodKeys.ToList();
        return _db.ActiveEmployees.Where(e => e.HireDate != null
            && keys.Contains(e.Year * 100 + e.Month)
            && (e.HireDate!.Value.Year * 100 + e.HireDate!.Value.Month) == (e.Year * 100 + e.Month));
    }

    // Stores whose Operation Manager / Operation Consultant (as of the given period) match the filter.
    // Returns null when no OM/OC filter is set (caller should skip the store-list filter entirely).
    private async Task<List<string>?> GetStoresForOmOcAsync(int month, int year, string? om, string? oc, string? soc = null, string? od = null)
    {
        if (string.IsNullOrEmpty(om) && string.IsNullOrEmpty(oc) && string.IsNullOrEmpty(soc) && string.IsNullOrEmpty(od)) return null;
        var q = _db.StoreReferences.Where(s => s.Month == month && s.Year == year);
        if (!string.IsNullOrEmpty(om)) q = q.Where(s => s.OperationManager == om);
        if (!string.IsNullOrEmpty(oc)) q = q.Where(s => s.OperationConsultant == oc);
        if (!string.IsNullOrEmpty(soc)) q = q.Where(s => s.SeniorOperationConsultant == soc);
        if (!string.IsNullOrEmpty(od)) q = q.Where(s => s.OperationDirector == od);
        return await q.Select(s => s.StoreName).Distinct().ToListAsync();
    }

    // Fetches only the row(s) that could be "the latest period" for each store —
    // i.e. rows whose (Year, Month) matches that store's own max — instead of
    // pulling every historical period for every store over the wire. Ties (more
    // than one row sharing a store's max period) are resolved by the caller with
    // the exact same OrderByDescending(Year).ThenByDescending(Month).First() rule
    // used before this optimization, so the result is identical either way.
    private async Task<List<StoreReference>> LoadLatestStoreReferenceCandidatesAsync() =>
        await _db.StoreReferences
            .Where(s => s.Year * 100 + s.Month == _db.StoreReferences
                .Where(x => x.StoreName == s.StoreName)
                .Max(x => x.Year * 100 + x.Month))
            .ToListAsync();

    public async Task<List<string>> GetOperationManagersAsync(int? month, int? year, string role, string? assignedName)
    {
        var q = _db.StoreReferences.AsQueryable();
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (year.HasValue) q = q.Where(s => s.Year == year);
        // Restricted roles must never see OM names outside their own accessible
        // stores in the filter dropdown, regardless of month/year selected.
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        if (accessible != null) q = q.Where(s => accessible.Contains(s.StoreName));
        return await q.Where(s => s.OperationManager != "")
            .Select(s => s.OperationManager).Distinct().OrderBy(s => s).ToListAsync();
    }

    public async Task<List<string>> GetOperationConsultantsAsync(int? month, int? year, string role, string? assignedName)
    {
        var q = _db.StoreReferences.AsQueryable();
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (year.HasValue) q = q.Where(s => s.Year == year);
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        if (accessible != null) q = q.Where(s => accessible.Contains(s.StoreName));
        return await q.Where(s => s.OperationConsultant != "")
            .Select(s => s.OperationConsultant).Distinct().OrderBy(s => s).ToListAsync();
    }

    public async Task<List<string>> GetSeniorOperationConsultantsAsync(int? month, int? year, string role, string? assignedName)
    {
        var q = _db.StoreReferences.AsQueryable();
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (year.HasValue) q = q.Where(s => s.Year == year);
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        if (accessible != null) q = q.Where(s => accessible.Contains(s.StoreName));
        return await q.Where(s => s.SeniorOperationConsultant != "")
            .Select(s => s.SeniorOperationConsultant).Distinct().OrderBy(s => s).ToListAsync();
    }

    public async Task<List<string>> GetOperationDirectorsAsync(int? month, int? year, string role, string? assignedName)
    {
        var q = _db.StoreReferences.AsQueryable();
        if (month.HasValue) q = q.Where(s => s.Month == month);
        if (year.HasValue) q = q.Where(s => s.Year == year);
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        if (accessible != null) q = q.Where(s => accessible.Contains(s.StoreName));
        return await q.Where(s => s.OperationDirector != "")
            .Select(s => s.OperationDirector).Distinct().OrderBy(s => s).ToListAsync();
    }

    public async Task<DashboardKpiViewModel> GetKpisAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        if (!month.HasValue || !year.HasValue)
        {
            var latest = await _db.ActiveEmployees
                .OrderByDescending(e => e.Year).ThenByDescending(e => e.Month)
                .Select(e => new { e.Month, e.Year })
                .FirstOrDefaultAsync();
            month ??= latest?.Month ?? DateTime.Now.Month;
            year ??= latest?.Year ?? DateTime.Now.Year;
        }
        fromMonth ??= month; fromYear ??= year;

        var cacheKey = $"kpi_{fromMonth}_{fromYear}_{month}_{year}_{store}_{om}_{oc}_{soc}_{od}_{months}_{role}_{assignedName}";
        if (_cache.TryGetValue(cacheKey, out DashboardKpiViewModel? cached) && cached != null)
            return cached;

        var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        var anchor  = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();

        var accessible = await GetAccessibleStoresAsync(role, assignedName, anchor.Month, anchor.Year);
        var omOcStores = await GetStoresForOmOcAsync(anchor.Month, anchor.Year, om, oc, soc, od);
        var stores = MultiValueFilter.Split(store);

        var headcountsPerPeriod = new List<int>();
        var toHeadcount = 0;
        var totalResignations = 0;

        foreach (var p in periods)
        {
            // Role-based store access is ALWAYS applied (never bypassed by an
            // explicit store/om/oc selection) — the explicit filter, when present,
            // narrows further on top of it. Final population = accessible ∩ explicit.
            // Headcount is a snapshot: the active-employee count for this exact period.
            var empQ = _db.ActiveEmployees.Where(e => e.Month == p.Month && e.Year == p.Year);
            if (accessible != null) empQ = empQ.Where(e => accessible.Contains(e.Store));
            if (stores != null) empQ = empQ.Where(e => stores.Contains(e.Store));
            else if (omOcStores != null) empQ = empQ.Where(e => omOcStores.Contains(e.Store));

            var hc = await empQ.CountAsync();
            headcountsPerPeriod.Add(hc);
            if (p.Month == anchor.Month && p.Year == anchor.Year) toHeadcount = hc;

            var resQ = _db.Resignations.Where(r => r.Month == p.Month && r.Year == p.Year);
            if (accessible != null) resQ = resQ.Where(r => accessible.Contains(r.Store));
            if (stores != null) resQ = resQ.Where(r => stores.Contains(r.Store));
            else if (omOcStores != null) resQ = resQ.Where(r => omOcStores.Contains(r.Store));
            totalResignations += await resQ.CountAsync();
        }

        // New Hires: strictly HireDate-driven across the resolved periods — no
        // roster-diffing against the prior month's active list.
        var periodKeys = periods.Select(p => p.Year * 100 + p.Month).ToList();
        var newHireQ = NewHiresQuery(periodKeys);
        if (accessible != null) newHireQ = newHireQ.Where(e => accessible.Contains(e.Store));
        if (stores != null) newHireQ = newHireQ.Where(e => stores.Contains(e.Store));
        else if (omOcStores != null) newHireQ = newHireQ.Where(e => omOcStores.Contains(e.Store));
        var totalNewHires = await newHireQ.CountAsync();

        var avgHeadcount = headcountsPerPeriod.Count > 0 ? headcountsPerPeriod.Average() : 0;
        var turnoverRate = MetricsCalculationService.RatePercent(totalResignations, avgHeadcount, 2);

        var result = new DashboardKpiViewModel
        {
            TotalHeadcount = toHeadcount,
            NewHires = totalNewHires,
            TotalResignations = totalResignations,
            TurnoverRate = turnoverRate,
            Month = anchor.Month,
            Year = anchor.Year
        };

        _cache.Set(cacheKey, result, CacheDuration);
        return result;
    }

    public async Task<List<ChartDataItem>> GetTurnoverByJobTitleAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var q = _db.Resignations.AsQueryable();
        (int Month, int Year)? anchor = null;
        if (month.HasValue && year.HasValue)
        {
            var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
            var keys = periods.Select(p => p.Year * 100 + p.Month).ToList();
            anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
            q = q.Where(r => keys.Contains(r.Year * 100 + r.Month));
        }
        if (accessible != null) q = q.Where(r => accessible.Contains(r.Store));
        if (MultiValueFilter.Split(store) is { } stores) q = q.Where(r => stores.Contains(r.Store));
        else if (anchor is { } a && await GetStoresForOmOcAsync(a.Month, a.Year, om, oc, soc, od) is { } omOcStores)
            q = q.Where(r => omOcStores.Contains(r.Store));

        return await q.GroupBy(r => r.JobTitle)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .ToListAsync();
    }

    public async Task<List<ChartDataItem>> GetTurnoverByPayrollGroupAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var q = _db.Resignations.AsQueryable();
        (int Month, int Year)? anchor = null;
        if (month.HasValue && year.HasValue)
        {
            var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
            var keys = periods.Select(p => p.Year * 100 + p.Month).ToList();
            anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
            q = q.Where(r => keys.Contains(r.Year * 100 + r.Month));
        }
        if (accessible != null) q = q.Where(r => accessible.Contains(r.Store));
        if (MultiValueFilter.Split(store) is { } stores) q = q.Where(r => stores.Contains(r.Store));
        else if (anchor is { } a && await GetStoresForOmOcAsync(a.Month, a.Year, om, oc, soc, od) is { } omOcStores)
            q = q.Where(r => omOcStores.Contains(r.Store));

        return await q.Where(r => r.PayrollGroup != "")
            .GroupBy(r => r.PayrollGroup)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .ToListAsync();
    }

    public async Task<List<ChartDataItem>> GetTurnoverByTenureAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var q = _db.Resignations.AsQueryable();
        (int Month, int Year)? anchor = null;
        if (month.HasValue && year.HasValue)
        {
            var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
            var keys = periods.Select(p => p.Year * 100 + p.Month).ToList();
            anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
            q = q.Where(r => keys.Contains(r.Year * 100 + r.Month));
        }
        if (accessible != null) q = q.Where(r => accessible.Contains(r.Store));
        if (MultiValueFilter.Split(store) is { } stores) q = q.Where(r => stores.Contains(r.Store));
        else if (anchor is { } a && await GetStoresForOmOcAsync(a.Month, a.Year, om, oc, soc, od) is { } omOcStores)
            q = q.Where(r => omOcStores.Contains(r.Store));

        var rows = await q.Select(r => new { r.HireDate, r.ResignationDate }).ToListAsync();

        var buckets = new Dictionary<string, int> { ["<3m"] = 0, ["3-6m"] = 0, ["6-12m"] = 0, [">1y"] = 0 };
        foreach (var r in rows)
        {
            if (!r.HireDate.HasValue) { buckets[">1y"]++; continue; }
            var hire = r.HireDate.Value.ToDateTime(TimeOnly.MinValue);
            var resign = r.ResignationDate.HasValue ? r.ResignationDate.Value.ToDateTime(TimeOnly.MinValue) : DateTime.Now;
            var tenureMonths = (resign.Year - hire.Year) * 12 + (resign.Month - hire.Month);
            if (tenureMonths < 3) buckets["<3m"]++;
            else if (tenureMonths < 6) buckets["3-6m"]++;
            else if (tenureMonths < 12) buckets["6-12m"]++;
            else buckets[">1y"]++;
        }

        return buckets.Select(kv => new ChartDataItem { Label = kv.Key, Value = kv.Value }).ToList();
    }

    // Averages a per-category active-employee count across the resolved periods,
    // instead of a single snapshot month — so a multi-month range on Turnover/
    // Workforce doesn't silently collapse the Gender/Headcount composition
    // charts down to the latest month while every other chart on the page
    // honors the full range. Averaging (rather than summing) avoids double-
    // counting the same still-employed person across multiple monthly snapshots.
    private async Task<List<ChartDataItem>> AverageBreakdownAsync(
        List<(int Month, int Year)> periods, List<string>? accessible, List<string>? stores, List<string>? omOcStores,
        Expression<Func<ActiveEmployee, string>> keySelector, bool excludeEmptyKey)
    {
        var totals = new Dictionary<string, int>();
        foreach (var p in periods)
        {
            var q = _db.ActiveEmployees.Where(e => e.Month == p.Month && e.Year == p.Year);
            if (accessible != null) q = q.Where(e => accessible.Contains(e.Store));
            if (stores != null) q = q.Where(e => stores.Contains(e.Store));
            else if (omOcStores != null) q = q.Where(e => omOcStores.Contains(e.Store));

            // Group and count server-side instead of pulling every matching
            // ActiveEmployee row (all columns) over the wire just to group by
            // one field in C# — same result, far less data transferred per
            // period (was the single biggest source of slow page loads once
            // Neon was swapped for a remote SQL Server with less network
            // headroom: this one query alone was taking 6+ seconds).
            var grouped = await q.GroupBy(keySelector)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToListAsync();
            foreach (var group in grouped)
            {
                if (excludeEmptyKey && string.IsNullOrEmpty(group.Key)) continue;
                totals[group.Key] = totals.GetValueOrDefault(group.Key) + group.Count;
            }
        }

        var periodCount = periods.Count > 0 ? periods.Count : 1;
        return totals
            .Select(kv => new ChartDataItem { Label = kv.Key, Value = (int)Math.Round(kv.Value / (double)periodCount) })
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetGenderBreakdownAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var stores = MultiValueFilter.Split(store);

        if (!month.HasValue || !year.HasValue)
        {
            var qAll = _db.ActiveEmployees.AsQueryable();
            if (accessible != null) qAll = qAll.Where(e => accessible.Contains(e.Store));
            if (stores != null) qAll = qAll.Where(e => stores.Contains(e.Store));
            return await qAll.GroupBy(e => e.Gender)
                .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
                .OrderBy(c => c.Label)
                .ToListAsync();
        }

        fromMonth ??= month; fromYear ??= year;
        var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        var anchor  = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
        var omOcStores = stores == null ? await GetStoresForOmOcAsync(anchor.Month, anchor.Year, om, oc, soc, od) : null;

        return (await AverageBreakdownAsync(periods, accessible, stores, omOcStores, e => e.Gender, excludeEmptyKey: false))
            .OrderBy(c => c.Label)
            .ToList();
    }

    public async Task<List<PeriodItem>> GetAvailablePeriodsAsync()
    {
        return await _db.ActiveEmployees
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => new PeriodItem { Month = p.Month, Year = p.Year })
            .ToListAsync();
    }

    public async Task<List<StoreComparisonRow>> GetStoreComparisonAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        // Multiple sibling AJAX endpoints (store-comparison, oc-om-analysis,
        // smart-insights) each need this exact result for the same request
        // parameters — cache it the same way GetKpisAsync is cached, so a single
        // page load computes it once instead of recomputing it 2-4x.
        var cacheKey = $"store-comparison_{month}_{year}_{fromMonth}_{fromYear}_{om}_{oc}_{soc}_{od}_{months}_{role}_{assignedName}";
        if (_cache.TryGetValue(cacheKey, out List<StoreComparisonRow>? cachedRows) && cachedRows != null)
            return cachedRows;

        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        var keys = periods.Select(p => p.Year * 100 + p.Month).ToList();
        var anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();

        // ── Headcount: average across ALL selected periods per store ──────────
        // Querying each period separately and averaging gives a fair denominator
        // even when the number of months varies (fixes anchor-only bug).
        var allEmpQ = _db.ActiveEmployees.Where(e => keys.Contains(e.Year * 100 + e.Month));
        if (accessible != null)
            allEmpQ = allEmpQ.Where(e => accessible.Contains(e.Store));

        var headcountsByPeriod = await allEmpQ
            .GroupBy(e => new { e.Store, e.Month, e.Year })
            .Select(g => new { g.Key.Store, Count = g.Count() })
            .ToListAsync();

        // Average headcount per store across the resolved periods
        var headcounts = headcountsByPeriod
            .GroupBy(x => x.Store)
            .Select(g => new { Store = g.Key, AvgCount = g.Average(x => x.Count) })
            .ToList();

        var resQ = _db.Resignations.Where(r => keys.Contains(r.Year * 100 + r.Month));
        if (accessible != null)
            resQ = resQ.Where(r => accessible.Contains(r.Store));

        var resignations = await resQ
            .GroupBy(r => r.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();

        // New Hires: strictly HireDate-driven, scoped to each row's own snapshot
        // period so an employee who stays active across the whole range isn't
        // counted once per monthly snapshot they appear in.
        var newHireQ = NewHiresQuery(keys);
        if (accessible != null)
            newHireQ = newHireQ.Where(e => accessible.Contains(e.Store));

        var newHireRaw = await newHireQ
            .GroupBy(e => e.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();

        var newHiresByStore = newHireRaw.ToDictionary(x => x.Store, x => x.Count);

        // Take first match per store name to avoid duplicate-key issues
        var storeRefList = await _db.StoreReferences
            .Where(s => s.Month == anchor.Month && s.Year == anchor.Year)
            .ToListAsync();
        var storeRefs = storeRefList
            .GroupBy(s => s.StoreName)
            .ToDictionary(g => g.Key, g => g.First());

        var resByStore = resignations.ToDictionary(r => r.Store, r => r.Count);

        var rows = headcounts
            .Select(h =>
            {
                var res        = resByStore.TryGetValue(h.Store, out var r) ? r : 0;
                var nh         = newHiresByStore.TryGetValue(h.Store, out var n) ? n : 0;
                var headcount  = (int)Math.Round(h.AvgCount);
                storeRefs.TryGetValue(h.Store, out var sr);
                return new StoreComparisonRow
                {
                    StoreName           = h.Store,
                    Headcount           = headcount,
                    NewHires            = nh,
                    Resignations        = res,
                    TurnoverRate        = MetricsCalculationService.RatePercent(res, h.AvgCount),
                    OperationConsultant        = sr?.OperationConsultant        ?? "",
                    OperationManager           = sr?.OperationManager           ?? "",
                    SeniorOperationConsultant  = sr?.SeniorOperationConsultant  ?? "",
                    OperationDirector          = sr?.OperationDirector          ?? ""
                };
            });

        if (MultiValueFilter.Split(om) is { } oms) rows = rows.Where(r => oms.Contains(r.OperationManager));
        if (MultiValueFilter.Split(oc) is { } ocs) rows = rows.Where(r => ocs.Contains(r.OperationConsultant));
        if (MultiValueFilter.Split(soc) is { } socs) rows = rows.Where(r => socs.Contains(r.SeniorOperationConsultant));
        if (MultiValueFilter.Split(od) is { } ods) rows = rows.Where(r => ods.Contains(r.OperationDirector));

        var result = rows.OrderByDescending(s => s.TurnoverRate).ToList();
        _cache.Set(cacheKey, result, CacheDuration);
        return result;
    }

    public async Task<OcOmAnalysisResult> GetOcOmAnalysisAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var stores = await GetStoreComparisonAsync(month, year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months);

        OcOmRow ToRow(IGrouping<string, StoreComparisonRow> g, string type) => new()
        {
            Name              = g.Key,
            Type              = type,
            StoreCount        = g.Count(),
            TotalResignations = g.Sum(s => s.Resignations),
            TotalHeadcount    = g.Sum(s => s.Headcount),
            AvgTurnoverRate   = MetricsCalculationService.RatePercent(g.Sum(s => s.Resignations), g.Sum(s => s.Headcount))
        };

        var ocRows = stores
            .Where(s => !string.IsNullOrEmpty(s.OperationConsultant))
            .GroupBy(s => s.OperationConsultant)
            .Select(g => ToRow(g, "OC"))
            .OrderByDescending(r => r.AvgTurnoverRate)
            .ToList();

        var omRows = stores
            .Where(s => !string.IsNullOrEmpty(s.OperationManager))
            .GroupBy(s => s.OperationManager)
            .Select(g => ToRow(g, "OM"))
            .OrderByDescending(r => r.AvgTurnoverRate)
            .ToList();

        var socRows = stores
            .Where(s => !string.IsNullOrEmpty(s.SeniorOperationConsultant))
            .GroupBy(s => s.SeniorOperationConsultant)
            .Select(g => ToRow(g, "SOC"))
            .OrderByDescending(r => r.AvgTurnoverRate)
            .ToList();

        var odRows = stores
            .Where(s => !string.IsNullOrEmpty(s.OperationDirector))
            .GroupBy(s => s.OperationDirector)
            .Select(g => ToRow(g, "OD"))
            .OrderByDescending(r => r.AvgTurnoverRate)
            .ToList();

        return new OcOmAnalysisResult { OcRows = ocRows, OmRows = omRows, SocRows = socRows, OdRows = odRows };
    }

    public async Task<List<SmartInsightItem>> GetSmartInsightsAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var insights = new List<SmartInsightItem>();
        var current  = await GetStoreComparisonAsync(month, year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months);
        if (!current.Any()) return insights;

        // ── Build the equivalent PREVIOUS window (same length as the current selection) ──
        // This ensures comparisons are apples-to-apples regardless of how many months
        // the user has selected (fixes single-month fallback bug).
        var currentPeriods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        var periodCount    = currentPeriods.Count;

        int  prevAnchorMonth, prevAnchorYear;
        int? prevFromMonth = null, prevFromYear = null;
        string? prevMonths = null;

        if (!string.IsNullOrWhiteSpace(months))
        {
            // Discrete month selection (e.g. "1,3,5" in 2024) → same months, prior year
            prevAnchorMonth = month;
            prevAnchorYear  = year - 1;
            prevMonths      = months;
        }
        else
        {
            // Contiguous range → shift the entire window back by periodCount months
            var anchorShifted = new DateTime(year, month, 1).AddMonths(-periodCount);
            prevAnchorMonth   = anchorShifted.Month;
            prevAnchorYear    = anchorShifted.Year;

            if (fromMonth.HasValue && fromYear.HasValue)
            {
                var fromShifted = new DateTime(fromYear.Value, fromMonth.Value, 1).AddMonths(-periodCount);
                prevFromMonth   = fromShifted.Month;
                prevFromYear    = fromShifted.Year;
            }
        }

        var previous    = await GetStoreComparisonAsync(prevAnchorMonth, prevAnchorYear, role, assignedName, prevFromMonth, prevFromYear, om, oc, soc, od, prevMonths);
        var prevByStore = previous.ToDictionary(s => s.StoreName);

        // Human-readable label for comparison window used in descriptions
        var periodLabel = periodCount == 1 ? "last month" : $"prior {periodCount}-month period";

        // 1. Highest turnover store
        var highest = current.First();
        if (highest.TurnoverRate > 0)
            insights.Add(new SmartInsightItem
            {
                Icon        = "bi-exclamation-triangle-fill",
                Color       = "danger",
                Title       = $"Highest Turnover: {highest.StoreName}",
                Description = $"{highest.TurnoverRate:F1}% turnover — {highest.Resignations} resignation(s) from {highest.Headcount} average employees."
            });

        // 2. Best performing store
        var best = current.Where(s => s.Headcount > 0).OrderBy(s => s.TurnoverRate).FirstOrDefault();
        if (best != null && current.Count > 1 && best.StoreName != highest.StoreName)
            insights.Add(new SmartInsightItem
            {
                Icon        = "bi-check-circle-fill",
                Color       = "success",
                Title       = $"Best Performing: {best.StoreName}",
                Description = $"Lowest turnover at {best.TurnoverRate:F1}% with only {best.Resignations} resignation(s)."
            });

        // 3. Spike detection (>= 5% jump vs equivalent prior window)
        var spikes = current
            .Where(s => prevByStore.TryGetValue(s.StoreName, out var p) && s.TurnoverRate - p.TurnoverRate >= 5)
            .OrderByDescending(s => s.TurnoverRate - prevByStore[s.StoreName].TurnoverRate)
            .Take(3)
            .ToList();

        foreach (var spike in spikes)
        {
            var prev  = prevByStore[spike.StoreName];
            var delta = spike.TurnoverRate - prev.TurnoverRate;
            insights.Add(new SmartInsightItem
            {
                Icon        = "bi-graph-up-arrow",
                Color       = "warning",
                Title       = $"Turnover Spike: {spike.StoreName}",
                Description = $"↑ +{delta:F1}% vs {periodLabel} ({prev.TurnoverRate:F1}% → {spike.TurnoverRate:F1}%)."
            });
        }

        // 4. Overall trend — compare turnover RATES (not raw counts) so a growing
        //    workforce does not falsely appear as "Worsening".
        if (previous.Any())
        {
            var currRes  = current.Sum(s => s.Resignations);
            var prevRes  = previous.Sum(s => s.Resignations);
            var currHead = current.Sum(s => s.Headcount);
            var prevHead = previous.Sum(s => s.Headcount);

            var currRate = MetricsCalculationService.RatePercent(currRes, currHead);
            var prevRate = MetricsCalculationService.RatePercent(prevRes, prevHead);
            var rateDiff = Math.Round(currRate - prevRate, 1);

            insights.Add(new SmartInsightItem
            {
                Icon        = rateDiff > 0 ? "bi-arrow-up-circle-fill" : rateDiff < 0 ? "bi-arrow-down-circle-fill" : "bi-dash-circle-fill",
                Color       = rateDiff > 0 ? "danger" : rateDiff < 0 ? "success" : "secondary",
                Title       = rateDiff > 0 ? "Trend: Worsening" : rateDiff < 0 ? "Trend: Improving" : "Trend: Stable",
                Description = rateDiff != 0
                    ? $"Turnover rate {(rateDiff > 0 ? "increased" : "decreased")} by {Math.Abs(rateDiff):F1}% vs {periodLabel} ({prevRate:F1}% → {currRate:F1}%)."
                    : $"Turnover rate unchanged at {currRate:F1}% vs {periodLabel}."
            });
        }

        // 5. Worst OC by weighted turnover rate
        var worstOc = current
            .Where(s => !string.IsNullOrEmpty(s.OperationConsultant))
            .GroupBy(s => s.OperationConsultant)
            .Select(g => new
            {
                Name            = g.Key,
                StoreCount      = g.Count(),
                TotalRes        = g.Sum(s => s.Resignations),
                TotalHead       = g.Sum(s => s.Headcount),
                AvgTurnoverRate = MetricsCalculationService.RatePercent(g.Sum(s => s.Resignations), g.Sum(s => s.Headcount))
            })
            .OrderByDescending(g => g.AvgTurnoverRate)
            .FirstOrDefault();

        if (worstOc != null)
            insights.Add(new SmartInsightItem
            {
                Icon        = "bi-person-fill-exclamation",
                Color       = "warning",
                Title       = $"Highest OC Turnover: {worstOc.Name}",
                Description = $"{worstOc.AvgTurnoverRate:F1}% weighted avg across {worstOc.StoreCount} store(s) — {worstOc.TotalRes} total resignation(s)."
            });

        // 6. Worst OM by weighted turnover rate
        var worstOm = current
            .Where(s => !string.IsNullOrEmpty(s.OperationManager))
            .GroupBy(s => s.OperationManager)
            .Select(g => new
            {
                Name            = g.Key,
                StoreCount      = g.Count(),
                TotalRes        = g.Sum(s => s.Resignations),
                TotalHead       = g.Sum(s => s.Headcount),
                AvgTurnoverRate = MetricsCalculationService.RatePercent(g.Sum(s => s.Resignations), g.Sum(s => s.Headcount))
            })
            .OrderByDescending(g => g.AvgTurnoverRate)
            .FirstOrDefault();

        if (worstOm != null)
            insights.Add(new SmartInsightItem
            {
                Icon        = "bi-person-badge-fill",
                Color       = "warning",
                Title       = $"Highest OM Turnover: {worstOm.Name}",
                Description = $"{worstOm.AvgTurnoverRate:F1}% weighted avg across {worstOm.StoreCount} store(s) — {worstOm.TotalRes} total resignation(s)."
            });

        return insights;
    }

    public async Task<List<StoreBreakdown>> GetPerStoreTurnoverAsync(int month, int year, string role, string? assignedName)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);

        // Headcount per store
        var empQ = _db.ActiveEmployees.Where(e => e.Month == month && e.Year == year);
        if (accessible != null)
            empQ = empQ.Where(e => accessible.Contains(e.Store));

        var headcounts = await empQ
            .GroupBy(e => e.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();

        // Resignations per store
        var resQ = _db.Resignations.Where(r => r.Month == month && r.Year == year);
        if (accessible != null)
            resQ = resQ.Where(r => accessible.Contains(r.Store));

        var resignations = await resQ
            .GroupBy(r => r.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();

        // New Hires: strictly HireDate-driven — no roster diff against last month.
        var newHireQ = NewHiresQuery(new[] { year * 100 + month });
        if (accessible != null)
            newHireQ = newHireQ.Where(e => accessible.Contains(e.Store));

        var newHireRaw = await newHireQ
            .GroupBy(e => e.Store)
            .Select(g => new { Store = g.Key, Count = g.Count() })
            .ToListAsync();
        var newHiresByStore = newHireRaw.ToDictionary(x => x.Store, x => x.Count);

        var resByStore = resignations.ToDictionary(r => r.Store, r => r.Count);

        return headcounts
            .Select(h =>
            {
                var res = resByStore.TryGetValue(h.Store, out var r) ? r : 0;
                var nh  = newHiresByStore.TryGetValue(h.Store, out var n) ? n : 0;
                return new StoreBreakdown
                {
                    Store       = h.Store,
                    Headcount   = h.Count,
                    Resignations = res,
                    TurnoverRate = MetricsCalculationService.RatePercent(res, h.Count),
                    NewHires    = nh
                };
            })
            .OrderByDescending(s => s.TurnoverRate)
            .ToList();
    }

    public async Task<TrendMatrixResult> GetTrendMatrixAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null, string? months = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, null, null);

        // All available periods ordered chronologically
        var periods = await _db.ActiveEmployees
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToListAsync();

        if (sinceYear.HasValue)
            periods = periods.Where(p => p.Year >= sinceYear.Value).ToList();

        var monthFilter = MultiValueFilter.Split(months)?.Select(int.Parse).ToHashSet();
        if (monthFilter != null)
            periods = periods.Where(p => monthFilter.Contains(p.Month)).ToList();

        var periodKeys = periods.Select(p => $"{p.Year:D4}-{p.Month:D2}").ToList();

        // Headcounts grouped by store + period
        var empQ = _db.ActiveEmployees.AsQueryable();
        if (accessible != null)
            empQ = empQ.Where(e => accessible.Contains(e.Store));

        var headcounts = await empQ
            .GroupBy(e => new { e.Store, e.Month, e.Year })
            .Select(g => new { g.Key.Store, g.Key.Month, g.Key.Year, Count = g.Count() })
            .ToListAsync();

        // Resignations grouped by store + period
        var resQ = _db.Resignations.AsQueryable();
        if (accessible != null)
            resQ = resQ.Where(r => accessible.Contains(r.Store));

        var resignations = await resQ
            .GroupBy(r => new { r.Store, r.Month, r.Year })
            .Select(g => new { g.Key.Store, g.Key.Month, g.Key.Year, Count = g.Count() })
            .ToListAsync();

        // Latest OC/OM assignment per store
        var storeRefList = await LoadLatestStoreReferenceCandidatesAsync();
        var latestRefByStore = storeRefList
            .GroupBy(s => s.StoreName)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).First());
        var ocByStore = latestRefByStore.ToDictionary(kv => kv.Key, kv => kv.Value.OperationConsultant ?? "");
        var omByStore = latestRefByStore.ToDictionary(kv => kv.Key, kv => kv.Value.OperationManager ?? "");
        var socByStore = latestRefByStore.ToDictionary(kv => kv.Key, kv => kv.Value.SeniorOperationConsultant ?? "");
        var odByStore = latestRefByStore.ToDictionary(kv => kv.Key, kv => kv.Value.OperationDirector ?? "");

        // Build fast lookups
        var hcLookup  = headcounts .ToDictionary(x => $"{x.Store}|{x.Year:D4}-{x.Month:D2}", x => x.Count);
        var resLookup = resignations.GroupBy(x => $"{x.Store}|{x.Year:D4}-{x.Month:D2}")
                                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

        var allStores = headcounts.Select(h => h.Store).Distinct().OrderBy(s => s).ToList();
        if (MultiValueFilter.Split(om) is { } oms) allStores = allStores.Where(s => omByStore.TryGetValue(s, out var v) && oms.Contains(v)).ToList();
        if (MultiValueFilter.Split(oc) is { } ocs) allStores = allStores.Where(s => ocByStore.TryGetValue(s, out var v) && ocs.Contains(v)).ToList();
        if (MultiValueFilter.Split(soc) is { } socs) allStores = allStores.Where(s => socByStore.TryGetValue(s, out var v) && socs.Contains(v)).ToList();
        if (MultiValueFilter.Split(od) is { } ods) allStores = allStores.Where(s => odByStore.TryGetValue(s, out var v) && ods.Contains(v)).ToList();

        var rows = allStores.Select(store =>
        {
            var periodRates = new Dictionary<string, double?>();
            var nonNullRates = new List<double>();

            foreach (var pk in periodKeys)
            {
                var key = $"{store}|{pk}";
                if (hcLookup.TryGetValue(key, out var hc) && hc > 0)
                {
                    var res  = resLookup.TryGetValue(key, out var rc) ? rc : 0;
                    var rate = MetricsCalculationService.RatePercent(res, hc);
                    periodRates[pk] = rate;
                    nonNullRates.Add(rate);
                }
                else
                {
                    periodRates[pk] = null;
                }
            }

            return new TrendMatrixRow
            {
                StoreName           = store,
                OperationConsultant = ocByStore.TryGetValue(store, out var ocVal) ? ocVal : "",
                OperationManager    = omByStore.TryGetValue(store, out var omVal) ? omVal : "",
                PeriodRates         = periodRates,
                // Mean of each shown period's own rate, matching the TOTAL
                // column (sum of the same displayed rates) so AVG = TOTAL /
                // number of periods with data.
                AvgRate             = nonNullRates.Count > 0 ? Math.Round(nonNullRates.Average(), 1) : null
            };
        }).ToList();

        return new TrendMatrixResult { Periods = periodKeys, Rows = rows };
    }

    // ── Active-workforce composition (Workforce page) ──────────────────────
    // Snapshots of who currently works here, as opposed to the Turnover-page
    // methods above which describe who resigned.

    public async Task<List<ChartDataItem>> GetHeadcountByJobTitleAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var stores = MultiValueFilter.Split(store);

        if (!month.HasValue || !year.HasValue)
        {
            var qAll = _db.ActiveEmployees.AsQueryable();
            if (accessible != null) qAll = qAll.Where(e => accessible.Contains(e.Store));
            if (stores != null) qAll = qAll.Where(e => stores.Contains(e.Store));
            return await qAll.GroupBy(e => e.JobTitle)
                .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
                .OrderByDescending(x => x.Value)
                .ToListAsync();
        }

        fromMonth ??= month; fromYear ??= year;
        var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        var anchor  = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
        var omOcStores = stores == null ? await GetStoresForOmOcAsync(anchor.Month, anchor.Year, om, oc, soc, od) : null;

        return (await AverageBreakdownAsync(periods, accessible, stores, omOcStores, e => e.JobTitle, excludeEmptyKey: false))
            .OrderByDescending(c => c.Value)
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetHeadcountByPayrollGroupAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var stores = MultiValueFilter.Split(store);

        if (!month.HasValue || !year.HasValue)
        {
            var qAll = _db.ActiveEmployees.Where(e => e.PayrollGroup != "");
            if (accessible != null) qAll = qAll.Where(e => accessible.Contains(e.Store));
            if (stores != null) qAll = qAll.Where(e => stores.Contains(e.Store));
            return await qAll.GroupBy(e => e.PayrollGroup)
                .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
                .OrderByDescending(x => x.Value)
                .ToListAsync();
        }

        fromMonth ??= month; fromYear ??= year;
        var periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        var anchor  = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
        var omOcStores = stores == null ? await GetStoresForOmOcAsync(anchor.Month, anchor.Year, om, oc, soc, od) : null;

        return (await AverageBreakdownAsync(periods, accessible, stores, omOcStores, e => e.PayrollGroup, excludeEmptyKey: true))
            .OrderByDescending(c => c.Value)
            .ToList();
    }

    private static readonly (string Label, int Min, int Max)[] HeadcountTenureBuckets =
    {
        ("< 3 months", 0, 90),
        ("3–6 months", 90, 180),
        ("6–12 months", 180, 365),
        ("1–2 years", 365, 730),
        ("2+ years", 730, int.MaxValue),
    };

    public async Task<List<ChartDataItem>> GetHeadcountByTenureAsync(int? month, int? year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var stores = MultiValueFilter.Split(store);

        List<(int Month, int Year)> periods;
        if (month.HasValue && year.HasValue)
        {
            fromMonth ??= month; fromYear ??= year;
            periods = ResolvePeriods(month, year, fromMonth, fromYear, months);
        }
        else
        {
            var latest = await _db.ActiveEmployees
                .OrderByDescending(e => e.Year).ThenByDescending(e => e.Month)
                .Select(e => new { e.Month, e.Year }).FirstOrDefaultAsync();
            if (latest == null) return new List<ChartDataItem>();
            periods = new List<(int Month, int Year)> { (latest.Month, latest.Year) };
        }

        var anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
        var omOcStores = stores == null ? await GetStoresForOmOcAsync(anchor.Month, anchor.Year, om, oc, soc, od) : null;

        // Tenure buckets are computed per period (as-of that period's month end)
        // then averaged across periods — the same "average across the selected
        // range" treatment as the other composition breakdowns above, rather
        // than anchoring on a single (e.g. latest) month.
        var totals = new Dictionary<string, int>();
        foreach (var p in periods)
        {
            var q = _db.ActiveEmployees.Where(e => e.Month == p.Month && e.Year == p.Year && e.HireDate != null);
            if (accessible != null) q = q.Where(e => accessible.Contains(e.Store));
            if (stores != null) q = q.Where(e => stores.Contains(e.Store));
            else if (omOcStores != null) q = q.Where(e => omOcStores.Contains(e.Store));

            var hireDates = await q.Select(e => e.HireDate!.Value).ToListAsync();
            if (hireDates.Count == 0) continue;

            var asOf = new DateOnly(p.Year, p.Month, DateTime.DaysInMonth(p.Year, p.Month));
            foreach (var b in HeadcountTenureBuckets)
            {
                var count = hireDates.Count(hd => (asOf.DayNumber - hd.DayNumber) >= b.Min && (asOf.DayNumber - hd.DayNumber) < b.Max);
                if (count > 0) totals[b.Label] = totals.GetValueOrDefault(b.Label) + count;
            }
        }

        var periodCount = periods.Count > 0 ? periods.Count : 1;
        return HeadcountTenureBuckets
            .Where(b => totals.ContainsKey(b.Label))
            .Select(b => new ChartDataItem { Label = b.Label, Value = (int)Math.Round(totals[b.Label] / (double)periodCount) })
            .Where(c => c.Value > 0)
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetHeadcountTrendAsync(string? store, string role, string? assignedName, string? om, string? oc, string? soc, string? od, int? sinceYear)
    {
        var periods = await _db.ActiveEmployees
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToListAsync();
        if (sinceYear.HasValue) periods = periods.Where(p => p.Year >= sinceYear.Value).ToList();

        var accessible = await GetAccessibleStoresAsync(role, assignedName, null, null);
        var stores = MultiValueFilter.Split(store);

        var result = new List<ChartDataItem>();
        foreach (var p in periods)
        {
            var q = _db.ActiveEmployees.Where(e => e.Month == p.Month && e.Year == p.Year);
            if (accessible != null) q = q.Where(e => accessible.Contains(e.Store));
            if (stores != null) q = q.Where(e => stores.Contains(e.Store));
            else if (await GetStoresForOmOcAsync(p.Month, p.Year, om, oc, soc, od) is { } omOcStores) q = q.Where(e => omOcStores.Contains(e.Store));

            var count = await q.CountAsync();
            result.Add(new ChartDataItem { Label = $"{p.Year:D4}-{p.Month:D2}", Value = count });
        }
        return result;
    }

    public async Task<List<StoreHeadcountRow>> GetStoreHeadcountBreakdownAsync(int month, int year, string role, string? assignedName, string? om, string? oc, string? soc, string? od)
    {
        var accessible = await GetAccessibleStoresAsync(role, assignedName, month, year);
        var omOcStores = await GetStoresForOmOcAsync(month, year, om, oc, soc, od);
        var q = _db.ActiveEmployees.Where(e => e.Month == month && e.Year == year);
        if (accessible != null) q = q.Where(e => accessible.Contains(e.Store));
        if (omOcStores != null) q = q.Where(e => omOcStores.Contains(e.Store));

        // Group by (Store, Gender) server-side and only pull the per-pair counts
        // — one row per store/gender combination — instead of every matching
        // ActiveEmployee row. Same final shape, far fewer rows over the wire.
        var grouped = await q.GroupBy(e => new { e.Store, e.Gender })
            .Select(g => new { g.Key.Store, g.Key.Gender, Count = g.Count() })
            .ToListAsync();
        return grouped.GroupBy(r => r.Store)
            .Select(g => new StoreHeadcountRow
            {
                StoreName       = g.Key,
                Headcount       = g.Sum(x => x.Count),
                GenderBreakdown = g.OrderBy(x => x.Gender).ToDictionary(x => x.Gender, x => x.Count)
            })
            .OrderByDescending(r => r.Headcount)
            .ToList();
    }
}
