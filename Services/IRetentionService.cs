using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IRetentionService
{
    /// <summary>Distinct store names across all hire cohorts, for filter dropdowns.</summary>
    Task<List<string>> GetStoreListAsync(string role, string? assignedName);

    /// <summary>Retention rate at 6mo/1/2/3/4/5-year marks since hire, aggregated
    /// across every cohort old enough to have reached that milestone.</summary>
    Task<List<RetentionMilestoneItem>> GetMilestonesAsync(string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null);

    /// <summary>% of hires still retained at each day mark since hire (0 through 5 years),
    /// aggregated across every cohort old enough to have reached that day.</summary>
    Task<List<SurvivalPoint>> GetSurvivalCurveAsync(string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null);

    /// <summary>Retention rate at every milestone (6mo through 5yr) per hire-month cohort,
    /// chronological order, across all available cohorts (optionally from sinceYear onward) —
    /// unaffected by the discrete cohort-month filter used elsewhere on the page.</summary>
    Task<List<RetentionTrendPoint>> GetTrendAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null);

    /// <summary>1-year retention rate per store, best first, across complete cohorts.</summary>
    Task<List<ChartDataItem>> GetStoreLeaderboardAsync(string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null);

    /// <summary>Tenure buckets for the active workforce as of the given month/year snapshot
    /// (falls back to the latest available upload when no month/year is supplied).</summary>
    Task<List<ChartDataItem>> GetTenureDistributionAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null);

    /// <summary>Same tenure buckets as above, broken out per store.</summary>
    Task<List<StoreTenureRow>> GetTenureDistributionByStoreAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null);

    /// <summary>Cumulative share of the CURRENT active workforce whose tenure has reached
    /// each day mark — a plain headcount snapshot, not the milestone eligibility model.</summary>
    Task<List<SurvivalPoint>> GetActiveTenureCurveAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null);

    /// <summary>Per-store share of the CURRENT active workforce past 6 months' tenure, best first.</summary>
    Task<List<ChartDataItem>> GetStoreRetentionRankingAsync(string? store, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? month = null, int? year = null);

    Task<List<SmartInsightItem>> GetInsightsAsync(string? store, string role, string? assignedName,
        int? fromMonth = null, int? fromYear = null, int? toMonth = null, int? toYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null);
}
