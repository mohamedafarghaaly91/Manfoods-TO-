namespace MvcApp.Services;

/// <summary>
/// Single source of truth for the standardized business-metric formulas —
/// New Hires, 90-Day Early Leavers, and 6-Month Retention — approved by the
/// Product Owner to replace the redundant, mismatched per-service math that
/// used to live in DashboardService, RetentionService, ScorecardService,
/// NinetyDayTurnoverService, and EarlyWarningService.
///
/// Headcount is intentionally not represented here: every service already
/// computes it the same way (a snapshot count of the uploaded Active
/// Employees list for the given Month/Year), so there is no formula to
/// unify — it stays a plain query at each call site.
/// </summary>
internal static class MetricsCalculationService
{
    /// <summary>
    /// Go-live date for 90-day-turnover cohort analysis: only employees hired on
    /// or after this date are eligible "New Hires" for that metric. A single,
    /// dynamic date comparison — not a hardcoded year check — so it keeps working
    /// unchanged as future years' data arrives.
    /// </summary>
    public static readonly DateOnly GoLiveDate = new(2026, 1, 1);

    /// <summary>The early-leaver window for the 90-Day Turnover metric.</summary>
    public const int NinetyDayWindowDays = 90;

    /// <summary>The standardized 6-month retention milestone, in days.</summary>
    public const int SixMonthRetentionDays = 180;

    /// <summary>True once an employee's hire date reaches the 90-day cohort go-live threshold.</summary>
    public static bool IsPastGoLive(DateOnly hireDate) => hireDate >= GoLiveDate;

    /// <summary>A resignation counts as an early leaver once tenure is at or under the 90-day window.</summary>
    public static bool IsEarlyLeaver(int? tenureDays) => tenureDays.HasValue && tenureDays.Value <= NinetyDayWindowDays;

    /// <summary>
    /// 90-Day Turnover Rate = (resigned employees with tenure ≤ 90 days) ÷ (total New
    /// Hires in the period). Shared by NinetyDayTurnoverService, ScorecardService, and
    /// EarlyWarningService so all three report the identical figure for the same population.
    /// </summary>
    public static double NinetyDayRate(int totalNewHires, int earlyLeavers) => RatePercent(earlyLeavers, totalNewHires);

    /// <summary>
    /// Whether an employee's outcome is decided for the given retention milestone.
    /// Resigned employees are always eligible — their fate is already known, however
    /// early it happened. Still-active employees are only eligible once enough
    /// calendar time has passed since their hire date to know whether they would
    /// have reached the milestone; otherwise they would inflate the denominator with
    /// an undetermined outcome (a hire from last week isn't yet "6-month retained").
    /// </summary>
    public static bool IsEligibleForMilestone(DateOnly hireDate, int? tenureDays, int milestoneDays, DateOnly asOfDate) =>
        tenureDays.HasValue || (asOfDate.DayNumber - hireDate.DayNumber) >= milestoneDays;

    /// <summary>Retained at the given milestone: still active, or resigned only after exceeding it.</summary>
    public static bool IsRetainedAtMilestone(int? tenureDays, int milestoneDays) =>
        tenureDays == null || tenureDays.Value > milestoneDays;

    /// <summary>Shared percentage-rate rounding used by every turnover/retention/early-leaver metric.</summary>
    public static double RatePercent(int numerator, int denominator, int decimals = 1) =>
        denominator > 0 ? Math.Round(numerator * 100.0 / denominator, decimals) : 0;

    public static double RatePercent(double numerator, double denominator, int decimals = 1) =>
        denominator > 0 ? Math.Round(numerator * 100.0 / denominator, decimals) : 0;
}
