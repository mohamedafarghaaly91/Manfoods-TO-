using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class NinetyDayTurnoverService : INinetyDayTurnoverService
{
    // Shared with UploadService, which invalidates these keys whenever it writes
    // to ActiveEmployees or Resignations — same convention as
    // ScorecardService.HistoricalRecordsCacheKey.
    public const string ActiveHiresCacheKey = "ninety-day:active-hires";
    public const string ResignationTenuresCacheKey = "ninety-day:resignation-tenures";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private readonly AppDbContext _db;
    private readonly IStoreAccessService _storeAccess;
    private readonly IMemoryCache _cache;

    public NinetyDayTurnoverService(AppDbContext db, IStoreAccessService storeAccess, IMemoryCache cache)
    {
        _db = db;
        _storeAccess = storeAccess;
        _cache = cache;
    }

    private class ResignationTenure
    {
        public string EmployeeId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Store { get; set; } = "";
        public string JobTitle { get; set; } = "";
        public string PayrollGroup { get; set; } = "";
        public string Gender { get; set; } = "";
        public DateOnly HireDate { get; set; }
        public DateOnly ResignationDate { get; set; }
        public int TenureDays { get; set; }
    }

    private async Task<List<(string EmployeeId, string Store, int Month, int Year)>> LoadActiveHiresAsync()
    {
        // This query has no request-specific parameters (the go-live threshold is a
        // fixed cutoff, not a per-call filter), so it's cached whole rather than
        // re-read from the database on every call — see UploadService for the
        // write-side invalidation of ActiveHiresCacheKey.
        if (_cache.TryGetValue(ActiveHiresCacheKey, out List<(string EmployeeId, string Store, int Month, int Year)>? cached) && cached != null)
            return cached;

        // Go-live threshold is a dynamic date comparison (not a hardcoded year), so it
        // keeps working unchanged as future years' data arrives.
        var goLive = MetricsCalculationService.GoLiveDate;
        var rows = await _db.ActiveEmployees
            .Where(e => e.HireDate != null && e.HireDate.Value >= goLive)
            .Select(e => new { e.EmployeeId, e.Store, e.HireDate })
            .ToListAsync();
        var result = rows.Select(r => (r.EmployeeId, r.Store, r.HireDate!.Value.Month, r.HireDate!.Value.Year)).ToList();
        _cache.Set(ActiveHiresCacheKey, result, CacheDuration);
        return result;
    }

    private async Task<List<ResignationTenure>> LoadResignationTenuresAsync()
    {
        // Same no-request-parameters cacheability as LoadActiveHiresAsync above.
        if (_cache.TryGetValue(ResignationTenuresCacheKey, out List<ResignationTenure>? cached) && cached != null)
            return cached;

        var goLive = MetricsCalculationService.GoLiveDate;
        var rows = await _db.Resignations
            .Where(r => r.HireDate != null && r.ResignationDate != null && r.HireDate.Value >= goLive)
            .ToListAsync();
        var result = rows.Select(r => new ResignationTenure
        {
            EmployeeId = r.EmployeeId,
            Name = r.Name,
            Store = r.Store,
            JobTitle = r.JobTitle,
            PayrollGroup = r.PayrollGroup,
            Gender = r.Gender,
            HireDate = r.HireDate!.Value,
            ResignationDate = r.ResignationDate!.Value,
            TenureDays = r.ResignationDate!.Value.DayNumber - r.HireDate!.Value.DayNumber,
        }).ToList();
        _cache.Set(ResignationTenuresCacheKey, result, CacheDuration);
        return result;
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

    // Stores whose latest-known Operation Manager / Operation Consultant match the filter.
    // Returns null when no OM/OC filter is set.
    private async Task<List<string>?> GetStoresForOmOcAsync(string? om, string? oc, string? soc = null, string? od = null)
    {
        if (string.IsNullOrEmpty(om) && string.IsNullOrEmpty(oc) && string.IsNullOrEmpty(soc) && string.IsNullOrEmpty(od)) return null;
        var refs = await LoadLatestStoreReferenceCandidatesAsync();
        var latestByStore = refs.GroupBy(s => s.StoreName)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).First());
        return latestByStore.Values
            .Where(s => (string.IsNullOrEmpty(om) || s.OperationManager == om)
                     && (string.IsNullOrEmpty(oc) || s.OperationConsultant == oc)
                     && (string.IsNullOrEmpty(soc) || s.SeniorOperationConsultant == soc)
                     && (string.IsNullOrEmpty(od) || s.OperationDirector == od))
            .Select(s => s.StoreName)
            .ToList();
    }

    private static NinetyDayKpiViewModel ComputeKpi(
        List<(string EmployeeId, string Store, int Month, int Year)> activeHires,
        List<ResignationTenure> resTenures,
        HashSet<int> cohortKeys, int latestMonth, int latestYear, List<string>? stores, List<string>? omOcStores,
        List<string>? accessible = null)
    {
        // Role-based store access is always enforced (AND'd in), regardless of
        // which explicit store/om-oc filter is also present.
        bool StoreOk(string s) => (stores == null || stores.Contains(s)) && (omOcStores == null || omOcStores.Contains(s))
            && (accessible == null || accessible.Contains(s));

        var fromActive = activeHires.Where(a => cohortKeys.Contains(a.Year * 100 + a.Month) && StoreOk(a.Store)).Select(a => a.EmployeeId);
        var fromRes = resTenures.Where(r => cohortKeys.Contains(r.HireDate.Year * 100 + r.HireDate.Month) && StoreOk(r.Store)).Select(r => r.EmployeeId);
        var hireIds = fromActive.Concat(fromRes).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToHashSet();

        var earlyLeaverIds = resTenures
            .Where(r => cohortKeys.Contains(r.HireDate.Year * 100 + r.HireDate.Month) && MetricsCalculationService.IsEarlyLeaver(r.TenureDays) && StoreOk(r.Store))
            .Select(r => r.EmployeeId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToHashSet();

        var totalHires = hireIds.Count;
        var earlyLeavers = earlyLeaverIds.Count;
        var rate = MetricsCalculationService.NinetyDayRate(totalHires, earlyLeavers);

        var cohortCloseDate = new DateOnly(latestYear, latestMonth, DateTime.DaysInMonth(latestYear, latestMonth));
        var isProvisional = DateOnly.FromDateTime(DateTime.UtcNow) < cohortCloseDate.AddDays(MetricsCalculationService.NinetyDayWindowDays);

        return new NinetyDayKpiViewModel
        {
            CohortMonth = latestMonth,
            CohortYear = latestYear,
            TotalHires = totalHires,
            EarlyLeavers = earlyLeavers,
            Rate = rate,
            IsProvisional = isProvisional,
        };
    }

    public async Task<List<PeriodItem>> GetCohortPeriodsAsync()
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();

        return activeHires.Select(a => (a.Month, a.Year))
            .Concat(resTenures.Select(r => (r.HireDate.Month, r.HireDate.Year)))
            .Distinct()
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => new PeriodItem { Month = p.Month, Year = p.Year })
            .ToList();
    }

    public async Task<List<string>> GetStoreListAsync(string role, string? assignedName)
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        var stores = activeHires.Select(a => a.Store)
            .Concat(resTenures.Select(r => r.Store))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct();
        if (accessible != null) stores = stores.Where(s => accessible.Contains(s));
        return stores.OrderBy(s => s).ToList();
    }

    public async Task<NinetyDayKpiViewModel> GetKpiAsync(int month, int year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();
        var periods = DashboardService.ResolvePeriods(month, year, fromMonth, fromYear, months);
        var keys = periods.Select(p => p.Year * 100 + p.Month).ToHashSet();
        var anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
        var omOcStores = await GetStoresForOmOcAsync(om, oc, soc, od);
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        return ComputeKpi(activeHires, resTenures, keys, anchor.Month, anchor.Year, MultiValueFilter.Split(store), omOcStores, accessible);
    }

    public async Task<List<RateTrendItem>> GetTrendAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();
        var omOcStores = await GetStoresForOmOcAsync(om, oc, soc, od);
        var stores = MultiValueFilter.Split(store);
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);

        var periods = activeHires.Select(a => (a.Month, a.Year))
            .Concat(resTenures.Select(r => (r.HireDate.Month, r.HireDate.Year)))
            .Distinct()
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToList();

        var result = new List<RateTrendItem>();
        foreach (var (m, y) in periods)
        {
            var kpi = ComputeKpi(activeHires, resTenures, new HashSet<int> { y * 100 + m }, m, y, stores, omOcStores, accessible);
            if (kpi.TotalHires == 0) continue;
            result.Add(new RateTrendItem
            {
                Label = new DateOnly(y, m, 1).ToString("MMM yy"),
                Rate = kpi.Rate,
                TotalHires = kpi.TotalHires,
                EarlyLeavers = kpi.EarlyLeavers,
                IsProvisional = kpi.IsProvisional,
            });
        }
        return result;
    }

    public async Task<List<ChartDataItem>> GetByStoreAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();
        var periods = DashboardService.ResolvePeriods(month, year, fromMonth, fromYear, months);
        var keys = periods.Select(p => p.Year * 100 + p.Month).ToHashSet();
        var anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
        var omOcStores = await GetStoresForOmOcAsync(om, oc, soc, od);
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);

        var stores = activeHires.Where(a => keys.Contains(a.Year * 100 + a.Month)).Select(a => a.Store)
            .Concat(resTenures.Where(r => keys.Contains(r.HireDate.Year * 100 + r.HireDate.Month)).Select(r => r.Store))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct();
        if (omOcStores != null) stores = stores.Where(s => omOcStores.Contains(s));
        if (accessible != null) stores = stores.Where(s => accessible.Contains(s));

        var result = new List<ChartDataItem>();
        foreach (var store in stores)
        {
            var kpi = ComputeKpi(activeHires, resTenures, keys, anchor.Month, anchor.Year, new List<string> { store }, null);
            if (kpi.TotalHires == 0) continue;
            result.Add(new ChartDataItem { Label = store, Value = (int)Math.Round(kpi.Rate) });
        }
        return result.OrderByDescending(c => c.Value).ToList();
    }

    public async Task<List<NinetyDayStoreRow>> GetStoreComparisonAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();
        var periods = DashboardService.ResolvePeriods(month, year, fromMonth, fromYear, months);
        var keys = periods.Select(p => p.Year * 100 + p.Month).ToHashSet();
        var anchor = periods.OrderByDescending(p => p.Year * 100 + p.Month).First();
        var omOcStores = await GetStoresForOmOcAsync(om, oc, soc, od);
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);

        var stores = activeHires.Where(a => keys.Contains(a.Year * 100 + a.Month)).Select(a => a.Store)
            .Concat(resTenures.Where(r => keys.Contains(r.HireDate.Year * 100 + r.HireDate.Month)).Select(r => r.Store))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct();
        if (omOcStores != null) stores = stores.Where(s => omOcStores.Contains(s));
        if (accessible != null) stores = stores.Where(s => accessible.Contains(s));

        var storeRefList = await LoadLatestStoreReferenceCandidatesAsync();
        var latestRefByStore = storeRefList.GroupBy(s => s.StoreName)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).First());

        var rows = new List<NinetyDayStoreRow>();
        foreach (var store in stores)
        {
            var kpi = ComputeKpi(activeHires, resTenures, keys, anchor.Month, anchor.Year, new List<string> { store }, null);
            if (kpi.TotalHires == 0) continue;
            latestRefByStore.TryGetValue(store, out var sr);
            rows.Add(new NinetyDayStoreRow
            {
                StoreName                  = store,
                OperationConsultant        = sr?.OperationConsultant ?? "",
                OperationManager           = sr?.OperationManager ?? "",
                SeniorOperationConsultant  = sr?.SeniorOperationConsultant ?? "",
                OperationDirector          = sr?.OperationDirector ?? "",
                TotalHires                 = kpi.TotalHires,
                EarlyLeavers               = kpi.EarlyLeavers,
                Rate                       = kpi.Rate,
            });
        }
        return rows.OrderByDescending(r => r.Rate).ToList();
    }

    public async Task<NinetyDayOcOmAnalysisResult> GetOcOmAnalysisAsync(int month, int year, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var stores = await GetStoreComparisonAsync(month, year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months);

        NinetyDayOcOmRow ToRow(IGrouping<string, NinetyDayStoreRow> g, string type) => new()
        {
            Name         = g.Key,
            Type         = type,
            StoreCount   = g.Count(),
            TotalHires   = g.Sum(s => s.TotalHires),
            EarlyLeavers = g.Sum(s => s.EarlyLeavers),
            AvgRate      = MetricsCalculationService.NinetyDayRate(g.Sum(s => s.TotalHires), g.Sum(s => s.EarlyLeavers)),
        };

        var ocRows = stores
            .Where(s => !string.IsNullOrEmpty(s.OperationConsultant))
            .GroupBy(s => s.OperationConsultant)
            .Select(g => ToRow(g, "OC"))
            .OrderByDescending(r => r.AvgRate)
            .ToList();

        var omRows = stores
            .Where(s => !string.IsNullOrEmpty(s.OperationManager))
            .GroupBy(s => s.OperationManager)
            .Select(g => ToRow(g, "OM"))
            .OrderByDescending(r => r.AvgRate)
            .ToList();

        var socRows = stores
            .Where(s => !string.IsNullOrEmpty(s.SeniorOperationConsultant))
            .GroupBy(s => s.SeniorOperationConsultant)
            .Select(g => ToRow(g, "SOC"))
            .OrderByDescending(r => r.AvgRate)
            .ToList();

        var odRows = stores
            .Where(s => !string.IsNullOrEmpty(s.OperationDirector))
            .GroupBy(s => s.OperationDirector)
            .Select(g => ToRow(g, "OD"))
            .OrderByDescending(r => r.AvgRate)
            .ToList();

        return new NinetyDayOcOmAnalysisResult { OcRows = ocRows, OmRows = omRows, SocRows = socRows, OdRows = odRows };
    }

    public async Task<List<EarlyLeaverRow>> GetEarlyLeaversAsync(int month, int year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var resTenures = await LoadResignationTenuresAsync();
        var keys = DashboardService.ResolvePeriods(month, year, fromMonth, fromYear, months)
            .Select(p => p.Year * 100 + p.Month).ToHashSet();
        var omOcStores = await GetStoresForOmOcAsync(om, oc, soc, od);
        var stores = MultiValueFilter.Split(store);
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        return resTenures
            .Where(r => keys.Contains(r.HireDate.Year * 100 + r.HireDate.Month) && MetricsCalculationService.IsEarlyLeaver(r.TenureDays)
                     && (stores == null || stores.Contains(r.Store))
                     && (omOcStores == null || omOcStores.Contains(r.Store))
                     && (accessible == null || accessible.Contains(r.Store)))
            .OrderBy(r => r.TenureDays)
            .Select(r => new EarlyLeaverRow
            {
                Name = r.Name,
                Store = r.Store,
                JobTitle = r.JobTitle,
                HireDate = r.HireDate,
                ResignationDate = r.ResignationDate,
                TenureDays = r.TenureDays,
            })
            .ToList();
    }

    // Early leavers (TenureDays <= 90) whose hire cohort and store fall within
    // the resolved filter — shared by every "early leaver breakdown" chart.
    private async Task<List<ResignationTenure>> EarlyLeaversAsync(int month, int year, string? store, string role, string? assignedName,
        int? fromMonth, int? fromYear, string? om, string? oc, string? soc, string? od, string? months)
    {
        var resTenures = await LoadResignationTenuresAsync();
        var keys = DashboardService.ResolvePeriods(month, year, fromMonth, fromYear, months)
            .Select(p => p.Year * 100 + p.Month).ToHashSet();
        var omOcStores = await GetStoresForOmOcAsync(om, oc, soc, od);
        var stores = MultiValueFilter.Split(store);
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        return resTenures
            .Where(r => keys.Contains(r.HireDate.Year * 100 + r.HireDate.Month) && MetricsCalculationService.IsEarlyLeaver(r.TenureDays)
                     && (stores == null || stores.Contains(r.Store))
                     && (omOcStores == null || omOcStores.Contains(r.Store))
                     && (accessible == null || accessible.Contains(r.Store)))
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetEarlyLeaverJobTitlesAsync(int month, int year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var leavers = await EarlyLeaversAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months);
        return leavers
            .Where(r => !string.IsNullOrWhiteSpace(r.JobTitle))
            .GroupBy(r => r.JobTitle)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .OrderByDescending(c => c.Value)
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetEarlyLeaverPayrollGroupsAsync(int month, int year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var leavers = await EarlyLeaversAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months);
        return leavers
            .Where(r => !string.IsNullOrWhiteSpace(r.PayrollGroup))
            .GroupBy(r => r.PayrollGroup)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .OrderByDescending(c => c.Value)
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetEarlyLeaverGenderBreakdownAsync(int month, int year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var leavers = await EarlyLeaversAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months);
        return leavers
            .Where(r => !string.IsNullOrWhiteSpace(r.Gender))
            .GroupBy(r => r.Gender)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .OrderBy(c => c.Label)
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetEarlyLeaverReasonsAsync(int month, int year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var leavers = await EarlyLeaversAsync(month, year, store, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months);
        var earlyLeaverIds = leavers
            .Select(r => r.EmployeeId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        if (earlyLeaverIds.Count == 0) return new List<ChartDataItem>();

        var reasons = await _db.ExitInterviews
            .Where(e => earlyLeaverIds.Contains(e.EmployeeId) && e.ReasonForLeaving != "")
            .Select(e => e.ReasonForLeaving)
            .ToListAsync();

        return reasons
            .GroupBy(r => r)
            .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
            .OrderByDescending(c => c.Value)
            .ToList();
    }

    public async Task<TrendMatrixResult> GetTrendMatrixAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, int? sinceYear = null)
    {
        var activeHires = await LoadActiveHiresAsync();
        var resTenures = await LoadResignationTenuresAsync();
        var omOcStores = await GetStoresForOmOcAsync(om, oc, soc, od);
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        var monthFilter = MultiValueFilter.Split(months)?.Select(int.Parse).ToHashSet();

        var periods = activeHires.Select(a => (a.Month, a.Year))
            .Concat(resTenures.Select(r => (r.HireDate.Month, r.HireDate.Year)))
            .Distinct()
            .Where(p => !sinceYear.HasValue || p.Year >= sinceYear.Value)
            .Where(p => monthFilter == null || monthFilter.Contains(p.Month))
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToList();
        var periodKeys = periods.Select(p => $"{p.Year:D4}-{p.Month:D2}").ToList();

        var allStores = activeHires.Select(a => a.Store)
            .Concat(resTenures.Select(r => r.Store))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();
        if (omOcStores != null) allStores = allStores.Where(s => omOcStores.Contains(s)).ToList();
        if (accessible != null) allStores = allStores.Where(s => accessible.Contains(s)).ToList();

        var storeRefList = await LoadLatestStoreReferenceCandidatesAsync();
        var latestRefByStore = storeRefList.GroupBy(s => s.StoreName)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).First());
        var ocByStore = latestRefByStore.ToDictionary(kv => kv.Key, kv => kv.Value.OperationConsultant ?? "");
        var omByStore = latestRefByStore.ToDictionary(kv => kv.Key, kv => kv.Value.OperationManager ?? "");

        var rows = allStores.Select(store =>
        {
            var periodRates = new Dictionary<string, double?>();
            var nonNullRates = new List<double>();
            var storeList = new List<string> { store };

            foreach (var (m, y) in periods)
            {
                var pk = $"{y:D4}-{m:D2}";
                var kpi = ComputeKpi(activeHires, resTenures, new HashSet<int> { y * 100 + m }, m, y, storeList, null);
                if (kpi.TotalHires > 0)
                {
                    periodRates[pk] = kpi.Rate;
                    nonNullRates.Add(kpi.Rate);
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
                AvgRate             = nonNullRates.Count > 0 ? Math.Round(nonNullRates.Average(), 1) : null,
            };
        }).ToList();

        return new TrendMatrixResult { Periods = periodKeys, Rows = rows };
    }

    public async Task<List<SmartInsightItem>> GetSmartInsightsAsync(int month, int year, string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var insights = new List<SmartInsightItem>();

        // 1. Recent vs. prior 90-day rate trend (up to 3 complete cohorts each
        // side, full history — not limited to the page's cohort-month filter).
        var trend = await GetTrendAsync(store, role, assignedName, om, oc, soc, od);
        var complete = trend.Where(t => !t.IsProvisional).ToList();
        if (complete.Count >= 2)
        {
            var recent = complete.TakeLast(Math.Min(3, complete.Count)).ToList();
            var priorCount = Math.Min(3, complete.Count - recent.Count);
            if (priorCount > 0)
            {
                var prior = complete.Skip(complete.Count - recent.Count - priorCount).Take(priorCount).ToList();
                var recentAvg = recent.Average(t => t.Rate);
                var priorAvg = prior.Average(t => t.Rate);
                var diff = Math.Round(recentAvg - priorAvg, 1);
                if (Math.Abs(diff) >= 1)
                    insights.Add(new SmartInsightItem
                    {
                        Icon = diff < 0 ? "bi-arrow-down-circle-fill" : "bi-arrow-up-circle-fill",
                        Color = diff < 0 ? "success" : "danger",
                        Title = diff < 0 ? "90-Day Rate Improving" : "90-Day Rate Slipping",
                        Description = $"{recentAvg:F1}% avg over the last {recent.Count} cohort(s) vs {priorAvg:F1}% before — {(diff > 0 ? "+" : "")}{diff}pt.",
                    });
            }
        }

        // 2. Best/worst store for the selected cohort months (only meaningful company-wide).
        if (store == null)
        {
            var byStore = await GetStoreComparisonAsync(month, year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months);
            if (byStore.Count > 1)
            {
                var best = byStore.OrderBy(s => s.Rate).First();
                insights.Add(new SmartInsightItem
                {
                    Icon = "bi-trophy-fill",
                    Color = "success",
                    Title = $"Best 90-Day Rate: {best.StoreName}",
                    Description = $"Only {best.Rate:F1}% of this store's hires left within 90 days.",
                });
                var worst = byStore.OrderByDescending(s => s.Rate).First();
                if (worst.StoreName != best.StoreName && worst.Rate >= 50)
                    insights.Add(new SmartInsightItem
                    {
                        Icon = "bi-exclamation-triangle-fill",
                        Color = "danger",
                        Title = $"Weakest 90-Day Rate: {worst.StoreName}",
                        Description = $"{worst.Rate:F1}% of this store's hires left within 90 days.",
                    });
            }
        }

        return insights;
    }
}
