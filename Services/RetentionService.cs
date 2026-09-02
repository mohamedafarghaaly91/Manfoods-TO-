using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class RetentionService : IRetentionService
{
    // Shared with UploadService, which invalidates this key whenever it writes
    // to ActiveEmployees or Resignations — same convention as
    // ScorecardService.HistoricalRecordsCacheKey.
    public const string EmployeeCohortsCacheKey = "retention:employee-cohorts";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private readonly AppDbContext _db;
    private readonly IStoreAccessService _storeAccess;
    private readonly IMemoryCache _cache;

    public RetentionService(AppDbContext db, IStoreAccessService storeAccess, IMemoryCache cache)
    {
        _db = db;
        _storeAccess = storeAccess;
        _cache = cache;
    }

    // Real "retention" is a long-term measure — 30/90-day attrition is covered by the
    // dedicated 90-Day Turnover page instead. The 6-month mark is standardized to
    // MetricsCalculationService.SixMonthRetentionDays (180 days) company-wide.
    private static readonly (int Days, string Label)[] Milestones =
    {
        (MetricsCalculationService.SixMonthRetentionDays, "6 Months"), (365, "1 Year"), (730, "2 Years"), (1095, "3 Years"), (1460, "4 Years"), (1825, "5 Years")
    };
    private static readonly (int Days, string Label)[] CurvePoints =
    {
        (0, "Day 0"), (30, "1mo"), (90, "3mo"), (MetricsCalculationService.SixMonthRetentionDays, "6mo"), (365, "1yr"), (545, "1.5yr"), (730, "2yr"), (1095, "3yr"), (1460, "4yr"), (1825, "5yr")
    };
    private const int LeaderboardDays = 365; // 1-year retention, the standard HR benchmark
    private static readonly (string Label, int Min, int Max)[] TenureBuckets =
    {
        ("< 6 months", 0, MetricsCalculationService.SixMonthRetentionDays),
        ("6–12 months", MetricsCalculationService.SixMonthRetentionDays, 365),
        ("1–2 years", 365, 730),
        ("2–3 years", 730, 1095),
        ("3–4 years", 1095, 1460),
        ("4–5 years", 1460, 1825),
        ("5+ years", 1825, int.MaxValue),
    };

    private class EmployeeCohort
    {
        public string EmployeeId { get; set; } = "";
        public string Store { get; set; } = "";
        public DateOnly HireDate { get; set; }
        public int CohortMonth { get; set; }
        public int CohortYear { get; set; }
        /// <summary>Null means still active (never resigned) as of the latest upload.</summary>
        public int? TenureDays { get; set; }
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

    // This query has no request-specific parameters — it returns the same merged
    // active/resignation rows regardless of role/period/om/oc filters (those are
    // applied afterward by LoadEmployeeCohortsAsync) — so it's cached whole rather
    // than re-read from the database on every Retention page interaction. See
    // UploadService for the write-side invalidation of EmployeeCohortsCacheKey.
    private async Task<List<EmployeeCohort>> LoadAllEmployeeCohortsAsync()
    {
        if (_cache.TryGetValue(EmployeeCohortsCacheKey, out List<EmployeeCohort>? cached) && cached != null)
            return cached;

        var activeRows = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.EmployeeId, e.Store, e.HireDate })
            .ToListAsync();

        var resignationRows = await _db.Resignations
            .Where(r => r.HireDate != null && r.ResignationDate != null)
            .Select(r => new { r.EmployeeId, r.Store, r.HireDate, r.ResignationDate })
            .ToListAsync();

        var byEmployee = new Dictionary<string, EmployeeCohort>();

        foreach (var a in activeRows)
        {
            if (string.IsNullOrWhiteSpace(a.EmployeeId)) continue;
            byEmployee[a.EmployeeId] = new EmployeeCohort
            {
                EmployeeId = a.EmployeeId,
                Store = a.Store,
                HireDate = a.HireDate!.Value,
                CohortMonth = a.HireDate!.Value.Month,
                CohortYear = a.HireDate!.Value.Year,
                TenureDays = null,
            };
        }

        // Resignation records win — they prove the employee actually left. The
        // resignation sheet's own Store column is sometimes left blank at upload
        // time, so fall back to the employee's last-known active Store rather
        // than losing them from every store-grouped chart (Leaderboard, etc.).
        foreach (var r in resignationRows)
        {
            if (string.IsNullOrWhiteSpace(r.EmployeeId)) continue;
            var store = !string.IsNullOrWhiteSpace(r.Store) ? r.Store
                : byEmployee.TryGetValue(r.EmployeeId, out var prior) ? prior.Store : r.Store;
            byEmployee[r.EmployeeId] = new EmployeeCohort
            {
                EmployeeId = r.EmployeeId,
                Store = store,
                HireDate = r.HireDate!.Value,
                CohortMonth = r.HireDate!.Value.Month,
                CohortYear = r.HireDate!.Value.Year,
                TenureDays = r.ResignationDate!.Value.DayNumber - r.HireDate!.Value.DayNumber,
            };
        }

        var records = byEmployee.Values.ToList();
        _cache.Set(EmployeeCohortsCacheKey, records, CacheDuration);
        return records;
    }

    private async Task<List<EmployeeCohort>> LoadEmployeeCohortsAsync(
        string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        IEnumerable<EmployeeCohort> cohorts = await LoadAllEmployeeCohortsAsync();

        if (!string.IsNullOrWhiteSpace(months) && toYear.HasValue)
        {
            var keys = DashboardService.ResolvePeriods(toMonth, toYear, fromMonth, fromYear, months)
                .Select(p => p.Year * 100 + p.Month).ToHashSet();
            cohorts = cohorts.Where(c => keys.Contains(c.CohortYear * 100 + c.CohortMonth));
        }
        else if (fromMonth.HasValue && fromYear.HasValue && toMonth.HasValue && toYear.HasValue)
        {
            var keys = DashboardService.ExpandRangeKeys(fromMonth.Value, fromYear.Value, toMonth.Value, toYear.Value).ToHashSet();
            cohorts = cohorts.Where(c => keys.Contains(c.CohortYear * 100 + c.CohortMonth));
        }

        var omOcStores = await GetStoresForOmOcAsync(om, oc, soc, od);
        if (omOcStores != null) cohorts = cohorts.Where(c => omOcStores.Contains(c.Store));

        // Role-based store access is always applied — never bypassed by the
        // om/oc (or the caller's later explicit store) filter, which only
        // narrows further on top of it.
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        if (accessible != null) cohorts = cohorts.Where(c => accessible.Contains(c.Store));

        return cohorts.ToList();
    }

    private static DateOnly CohortCloseDate(int month, int year) =>
        new(year, month, DateTime.DaysInMonth(year, month));

    private static bool CohortReaches(int month, int year, int days) =>
        DateOnly.FromDateTime(DateTime.UtcNow) >= CohortCloseDate(month, year).AddDays(days);

    public async Task<List<string>> GetStoreListAsync(string role, string? assignedName)
    {
        var cohorts = await LoadEmployeeCohortsAsync(role, assignedName);
        return cohorts.Select(c => c.Store)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();
    }

    public async Task<List<RetentionMilestoneItem>> GetMilestonesAsync(string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var cohorts = await LoadEmployeeCohortsAsync(role, assignedName, fromMonth, fromYear, toMonth, toYear, om, oc, soc, od, months);
        if (MultiValueFilter.Split(store) is { } stores) cohorts = cohorts.Where(c => stores.Contains(c.Store)).ToList();

        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = new List<RetentionMilestoneItem>();
        foreach (var (days, label) in Milestones)
        {
            // Exclude ineligible cohorts: a still-active hire whose tenure hasn't yet
            // reached the milestone has an undetermined outcome and must not skew the
            // denominator — resigned employees are always eligible (their fate is known).
            var included = cohorts.Where(c => MetricsCalculationService.IsEligibleForMilestone(c.HireDate, c.TenureDays, days, asOf)).ToList();
            if (included.Count == 0)
            {
                result.Add(new RetentionMilestoneItem { Days = days, Label = label });
                continue;
            }
            var total = included.Count;
            var retained = included.Count(c => MetricsCalculationService.IsRetainedAtMilestone(c.TenureDays, days));
            var latest = included.OrderByDescending(c => c.CohortYear).ThenByDescending(c => c.CohortMonth).First();
            result.Add(new RetentionMilestoneItem
            {
                Days = days,
                Label = label,
                RetentionRate = MetricsCalculationService.RatePercent(retained, total),
                TotalHires = total,
                Retained = retained,
                ThroughCohortLabel = new DateOnly(latest.CohortYear, latest.CohortMonth, 1).ToString("MMM yyyy"),
            });
        }
        return result;
    }

    public async Task<List<SurvivalPoint>> GetSurvivalCurveAsync(string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var cohorts = await LoadEmployeeCohortsAsync(role, assignedName, fromMonth, fromYear, toMonth, toYear, om, oc, soc, od, months);
        if (MultiValueFilter.Split(store) is { } stores) cohorts = cohorts.Where(c => stores.Contains(c.Store)).ToList();

        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = new List<SurvivalPoint>();
        foreach (var (day, label) in CurvePoints)
        {
            var included = cohorts.Where(c => MetricsCalculationService.IsEligibleForMilestone(c.HireDate, c.TenureDays, day, asOf)).ToList();
            if (included.Count == 0) continue;
            var total = included.Count;
            var retained = included.Count(c => MetricsCalculationService.IsRetainedAtMilestone(c.TenureDays, day));
            result.Add(new SurvivalPoint
            {
                Day = day,
                Label = label,
                RetentionRate = MetricsCalculationService.RatePercent(retained, total),
                SampleSize = total,
            });
        }
        return result;
    }

    public async Task<List<RetentionTrendPoint>> GetTrendAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null)
    {
        // Always full history (like the Turnover page's Monthly Trend) — unaffected
        // by the discrete cohort-month filter used for the milestone cards above.
        var cohorts = await LoadEmployeeCohortsAsync(role, assignedName, om: om, oc: oc, soc: soc, od: od);
        if (MultiValueFilter.Split(store) is { } stores) cohorts = cohorts.Where(c => stores.Contains(c.Store)).ToList();

        var periods = cohorts.Select(c => (c.CohortMonth, c.CohortYear))
            .Distinct()
            .Where(p => !sinceYear.HasValue || p.CohortYear >= sinceYear.Value)
            .OrderBy(p => p.CohortYear).ThenBy(p => p.CohortMonth)
            .ToList();

        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = new List<RetentionTrendPoint>();
        foreach (var (month, year) in periods)
        {
            var cohortRows = cohorts.Where(c => c.CohortMonth == month && c.CohortYear == year).ToList();
            if (cohortRows.Count == 0) continue;

            var point = new RetentionTrendPoint { Label = new DateOnly(year, month, 1).ToString("MMM yy") };
            foreach (var (days, label) in Milestones)
            {
                // Same eligibility rule as GetMilestonesAsync/GetSurvivalCurveAsync:
                // a still-active member whose tenure hasn't yet reached this milestone
                // has an undetermined outcome and must not be counted as "retained" —
                // exclude them from both numerator and denominator instead of assuming
                // they'll survive to a milestone they haven't had time to reach.
                var eligible = cohortRows.Where(c => MetricsCalculationService.IsEligibleForMilestone(c.HireDate, c.TenureDays, days, asOf)).ToList();
                point.Rates[label] = eligible.Count == 0
                    ? null
                    : MetricsCalculationService.RatePercent(eligible.Count(c => MetricsCalculationService.IsRetainedAtMilestone(c.TenureDays, days)), eligible.Count);
                point.Provisional[label] = !CohortReaches(month, year, days);
            }
            result.Add(point);
        }
        return result;
    }

    public async Task<List<ChartDataItem>> GetStoreLeaderboardAsync(string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var cohorts = await LoadEmployeeCohortsAsync(role, assignedName, fromMonth, fromYear, toMonth, toYear, om, oc, soc, od, months);
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        var included = cohorts
            .Where(c => !string.IsNullOrWhiteSpace(c.Store) && MetricsCalculationService.IsEligibleForMilestone(c.HireDate, c.TenureDays, LeaderboardDays, asOf))
            .ToList();

        return included.GroupBy(c => c.Store)
            .Select(g => new ChartDataItem
            {
                Label = g.Key,
                Value = (int)MetricsCalculationService.RatePercent(g.Count(c => MetricsCalculationService.IsRetainedAtMilestone(c.TenureDays, LeaderboardDays)), g.Count(), 0),
            })
            .OrderByDescending(c => c.Value)
            .ToList();
    }

    public async Task<List<ChartDataItem>> GetTenureDistributionAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null)
    {
        var periods = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .ToListAsync();
        if (periods.Count == 0) return new List<ChartDataItem>();

        // Use the user-selected month/year as the snapshot anchor; fall back to latest.
        var anchor = (month.HasValue && year.HasValue && periods.Any(p => p.Month == month && p.Year == year))
            ? (Month: month.Value, Year: year.Value)
            : periods.Select(p => (p.Month, p.Year)).OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).First();

        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        var rowsQuery = _db.ActiveEmployees.Where(e => e.Month == anchor.Month && e.Year == anchor.Year && e.HireDate != null);
        if (accessible != null) rowsQuery = rowsQuery.Where(e => accessible.Contains(e.Store));
        if (MultiValueFilter.Split(store) is { } stores) rowsQuery = rowsQuery.Where(e => stores.Contains(e.Store));
        else if (await GetStoresForOmOcAsync(om, oc, soc, od) is { } omOcStores) rowsQuery = rowsQuery.Where(e => omOcStores.Contains(e.Store));
        var hireDates = await rowsQuery.Select(e => e.HireDate!.Value).ToListAsync();

        var asOf = new DateOnly(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month));

        return TenureBuckets
            .Select(b => new ChartDataItem
            {
                Label = b.Label,
                Value = hireDates.Count(hd => (asOf.DayNumber - hd.DayNumber) >= b.Min && (asOf.DayNumber - hd.DayNumber) < b.Max),
            })
            .Where(c => c.Value > 0)
            .ToList();
    }

    public async Task<List<StoreTenureRow>> GetTenureDistributionByStoreAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null)
    {
        var periods = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .ToListAsync();
        if (periods.Count == 0) return new List<StoreTenureRow>();

        // Use the user-selected month/year as the snapshot anchor; fall back to latest.
        var anchor = (month.HasValue && year.HasValue && periods.Any(p => p.Month == month && p.Year == year))
            ? (Month: month.Value, Year: year.Value)
            : periods.Select(p => (p.Month, p.Year)).OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).First();

        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        var rowsQuery = _db.ActiveEmployees.Where(e => e.Month == anchor.Month && e.Year == anchor.Year && e.HireDate != null);
        if (accessible != null) rowsQuery = rowsQuery.Where(e => accessible.Contains(e.Store));
        if (MultiValueFilter.Split(store) is { } stores) rowsQuery = rowsQuery.Where(e => stores.Contains(e.Store));
        else if (await GetStoresForOmOcAsync(om, oc, soc, od) is { } omOcStores) rowsQuery = rowsQuery.Where(e => omOcStores.Contains(e.Store));
        var rows = await rowsQuery.Select(e => new { e.Store, e.HireDate }).ToListAsync();

        var asOf = new DateOnly(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month));

        return rows.GroupBy(r => r.Store)
            .Select(g => new StoreTenureRow
            {
                StoreName = g.Key,
                Headcount = g.Count(),
                Buckets = TenureBuckets.Select(b => new ChartDataItem
                {
                    Label = b.Label,
                    Value = g.Count(x => (asOf.DayNumber - x.HireDate!.Value.DayNumber) >= b.Min && (asOf.DayNumber - x.HireDate!.Value.DayNumber) < b.Max)
                }).ToList()
            })
            .OrderByDescending(r => r.Headcount)
            .ToList();
    }

    /// <summary>Cumulative share of the CURRENT active workforce whose tenure has reached
    /// each day mark — a plain headcount snapshot, not a survival/eligibility model, so it
    /// stays meaningful even for a young company where few cohorts have had time to mature.</summary>
    public async Task<List<SurvivalPoint>> GetActiveTenureCurveAsync(string? store, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null)
    {
        var periods = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .ToListAsync();
        if (periods.Count == 0) return new List<SurvivalPoint>();

        var anchor = (month.HasValue && year.HasValue && periods.Any(p => p.Month == month && p.Year == year))
            ? (Month: month.Value, Year: year.Value)
            : periods.Select(p => (p.Month, p.Year)).OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).First();

        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        var rowsQuery = _db.ActiveEmployees.Where(e => e.Month == anchor.Month && e.Year == anchor.Year && e.HireDate != null);
        if (accessible != null) rowsQuery = rowsQuery.Where(e => accessible.Contains(e.Store));
        if (MultiValueFilter.Split(store) is { } stores) rowsQuery = rowsQuery.Where(e => stores.Contains(e.Store));
        else if (await GetStoresForOmOcAsync(om, oc, soc, od) is { } omOcStores) rowsQuery = rowsQuery.Where(e => omOcStores.Contains(e.Store));
        var hireDates = await rowsQuery.Select(e => e.HireDate!.Value).ToListAsync();
        if (hireDates.Count == 0) return new List<SurvivalPoint>();

        var asOf = new DateOnly(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month));
        var total = hireDates.Count;

        return CurvePoints
            .Select(p => new SurvivalPoint
            {
                Day = p.Days,
                Label = p.Label,
                RetentionRate = MetricsCalculationService.RatePercent(hireDates.Count(hd => (asOf.DayNumber - hd.DayNumber) >= p.Days), total),
                SampleSize = total,
            })
            .ToList();
    }

    /// <summary>Per-store share of the CURRENT active workforce that has reached 6 months'
    /// tenure, ranked best first — a plain headcount snapshot computed from ActiveEmployees
    /// only, so a blank Store on a resignation row can never hide a store from the chart.</summary>
    public async Task<List<ChartDataItem>> GetStoreRetentionRankingAsync(string? store, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null)
    {
        var periods = await _db.ActiveEmployees
            .Where(e => e.HireDate != null)
            .Select(e => new { e.Month, e.Year })
            .Distinct()
            .ToListAsync();
        if (periods.Count == 0) return new List<ChartDataItem>();

        var anchor = (month.HasValue && year.HasValue && periods.Any(p => p.Month == month && p.Year == year))
            ? (Month: month.Value, Year: year.Value)
            : periods.Select(p => (p.Month, p.Year)).OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).First();

        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        var rowsQuery = _db.ActiveEmployees.Where(e => e.Month == anchor.Month && e.Year == anchor.Year && e.HireDate != null);
        if (accessible != null) rowsQuery = rowsQuery.Where(e => accessible.Contains(e.Store));
        if (MultiValueFilter.Split(store) is { } stores) rowsQuery = rowsQuery.Where(e => stores.Contains(e.Store));
        else if (await GetStoresForOmOcAsync(om, oc, soc, od) is { } omOcStores) rowsQuery = rowsQuery.Where(e => omOcStores.Contains(e.Store));
        var rows = await rowsQuery.Select(e => new { e.Store, e.HireDate }).ToListAsync();

        var asOf = new DateOnly(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month));

        return rows.GroupBy(r => r.Store)
            .Select(g => new ChartDataItem
            {
                Label = g.Key,
                Value = (int)MetricsCalculationService.RatePercent(
                    g.Count(x => (asOf.DayNumber - x.HireDate!.Value.DayNumber) >= MetricsCalculationService.SixMonthRetentionDays),
                    g.Count(), 0),
            })
            .OrderByDescending(c => c.Value)
            .ToList();
    }

    public async Task<List<SmartInsightItem>> GetInsightsAsync(string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var insights = new List<SmartInsightItem>();
        const string milestoneKey = "1 Year";

        // 1. Recent vs. prior 1-year retention trend (up to 3 complete cohorts each side,
        // full history — not limited to the page's cohort-month filter).
        var trend = await GetTrendAsync(store, role, assignedName, om, oc, soc, od);
        var complete = trend.Where(t => t.Rates.TryGetValue(milestoneKey, out var r) && r.HasValue && !t.Provisional[milestoneKey]).ToList();
        if (complete.Count >= 2)
        {
            var recent = complete.TakeLast(Math.Min(3, complete.Count)).ToList();
            var priorCount = Math.Min(3, complete.Count - recent.Count);
            if (priorCount > 0)
            {
                var prior = complete.Skip(complete.Count - recent.Count - priorCount).Take(priorCount).ToList();
                var recentAvg = recent.Average(t => t.Rates[milestoneKey]!.Value);
                var priorAvg = prior.Average(t => t.Rates[milestoneKey]!.Value);
                var diff = Math.Round(recentAvg - priorAvg, 1);
                if (Math.Abs(diff) >= 1)
                    insights.Add(new SmartInsightItem
                    {
                        Icon = diff > 0 ? "bi-arrow-up-circle-fill" : "bi-arrow-down-circle-fill",
                        Color = diff > 0 ? "success" : "danger",
                        Title = diff > 0 ? "1-Year Retention Improving" : "1-Year Retention Slipping",
                        Description = $"{recentAvg:F1}% avg over the last {recent.Count} cohort(s) vs {priorAvg:F1}% before — {(diff > 0 ? "+" : "")}{diff}pt.",
                    });
            }
        }

        // 2. Best/worst store on 1-year retention (only meaningful company-wide).
        if (store == null)
        {
            var leaderboard = await GetStoreLeaderboardAsync(role, assignedName, fromMonth, fromYear, toMonth, toYear, om, oc, soc, od, months);
            if (leaderboard.Count > 0)
            {
                var best = leaderboard.First();
                insights.Add(new SmartInsightItem
                {
                    Icon = "bi-trophy-fill",
                    Color = "success",
                    Title = $"Best 1-Year Retention: {best.Label}",
                    Description = $"{best.Value}% of hires are still there after 1 year.",
                });
                var worst = leaderboard.Last();
                if (worst.Label != best.Label && worst.Value < 50)
                    insights.Add(new SmartInsightItem
                    {
                        Icon = "bi-exclamation-triangle-fill",
                        Color = "danger",
                        Title = $"Weakest 1-Year Retention: {worst.Label}",
                        Description = $"Only {worst.Value}% of hires are still there after 1 year.",
                    });
            }
        }

        // 3. Workforce maturity from the active-employee tenure distribution.
        var tenureDist = await GetTenureDistributionAsync(store, role, assignedName, om, oc, soc, od);
        var totalActive = tenureDist.Sum(t => t.Value);
        if (totalActive > 0)
        {
            var seasoned = tenureDist.Where(t => t.Label is "1–2 years" or "2–3 years" or "3–4 years" or "4–5 years" or "5+ years").Sum(t => t.Value);
            var pct = MetricsCalculationService.RatePercent(seasoned, totalActive, 0);
            insights.Add(new SmartInsightItem
            {
                Icon = "bi-shield-check",
                Color = pct >= 40 ? "success" : "secondary",
                Title = "Workforce Maturity",
                Description = $"{pct}% of the current active workforce has been here a year or more.",
            });
        }

        return insights;
    }

    // ── Additional snapshot-based charts (all plain "current active headcount"
    // views, same philosophy as the KPI cards / Team Tenure Curve above — no
    // eligibility/survival model, so every number here is a number a regular
    // user can read as-is: "X% of the team", "Y months on average"). ──────────

    private static readonly (string Label, int MinDays, int MaxDays)[] FirstResignationBuckets =
    {
        ("First week", 0, 7),
        ("1–4 weeks", 7, 30),
        ("1–3 months", 30, 90),
        ("3–6 months", 90, 180),
        ("6–12 months", 180, 365),
        ("1+ year", 365, int.MaxValue),
    };

    private async Task<(int Month, int Year)?> ResolveAnchorPeriodAsync(int? month, int? year)
    {
        var periods = await _db.ActiveEmployees.Where(e => e.HireDate != null).Select(e => new { e.Month, e.Year }).Distinct().ToListAsync();
        if (periods.Count == 0) return null;
        return (month.HasValue && year.HasValue && periods.Any(p => p.Month == month && p.Year == year))
            ? (month.Value, year.Value)
            : periods.Select(p => (p.Month, p.Year)).OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).First();
    }

    private async Task<IQueryable<ActiveEmployee>> BuildActiveSnapshotQueryAsync(int anchorMonth, int anchorYear,
        string? store, string role, string? assignedName, string? om, string? oc, string? soc, string? od)
    {
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        var q = _db.ActiveEmployees.Where(e => e.Month == anchorMonth && e.Year == anchorYear && e.HireDate != null);
        if (accessible != null) q = q.Where(e => accessible.Contains(e.Store));
        if (MultiValueFilter.Split(store) is { } stores) q = q.Where(e => stores.Contains(e.Store));
        else if (await GetStoresForOmOcAsync(om, oc, soc, od) is { } omOcStores) q = q.Where(e => omOcStores.Contains(e.Store));
        return q;
    }

    private async Task<Dictionary<string, (string Om, string Oc, string Soc, string Od)>> GetManagerMappingByStoreAsync()
    {
        var refs = await LoadLatestStoreReferenceCandidatesAsync();
        return refs.GroupBy(s => s.StoreName)
            .ToDictionary(g => g.Key, g =>
            {
                var latest = g.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).First();
                return (latest.OperationManager ?? "", latest.OperationConsultant ?? "", latest.SeniorOperationConsultant ?? "", latest.OperationDirector ?? "");
            });
    }

    /// <summary>Share of the current active team past 6 months' tenure, per job title.</summary>
    public async Task<List<ChartDataItem>> GetRetentionByJobTitleAsync(string? store, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null)
    {
        var anchor = await ResolveAnchorPeriodAsync(month, year);
        if (anchor == null) return new List<ChartDataItem>();
        var q = await BuildActiveSnapshotQueryAsync(anchor.Value.Month, anchor.Value.Year, store, role, assignedName, om, oc, soc, od);
        var rows = await q.Select(e => new { e.JobTitle, e.HireDate }).ToListAsync();
        var asOf = new DateOnly(anchor.Value.Year, anchor.Value.Month, DateTime.DaysInMonth(anchor.Value.Year, anchor.Value.Month));

        return rows.Where(r => !string.IsNullOrWhiteSpace(r.JobTitle)).GroupBy(r => r.JobTitle)
            .Select(g => new ChartDataItem
            {
                Label = g.Key,
                Value = (int)MetricsCalculationService.RatePercent(
                    g.Count(x => (asOf.DayNumber - x.HireDate!.Value.DayNumber) >= MetricsCalculationService.SixMonthRetentionDays),
                    g.Count(), 0),
            })
            .OrderByDescending(c => c.Value)
            .ToList();
    }

    /// <summary>Share of the current active team past 6 months' tenure, per gender.</summary>
    public async Task<List<ChartDataItem>> GetGenderRetentionAsync(string? store, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null)
    {
        var anchor = await ResolveAnchorPeriodAsync(month, year);
        if (anchor == null) return new List<ChartDataItem>();
        var q = await BuildActiveSnapshotQueryAsync(anchor.Value.Month, anchor.Value.Year, store, role, assignedName, om, oc, soc, od);
        var rows = await q.Select(e => new { e.Gender, e.HireDate }).ToListAsync();
        var asOf = new DateOnly(anchor.Value.Year, anchor.Value.Month, DateTime.DaysInMonth(anchor.Value.Year, anchor.Value.Month));

        return rows.Where(r => !string.IsNullOrWhiteSpace(r.Gender)).GroupBy(r => r.Gender)
            .Select(g => new ChartDataItem
            {
                Label = g.Key,
                Value = (int)MetricsCalculationService.RatePercent(
                    g.Count(x => (asOf.DayNumber - x.HireDate!.Value.DayNumber) >= MetricsCalculationService.SixMonthRetentionDays),
                    g.Count(), 0),
            })
            .OrderByDescending(c => c.Value)
            .ToList();
    }

    /// <summary>Average tenure (in whole months) of the current active team, per store.</summary>
    public async Task<List<ChartDataItem>> GetAverageTenureByStoreAsync(string? store, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null)
    {
        var anchor = await ResolveAnchorPeriodAsync(month, year);
        if (anchor == null) return new List<ChartDataItem>();
        var q = await BuildActiveSnapshotQueryAsync(anchor.Value.Month, anchor.Value.Year, store, role, assignedName, om, oc, soc, od);
        var rows = await q.Select(e => new { e.Store, e.HireDate }).ToListAsync();
        var asOf = new DateOnly(anchor.Value.Year, anchor.Value.Month, DateTime.DaysInMonth(anchor.Value.Year, anchor.Value.Month));

        return rows.GroupBy(r => r.Store)
            .Select(g => new ChartDataItem
            {
                Label = g.Key,
                Value = (int)Math.Round(g.Average(x => (asOf.DayNumber - x.HireDate!.Value.DayNumber) / 30.44)),
            })
            .OrderByDescending(c => c.Value)
            .ToList();
    }

    /// <summary>Average tenure (in whole months) of the current active team, grouped by the
    /// given manager dimension ("om", "oc", "soc" or "od") — same shape as the "By Operation
    /// Consultant &amp; Manager" tables elsewhere, but measuring tenure instead of turnover.</summary>
    public async Task<List<ManagerTenureRow>> GetAverageTenureByManagerAsync(string dimension, string role, string? assignedName,
        int? month = null, int? year = null)
    {
        var anchor = await ResolveAnchorPeriodAsync(month, year);
        if (anchor == null) return new List<ManagerTenureRow>();

        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        var q = _db.ActiveEmployees.Where(e => e.Month == anchor.Value.Month && e.Year == anchor.Value.Year && e.HireDate != null);
        if (accessible != null) q = q.Where(e => accessible.Contains(e.Store));
        var rows = await q.Select(e => new { e.Store, e.HireDate }).ToListAsync();

        var mapping = await GetManagerMappingByStoreAsync();
        Func<string, string> pick = dimension switch
        {
            "om" => s => mapping.TryGetValue(s, out var m) ? m.Om : "",
            "oc" => s => mapping.TryGetValue(s, out var m) ? m.Oc : "",
            "soc" => s => mapping.TryGetValue(s, out var m) ? m.Soc : "",
            "od" => s => mapping.TryGetValue(s, out var m) ? m.Od : "",
            _ => s => "",
        };
        var asOf = new DateOnly(anchor.Value.Year, anchor.Value.Month, DateTime.DaysInMonth(anchor.Value.Year, anchor.Value.Month));

        return rows
            .Select(r => new { r.Store, r.HireDate, Manager = pick(r.Store) })
            .Where(r => !string.IsNullOrWhiteSpace(r.Manager))
            .GroupBy(r => r.Manager)
            .Select(g => new ManagerTenureRow
            {
                Name = g.Key,
                Stores = g.Select(x => x.Store).Distinct().Count(),
                Headcount = g.Count(),
                AvgTenureMonths = Math.Round(g.Average(x => (asOf.DayNumber - x.HireDate!.Value.DayNumber) / 30.44), 1),
            })
            .OrderByDescending(r => r.AvgTenureMonths)
            .ToList();
    }

    /// <summary>How soon after being hired people who eventually resigned actually left —
    /// e.g. "34% resigned within their first month" — all-time, so it always has enough
    /// data to read even for a young company where long-term milestones don't yet.</summary>
    public async Task<List<ChartDataItem>> GetTimeToFirstResignationDistributionAsync(string? store, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var accessible = await _storeAccess.GetAccessibleStoreNamesAsync(role, assignedName);
        var q = _db.Resignations.Where(r => r.HireDate != null && r.ResignationDate != null);
        if (accessible != null) q = q.Where(r => accessible.Contains(r.Store));
        if (MultiValueFilter.Split(store) is { } stores) q = q.Where(r => stores.Contains(r.Store));
        else if (await GetStoresForOmOcAsync(om, oc, soc, od) is { } omOcStores) q = q.Where(r => omOcStores.Contains(r.Store));
        var tenures = await q.Select(r => r.ResignationDate!.Value.DayNumber - r.HireDate!.Value.DayNumber).ToListAsync();

        return FirstResignationBuckets
            .Select(b => new ChartDataItem { Label = b.Label, Value = tenures.Count(t => t >= b.MinDays && t < b.MaxDays) })
            .ToList();
    }

    /// <summary>New hires per month, all-time (both still-active and since-resigned hires
    /// count), for simple hiring-volume context alongside the retention charts above.</summary>
    public async Task<List<ChartDataItem>> GetMonthlyHiringVolumeAsync(string? store, string role, string? assignedName,
        string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null)
    {
        var cohorts = await LoadEmployeeCohortsAsync(role, assignedName, om: om, oc: oc, soc: soc, od: od);
        if (MultiValueFilter.Split(store) is { } stores) cohorts = cohorts.Where(c => stores.Contains(c.Store)).ToList();

        return cohorts
            .Where(c => !sinceYear.HasValue || c.CohortYear >= sinceYear.Value)
            .GroupBy(c => new { c.CohortMonth, c.CohortYear })
            .Select(g => new { g.Key.CohortMonth, g.Key.CohortYear, Count = g.Count() })
            .OrderBy(x => x.CohortYear).ThenBy(x => x.CohortMonth)
            .Select(x => new ChartDataItem { Label = new DateOnly(x.CohortYear, x.CohortMonth, 1).ToString("MMM yy"), Value = x.Count })
            .ToList();
    }
}
