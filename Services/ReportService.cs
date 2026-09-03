using ClosedXML.Excel;
using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public class ReportService : IReportService
{
    private const string BrandRed = "#DA291C";
    private const string BandFill = "#FAFAFA";
    private const string GridColor = "#D9D9D9";
    private const string PercentFormat = "0.0%";
    private const string DateFormat = "yyyy-mm-dd";

    private readonly IDashboardService _dashboard;
    private readonly INinetyDayTurnoverService _ninetyDay;
    private readonly IRetentionService _retention;
    private readonly IExitInterviewService _exitInterviews;
    private readonly IScorecardService _scorecard;
    private readonly IEarlyWarningService _earlyWarning;
    private readonly IStoreActionPlanService _actionPlans;

    public ReportService(
        IDashboardService dashboard,
        INinetyDayTurnoverService ninetyDay,
        IRetentionService retention,
        IExitInterviewService exitInterviews,
        IScorecardService scorecard,
        IEarlyWarningService earlyWarning,
        IStoreActionPlanService actionPlans)
    {
        _dashboard = dashboard;
        _ninetyDay = ninetyDay;
        _retention = retention;
        _exitInterviews = exitInterviews;
        _scorecard = scorecard;
        _earlyWarning = earlyWarning;
        _actionPlans = actionPlans;
    }

    private static void StyleHeader(IXLWorksheet ws, string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml(BrandRed);
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }

    private static IXLWorksheet AddSheet(XLWorkbook wb, string name)
    {
        var trimmed = name.Length > 31 ? name[..31] : name;
        return wb.AddWorksheet(trimmed);
    }

    private static void SetPercentCell(IXLCell cell, double value)
    {
        cell.Value = value / 100.0;
        cell.Style.NumberFormat.Format = PercentFormat;
    }

    private static void SetNullablePercentCell(IXLCell cell, double? value)
    {
        if (value.HasValue) SetPercentCell(cell, value.Value);
        else cell.Value = "—";
    }

    private static void SetDateCell(IXLCell cell, DateOnly date)
    {
        cell.Value = date.ToDateTime(TimeOnly.MinValue);
        cell.Style.DateFormat.Format = DateFormat;
    }

    private static void SetDateCell(IXLCell cell, DateTime? date)
    {
        if (date.HasValue)
        {
            cell.Value = date.Value;
            cell.Style.DateFormat.Format = DateFormat;
        }
        else cell.Value = "—";
    }

    /// <summary>Borders, zebra striping, auto-filter, and a frozen header
    /// row — applied once a sheet's data is fully written.</summary>
    private static void Finalize(IXLWorksheet ws)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;
        if (lastRow < 1 || lastCol < 1) { ws.Columns().AdjustToContents(); return; }

        var range = ws.Range(1, 1, lastRow, lastCol);
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorderColor = XLColor.FromHtml(GridColor);
        range.Style.Border.InsideBorderColor = XLColor.FromHtml(GridColor);

        for (int r = 3; r <= lastRow; r += 2)
            ws.Range(r, 1, r, lastCol).Style.Fill.BackgroundColor = XLColor.FromHtml(BandFill);

        if (lastRow > 1) range.SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    /// <summary>ClosedXML 0.102+ requires explicit double cast (int has no
    /// direct implicit conversion to XLCellValue) AND an explicit NumberFormat
    /// to prevent Excel from rendering the cell as blank.</summary>
    private static void SetIntCell(IXLCell cell, int value)
    {
        cell.Value = (double)value;
        cell.Style.NumberFormat.Format = "#,##0";
    }

    private static void WriteLabelValueSheet(XLWorkbook wb, string sheetName, string labelHeader, string valueHeader, IEnumerable<ChartDataItem> items, bool asPercent = false)
    {
        var ws = AddSheet(wb, sheetName);
        StyleHeader(ws, new[] { labelHeader, valueHeader });
        int r = 2;
        foreach (var item in items)
        {
            ws.Cell(r, 1).Value = item.Label;
            if (asPercent) SetPercentCell(ws.Cell(r, 2), item.Value);
            else SetIntCell(ws.Cell(r, 2), item.Value);
            r++;
        }
        Finalize(ws);
    }

    // ── Store Comparison ────────────────────────────────────
    private async Task AddStoreComparisonSheetAsync(XLWorkbook wb, int month, int year, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var rows = await _dashboard.GetStoreComparisonAsync(month, year, role, assignedName, om: om, oc: oc, soc: soc, od: od);
        var ws = AddSheet(wb, "Store Comparison");
        StyleHeader(ws, new[] { "Store", "OC", "OM", "Headcount", "New Hires", "Resignations", "Turnover Rate" });
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            ws.Cell(r + 2, 1).Value = row.StoreName;
            ws.Cell(r + 2, 2).Value = row.OperationConsultant;
            ws.Cell(r + 2, 3).Value = row.OperationManager;
            SetIntCell(ws.Cell(r + 2, 4), row.Headcount);
            SetIntCell(ws.Cell(r + 2, 5), row.NewHires);
            SetIntCell(ws.Cell(r + 2, 6), row.Resignations);
            SetPercentCell(ws.Cell(r + 2, 7), row.TurnoverRate);
        }
        Finalize(ws);
    }

    public async Task<XLWorkbook> BuildStoreComparisonReportAsync(int month, int year, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var wb = new XLWorkbook();
        await AddStoreComparisonSheetAsync(wb, month, year, role, assignedName, om, oc, soc, od);
        return wb;
    }

    // ── 90-Day Turnover ─────────────────────────────────────
    private async Task AddNinetyDaySheetsAsync(XLWorkbook wb, string role, string? assignedName, string? store = null)
    {
        var periods = await _ninetyDay.GetCohortPeriodsAsync();
        var trend = await _ninetyDay.GetTrendAsync(store, role, assignedName);

        var wsTrend = AddSheet(wb, "90D Cohort Trend");
        StyleHeader(wsTrend, new[] { "Cohort", "Total Hires", "Early Leavers", "Rate", "Provisional" });
        for (int i = 0; i < trend.Count; i++)
        {
            var t = trend[i];
            wsTrend.Cell(i + 2, 1).Value = t.Label;
            SetIntCell(wsTrend.Cell(i + 2, 2), t.TotalHires);
            SetIntCell(wsTrend.Cell(i + 2, 3), t.EarlyLeavers);
            SetPercentCell(wsTrend.Cell(i + 2, 4), t.Rate);
            wsTrend.Cell(i + 2, 5).Value = t.IsProvisional ? "Yes" : "No";
        }
        Finalize(wsTrend);

        if (periods.Count > 0)
        {
            var latest = periods[0]; // most recent first, per GetCohortPeriodsAsync contract
            var byStore = await _ninetyDay.GetByStoreAsync(latest.Month, latest.Year, role, assignedName);
            WriteLabelValueSheet(wb, $"90D By Store ({latest.Month}-{latest.Year})", "Store", "Early Leave Rate", byStore, asPercent: true);
        }

        var wsLeavers = AddSheet(wb, "90D Early Leavers (All)");
        StyleHeader(wsLeavers, new[] { "Cohort", "Name", "Store", "Job Title", "Hire Date", "Resignation Date", "Tenure (days)" });
        int row = 2;
        var reasonTotals = new Dictionary<string, int>();
        foreach (var p in periods)
        {
            var leavers = await _ninetyDay.GetEarlyLeaversAsync(p.Month, p.Year, store, role, assignedName);
            var cohortLabel = $"{p.Month}-{p.Year}";
            foreach (var lv in leavers)
            {
                wsLeavers.Cell(row, 1).Value = cohortLabel;
                wsLeavers.Cell(row, 2).Value = lv.Name;
                wsLeavers.Cell(row, 3).Value = lv.Store;
                wsLeavers.Cell(row, 4).Value = lv.JobTitle;
                SetDateCell(wsLeavers.Cell(row, 5), lv.HireDate);
                SetDateCell(wsLeavers.Cell(row, 6), lv.ResignationDate);
                SetIntCell(wsLeavers.Cell(row, 7), lv.TenureDays);
                row++;
            }

            var reasons = await _ninetyDay.GetEarlyLeaverReasonsAsync(p.Month, p.Year, store, role, assignedName);
            foreach (var reason in reasons)
                reasonTotals[reason.Label] = reasonTotals.GetValueOrDefault(reason.Label) + reason.Value;
        }
        Finalize(wsLeavers);

        WriteLabelValueSheet(wb, "90D Reasons (Aggregated)", "Reason", "Count",
            reasonTotals.OrderByDescending(kv => kv.Value).Select(kv => new ChartDataItem { Label = kv.Key, Value = kv.Value }));
    }

    public async Task<XLWorkbook> BuildNinetyDayReportAsync(string role, string? assignedName, string? store = null)
    {
        var wb = new XLWorkbook();
        await AddNinetyDaySheetsAsync(wb, role, assignedName, store);
        return wb;
    }

    // ── Retention ───────────────────────────────────────────
    private async Task AddRetentionSheetsAsync(XLWorkbook wb, string role, string? assignedName, string? store = null)
    {
        var milestones = await _retention.GetMilestonesAsync(store, role, assignedName);
        var survival = await _retention.GetSurvivalCurveAsync(store, role, assignedName);
        var trend = await _retention.GetTrendAsync(store, role, assignedName);
        var leaderboard = await _retention.GetStoreLeaderboardAsync(role, assignedName);
        var tenureDist = await _retention.GetTenureDistributionAsync(store, role, assignedName);
        var insights = await _retention.GetInsightsAsync(store, role, assignedName);

        var wsMilestones = AddSheet(wb, "Retention Milestones");
        StyleHeader(wsMilestones, new[] { "Days", "Retention Rate", "Total Hires", "Retained", "Through Cohort" });
        for (int i = 0; i < milestones.Count; i++)
        {
            var m = milestones[i];
            SetIntCell(wsMilestones.Cell(i + 2, 1), m.Days);
            SetPercentCell(wsMilestones.Cell(i + 2, 2), m.RetentionRate);
            SetIntCell(wsMilestones.Cell(i + 2, 3), m.TotalHires);
            SetIntCell(wsMilestones.Cell(i + 2, 4), m.Retained);
            wsMilestones.Cell(i + 2, 5).Value = m.ThroughCohortLabel;
        }
        Finalize(wsMilestones);

        var wsSurvival = AddSheet(wb, "Survival Curve");
        StyleHeader(wsSurvival, new[] { "Day", "Retention Rate", "Sample Size" });
        for (int i = 0; i < survival.Count; i++)
        {
            var s = survival[i];
            SetIntCell(wsSurvival.Cell(i + 2, 1), s.Day);
            SetPercentCell(wsSurvival.Cell(i + 2, 2), s.RetentionRate);
            SetIntCell(wsSurvival.Cell(i + 2, 3), s.SampleSize);
        }
        Finalize(wsSurvival);

        var milestoneLabels = new[] { "6 Months", "1 Year", "2 Years", "3 Years", "4 Years", "5 Years" };
        var wsTrend = AddSheet(wb, "Retention Trend");
        StyleHeader(wsTrend, milestoneLabels.SelectMany(l => new[] { l, "Provisional" }).Prepend("Cohort").ToArray());
        for (int i = 0; i < trend.Count; i++)
        {
            var t = trend[i];
            wsTrend.Cell(i + 2, 1).Value = t.Label;
            for (int m = 0; m < milestoneLabels.Length; m++)
            {
                var label = milestoneLabels[m];
                SetNullablePercentCell(wsTrend.Cell(i + 2, 2 + m * 2), t.Rates.TryGetValue(label, out var rate) ? rate : null);
                wsTrend.Cell(i + 2, 3 + m * 2).Value = t.Provisional.TryGetValue(label, out var prov) && prov ? "Yes" : "No";
            }
        }
        Finalize(wsTrend);

        WriteLabelValueSheet(wb, "Store Leaderboard (1yr)", "Store", "Retention Rate", leaderboard, asPercent: true);
        WriteLabelValueSheet(wb, "Workforce Tenure", "Tenure Bucket", "Employees", tenureDist);

        var wsInsights = AddSheet(wb, "Retention Insights");
        StyleHeader(wsInsights, new[] { "Insight", "Description" });
        for (int i = 0; i < insights.Count; i++)
        {
            wsInsights.Cell(i + 2, 1).Value = insights[i].Title;
            wsInsights.Cell(i + 2, 2).Value = insights[i].Description;
        }
        Finalize(wsInsights);
    }

    public async Task<XLWorkbook> BuildRetentionReportAsync(string role, string? assignedName, string? store = null)
    {
        var wb = new XLWorkbook();
        await AddRetentionSheetsAsync(wb, role, assignedName, store);
        return wb;
    }

    // ── Exit Interviews (aggregate only — never names or IDs) ──
    private async Task AddExitInterviewSheetsAsync(XLWorkbook wb, string role, string? assignedName, ExitInterviewFilter? filterOverride = null)
    {
        var filter = filterOverride ?? new ExitInterviewFilter();

        var reasons = await _exitInterviews.GetReasonsForLeavingAsync(filter, role, assignedName);
        var wouldReturn = await _exitInterviews.GetWouldReturnAsync(filter, role, assignedName);
        var overallExperience = await _exitInterviews.GetOverallExperienceAsync(filter, role, assignedName);
        var workload = await _exitInterviews.GetWorkloadConditionAsync(filter, role, assignedName);
        var drivers = await _exitInterviews.GetEngagementDriversAsync(filter, role, assignedName);
        var comments = await _exitInterviews.GetCommentsAsync(filter, role, assignedName);

        WriteLabelValueSheet(wb, "EI Reasons for Leaving", "Reason", "Count", reasons);
        WriteLabelValueSheet(wb, "EI Would Return", "Answer", "Count", wouldReturn);
        WriteLabelValueSheet(wb, "EI Overall Experience", "Answer", "Count", overallExperience);
        WriteLabelValueSheet(wb, "EI Workload Condition", "Answer", "Count", workload);

        var wsDrivers = AddSheet(wb, "EI Engagement Drivers");
        StyleHeader(wsDrivers, new[] { "Driver", "Positive", "Total Responses" });
        for (int i = 0; i < drivers.Count; i++)
        {
            wsDrivers.Cell(i + 2, 1).Value = drivers[i].Label;
            SetPercentCell(wsDrivers.Cell(i + 2, 2), drivers[i].PositivePercent);
            SetIntCell(wsDrivers.Cell(i + 2, 3), drivers[i].TotalResponses);
        }
        Finalize(wsDrivers);

        var wsComments = AddSheet(wb, "EI Comments (Anonymous)");
        StyleHeader(wsComments, new[] { "Store", "Store Leader", "Question", "Comment", "Submitted At" });
        for (int i = 0; i < comments.Count; i++)
        {
            var c = comments[i];
            wsComments.Cell(i + 2, 1).Value = c.Store;
            wsComments.Cell(i + 2, 2).Value = c.StoreLeader;
            wsComments.Cell(i + 2, 3).Value = c.QuestionLabel;
            wsComments.Cell(i + 2, 4).Value = c.Text;
            SetDateCell(wsComments.Cell(i + 2, 5), c.SubmittedAt);
        }
        Finalize(wsComments);
    }

    public async Task<XLWorkbook> BuildExitInterviewReportAsync(string role, string? assignedName, string? store = null, string? om = null, string? oc = null)
    {
        var wb = new XLWorkbook();
        await AddExitInterviewSheetsAsync(wb, role, assignedName, new ExitInterviewFilter { Store = store, OperationConsultant = oc, OperationManager = om });
        return wb;
    }

    // ── Scorecard ───────────────────────────────────────────
    private async Task AddScorecardSheetAsync(XLWorkbook wb, string dimension, string sheetName, string nameHeader, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, int? year = null)
    {
        var rows = await _scorecard.GetScorecardAsync(dimension, role, assignedName, om, oc, soc, od, months, year);
        var ws = AddSheet(wb, sheetName);
        StyleHeader(ws, new[] { nameHeader, "Stores", "Headcount", "Turnover Rate", "90-Day Early Leave", "180-Day Retention", "Exit Sentiment", "Exit Responses" });
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            ws.Cell(i + 2, 1).Value = r.Name;
            SetIntCell(ws.Cell(i + 2, 2), r.StoreCount);
            SetIntCell(ws.Cell(i + 2, 3), r.Headcount);
            SetPercentCell(ws.Cell(i + 2, 4), r.TurnoverRate);
            SetPercentCell(ws.Cell(i + 2, 5), r.EarlyLeaver90Rate);
            SetPercentCell(ws.Cell(i + 2, 6), r.Retention180Rate);
            if (r.ExitResponseCount > 0) SetPercentCell(ws.Cell(i + 2, 7), r.ExitSentimentPercent);
            else ws.Cell(i + 2, 7).Value = "—";
            SetIntCell(ws.Cell(i + 2, 8), r.ExitResponseCount);
        }
        Finalize(ws);
    }

    private async Task AddScorecardSheetsAsync(XLWorkbook wb, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, int? year = null)
    {
        await AddScorecardSheetAsync(wb, "leader", "Scorecard Store Leaders", "Store Leader", role, assignedName, om, oc, soc, od, months, year);
        await AddScorecardSheetAsync(wb, "oc", "Scorecard Op. Consultants", "Operation Consultant", role, assignedName, om, oc, soc, od, months, year);
        await AddScorecardSheetAsync(wb, "om", "Scorecard Op. Managers", "Operation Manager", role, assignedName, om, oc, soc, od, months, year);
    }

    public async Task<XLWorkbook> BuildScorecardReportAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, int? year = null)
    {
        var wb = new XLWorkbook();
        await AddScorecardSheetsAsync(wb, role, assignedName, om, oc, soc, od, months, year);
        return wb;
    }

    // ── Early Warning ───────────────────────────────────────
    private async Task AddEarlyWarningSheetsAsync(XLWorkbook wb, string role, string? assignedName, string? store = null)
    {
        var summary = await _earlyWarning.GetSummaryAsync(store, role, assignedName);
        var watchlist = await _earlyWarning.GetWatchlistAsync(store, role, assignedName);

        var wsSummary = AddSheet(wb, "Early Warning Summary");
        StyleHeader(wsSummary, new[] { "Metric", "Value" });
        wsSummary.Cell(2, 1).Value = "Total On Watchlist"; SetIntCell(wsSummary.Cell(2, 2), summary.TotalWatchlist);
        wsSummary.Cell(3, 1).Value = "High Risk (4–5 stars)"; SetIntCell(wsSummary.Cell(3, 2), summary.HighRiskCount);
        wsSummary.Cell(4, 1).Value = "In First 90 Days"; SetIntCell(wsSummary.Cell(4, 2), summary.NewHireWindowCount);
        wsSummary.Cell(5, 1).Value = "Company Baseline Early-Leave Rate"; SetPercentCell(wsSummary.Cell(5, 2), summary.CompanyBaselineRate);
        Finalize(wsSummary);

        var wsWatchlist = AddSheet(wb, "Early Warning Watchlist");
        StyleHeader(wsWatchlist, new[] { "Name", "Store", "Job Title", "Hire Date", "Tenure (days)", "Risk Stars (1-5)", "Raw Score", "Reasons" });
        for (int i = 0; i < watchlist.Count; i++)
        {
            var w = watchlist[i];
            wsWatchlist.Cell(i + 2, 1).Value = w.Name;
            wsWatchlist.Cell(i + 2, 2).Value = w.Store;
            wsWatchlist.Cell(i + 2, 3).Value = w.JobTitle;
            SetDateCell(wsWatchlist.Cell(i + 2, 4), w.HireDate);
            SetIntCell(wsWatchlist.Cell(i + 2, 5), w.TenureDays);
            wsWatchlist.Cell(i + 2, 6).Value = new string('★', w.Stars) + new string('☆', 5 - w.Stars);
            SetIntCell(wsWatchlist.Cell(i + 2, 7), w.RiskScore);
            wsWatchlist.Cell(i + 2, 8).Value = string.Join(" | ", w.Reasons.Select(r => r.Type));
        }
        Finalize(wsWatchlist);
    }

    public async Task<XLWorkbook> BuildEarlyWarningReportAsync(string role, string? assignedName, string? store = null)
    {
        var wb = new XLWorkbook();
        await AddEarlyWarningSheetsAsync(wb, role, assignedName, store);
        return wb;
    }

    // ── Trend Matrix ─────────────────────────────────────────
    private static void WriteTrendMatrixSheet(XLWorkbook wb, string sheetName, TrendMatrixResult result)
    {
        var ws = AddSheet(wb, sheetName);

        var headerList = new List<string> { "OC", "OM", "Store" };
        headerList.AddRange(result.Periods);
        headerList.Add("Total");
        StyleHeader(ws, headerList.ToArray());

        for (int i = 0; i < result.Rows.Count; i++)
        {
            var row = result.Rows[i];
            ws.Cell(i + 2, 1).Value = row.OperationConsultant;
            ws.Cell(i + 2, 2).Value = row.OperationManager;
            ws.Cell(i + 2, 3).Value = row.StoreName;
            // Sum of this row's own displayed period rates (not an average/pooled
            // rate) — matches the "Total" column on the Turnover/90-Day Turnover
            // dashboard pages (mxTotalRate), which replaced their old AVG column.
            double total = 0;
            for (int p = 0; p < result.Periods.Count; p++)
            {
                if (row.PeriodRates.TryGetValue(result.Periods[p], out var rate) && rate.HasValue)
                {
                    SetPercentCell(ws.Cell(i + 2, 4 + p), rate.Value);
                    total += rate.Value;
                }
                else
                {
                    ws.Cell(i + 2, 4 + p).Value = "—";
                }
            }
            SetPercentCell(ws.Cell(i + 2, 4 + result.Periods.Count), total);
        }
        Finalize(ws);
    }

    private async Task AddTrendMatrixSheetAsync(XLWorkbook wb, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null, string? months = null)
    {
        var result = await _dashboard.GetTrendMatrixAsync(role, assignedName, om, oc, soc, od, sinceYear, months);
        WriteTrendMatrixSheet(wb, "Turnover Trend Matrix", result);
    }

    public async Task<XLWorkbook> BuildTrendMatrixReportAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null, string? months = null)
    {
        var wb = new XLWorkbook();
        await AddTrendMatrixSheetAsync(wb, role, assignedName, om, oc, soc, od, sinceYear, months);
        return wb;
    }

    // ── 90-Day Trend Matrix ────────────────────────────────────
    public async Task<XLWorkbook> BuildNinetyDayTrendMatrixReportAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, int? sinceYear = null)
    {
        var wb = new XLWorkbook();
        var result = await _ninetyDay.GetTrendMatrixAsync(role, assignedName, om, oc, soc, od, months, sinceYear);
        WriteTrendMatrixSheet(wb, "90-Day Trend Matrix", result);
        return wb;
    }

    // ── Action Center ────────────────────────────────────────
    private async Task AddActionCenterSheetsAsync(XLWorkbook wb, string role, string? assignedName)
    {
        var summary = await _actionPlans.GetActionCenterSummaryAsync(role, assignedName);
        var stores = await _actionPlans.GetActionCenterStoresAsync(role, assignedName);

        var wsSummary = AddSheet(wb, "AC Summary");
        StyleHeader(wsSummary, new[] { "Metric", "Value" });
        wsSummary.Cell(2, 1).Value = "Total Active Plans"; SetIntCell(wsSummary.Cell(2, 2), summary.TotalActive);
        wsSummary.Cell(3, 1).Value = "Opened This Month"; SetIntCell(wsSummary.Cell(3, 2), summary.OpenedThisMonth);
        wsSummary.Cell(4, 1).Value = "Resolved This Month"; SetIntCell(wsSummary.Cell(4, 2), summary.ResolvedThisMonth);
        wsSummary.Cell(5, 1).Value = "Avg. Days to Resolution";
        if (summary.AvgDaysToResolution.HasValue) SetIntCell(wsSummary.Cell(5, 2), (int)Math.Round(summary.AvgDaysToResolution.Value));
        else wsSummary.Cell(5, 2).Value = "—";
        wsSummary.Cell(6, 1).Value = "Stalled Plans"; SetIntCell(wsSummary.Cell(6, 2), summary.StalledCount);
        wsSummary.Cell(7, 1).Value = "Chronic Stores"; SetIntCell(wsSummary.Cell(7, 2), summary.ChronicCount);
        wsSummary.Cell(8, 1).Value = "Critical Severity"; SetIntCell(wsSummary.Cell(8, 2), summary.CriticalCount);
        Finalize(wsSummary);

        WriteLabelValueSheet(wb, "AC Top Reasons", "Category", "Active Plans", summary.TopReasons);
        WriteLabelValueSheet(wb, "AC By Region (OM)", "Operation Manager", "Active Plans", summary.ByRegion);

        var wsTrend = AddSheet(wb, "AC Monthly Trend");
        StyleHeader(wsTrend, new[] { "Month", "Opened", "Resolved" });
        for (int i = 0; i < summary.MonthlyTrend.Count; i++)
        {
            var t = summary.MonthlyTrend[i];
            wsTrend.Cell(i + 2, 1).Value = t.Label;
            SetIntCell(wsTrend.Cell(i + 2, 2), t.Opened);
            SetIntCell(wsTrend.Cell(i + 2, 3), t.Resolved);
        }
        Finalize(wsTrend);

        var wsStores = AddSheet(wb, "AC Stores");
        StyleHeader(wsStores, new[] { "Store", "Plan Status", "Severity", "Signals", "Age (days)", "Chronic", "Stalled", "Trend", "Responsible", "Assigned To", "Target Date", "Tasks Done" });
        for (int i = 0; i < stores.Count; i++)
        {
            var s = stores[i];
            wsStores.Cell(i + 2, 1).Value = s.StoreName;
            wsStores.Cell(i + 2, 2).Value = s.PlanStatus;
            wsStores.Cell(i + 2, 3).Value = s.Severity;
            SetIntCell(wsStores.Cell(i + 2, 4), s.SignalCount);
            SetIntCell(wsStores.Cell(i + 2, 5), s.AgeDays);
            wsStores.Cell(i + 2, 6).Value = s.IsChronic ? "Yes" : "No";
            wsStores.Cell(i + 2, 7).Value = s.IsStalled ? "Yes" : "No";
            wsStores.Cell(i + 2, 8).Value = s.Trend;
            wsStores.Cell(i + 2, 9).Value = s.ResponsibleName ?? "—";
            wsStores.Cell(i + 2, 10).Value = s.AssignedToName ?? "—";
            SetDateCell(wsStores.Cell(i + 2, 11), s.TargetResolutionDate?.ToDateTime(TimeOnly.MinValue));
            wsStores.Cell(i + 2, 12).Value = s.TasksTotal > 0 ? $"{s.TasksCompleted}/{s.TasksTotal}" : "—";
        }
        Finalize(wsStores);
    }

    public async Task<XLWorkbook> BuildActionCenterReportAsync(string role, string? assignedName)
    {
        var wb = new XLWorkbook();
        await AddActionCenterSheetsAsync(wb, role, assignedName);
        return wb;
    }

    // ── Stores Overview ──────────────────────────────────────
    private async Task AddStoresOverviewSheetAsync(XLWorkbook wb, int month, int year, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var comparison = await _dashboard.GetStoreComparisonAsync(month, year, role, assignedName, om: om, oc: oc, soc: soc, od: od);
        var acStores = await _actionPlans.GetActionCenterStoresAsync(role, assignedName);
        var watchlist = await _earlyWarning.GetWatchlistAsync(null, role, assignedName);

        var acByStore = acStores.ToDictionary(s => s.StoreName, s => s);
        var highRiskByStore = watchlist.Where(w => w.Stars >= 4)
            .GroupBy(w => w.Store).ToDictionary(g => g.Key, g => g.Count());

        var ws = AddSheet(wb, "Stores Overview");
        StyleHeader(ws, new[] { "Store", "OC", "OM", "Headcount", "New Hires", "Resignations", "Turnover Rate", "Action Plan Status", "Severity", "High-Risk Employees" });
        for (int i = 0; i < comparison.Count; i++)
        {
            var row = comparison[i];
            ws.Cell(i + 2, 1).Value = row.StoreName;
            ws.Cell(i + 2, 2).Value = row.OperationConsultant;
            ws.Cell(i + 2, 3).Value = row.OperationManager;
            SetIntCell(ws.Cell(i + 2, 4), row.Headcount);
            SetIntCell(ws.Cell(i + 2, 5), row.NewHires);
            SetIntCell(ws.Cell(i + 2, 6), row.Resignations);
            SetPercentCell(ws.Cell(i + 2, 7), row.TurnoverRate);
            var ac = acByStore.GetValueOrDefault(row.StoreName);
            ws.Cell(i + 2, 8).Value = ac?.PlanStatus ?? "None";
            ws.Cell(i + 2, 9).Value = ac?.Severity ?? "None";
            SetIntCell(ws.Cell(i + 2, 10), highRiskByStore.GetValueOrDefault(row.StoreName));
        }
        Finalize(ws);
    }

    public async Task<XLWorkbook> BuildStoresOverviewReportAsync(int month, int year, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null)
    {
        var wb = new XLWorkbook();
        await AddStoresOverviewSheetAsync(wb, month, year, role, assignedName, om, oc, soc, od);
        return wb;
    }

    // ── Workforce ────────────────────────────────────────────
    private async Task AddWorkforceSheetsAsync(XLWorkbook wb, int month, int year, string role, string? assignedName, string? store = null, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null)
    {
        var kpi = await _dashboard.GetKpisAsync(month, year, store, role, assignedName, om: om, oc: oc, soc: soc, od: od);
        var byJobTitle = await _dashboard.GetHeadcountByJobTitleAsync(month, year, store, role, assignedName, om: om, oc: oc, soc: soc, od: od);
        var byPayrollGroup = await _dashboard.GetHeadcountByPayrollGroupAsync(month, year, store, role, assignedName, om: om, oc: oc, soc: soc, od: od);
        var byTenure = await _dashboard.GetHeadcountByTenureAsync(month, year, store, role, assignedName, om: om, oc: oc, soc: soc, od: od);
        var byGender = await _dashboard.GetGenderBreakdownAsync(month, year, store, role, assignedName, om: om, oc: oc, soc: soc, od: od);
        var trend = await _dashboard.GetHeadcountTrendAsync(store, role, assignedName, om, oc, soc, od, sinceYear);

        var wsKpi = AddSheet(wb, "Workforce KPIs");
        StyleHeader(wsKpi, new[] { "Metric", "Value" });
        wsKpi.Cell(2, 1).Value = "Total Headcount"; SetIntCell(wsKpi.Cell(2, 2), kpi.TotalHeadcount);
        wsKpi.Cell(3, 1).Value = "New Hires"; SetIntCell(wsKpi.Cell(3, 2), kpi.NewHires);
        wsKpi.Cell(4, 1).Value = "Total Resignations"; SetIntCell(wsKpi.Cell(4, 2), kpi.TotalResignations);
        wsKpi.Cell(5, 1).Value = "Turnover Rate"; SetPercentCell(wsKpi.Cell(5, 2), kpi.TurnoverRate);
        Finalize(wsKpi);

        WriteLabelValueSheet(wb, "Headcount By Job Title", "Job Title", "Headcount", byJobTitle);
        WriteLabelValueSheet(wb, "Headcount By Payroll Group", "Payroll Group", "Headcount", byPayrollGroup);
        WriteLabelValueSheet(wb, "Headcount By Tenure", "Tenure Bucket", "Headcount", byTenure);
        WriteLabelValueSheet(wb, "Headcount By Gender", "Gender", "Headcount", byGender);

        var wsTrend = AddSheet(wb, "Headcount Trend");
        StyleHeader(wsTrend, new[] { "Period", "Headcount" });
        for (int i = 0; i < trend.Count; i++)
        {
            wsTrend.Cell(i + 2, 1).Value = trend[i].Label;
            SetIntCell(wsTrend.Cell(i + 2, 2), trend[i].Value);
        }
        Finalize(wsTrend);
    }

    public async Task<XLWorkbook> BuildWorkforceReportAsync(int month, int year, string role, string? assignedName, string? store = null, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null)
    {
        var wb = new XLWorkbook();
        await AddWorkforceSheetsAsync(wb, month, year, role, assignedName, store, om, oc, soc, od, sinceYear);
        return wb;
    }

    // ── OC/OM Comparison ─────────────────────────────────────
    private static void WriteOcOmSheet(XLWorkbook wb, string sheetName, string nameHeader, List<OcOmRow> rows)
    {
        var ws = AddSheet(wb, sheetName);
        StyleHeader(ws, new[] { nameHeader, "Stores", "Headcount", "Resignations", "Avg. Turnover Rate" });
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            ws.Cell(i + 2, 1).Value = r.Name;
            SetIntCell(ws.Cell(i + 2, 2), r.StoreCount);
            SetIntCell(ws.Cell(i + 2, 3), r.TotalHeadcount);
            SetIntCell(ws.Cell(i + 2, 4), r.TotalResignations);
            SetPercentCell(ws.Cell(i + 2, 5), r.AvgTurnoverRate);
        }
        Finalize(ws);
    }

    private async Task AddOcOmComparisonSheetsAsync(XLWorkbook wb, int month, int year, string role, string? assignedName, int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var result = await _dashboard.GetOcOmAnalysisAsync(month, year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months);
        WriteOcOmSheet(wb, "By Operation Consultant", "Operation Consultant", result.OcRows);
        WriteOcOmSheet(wb, "By Operation Manager", "Operation Manager", result.OmRows);
        WriteOcOmSheet(wb, "By Senior Op. Consultant", "Senior Operation Consultant", result.SocRows);
        WriteOcOmSheet(wb, "By Operation Director", "Operation Director", result.OdRows);
    }

    public async Task<XLWorkbook> BuildOcOmComparisonReportAsync(int month, int year, string role, string? assignedName, int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null)
    {
        var wb = new XLWorkbook();
        await AddOcOmComparisonSheetsAsync(wb, month, year, role, assignedName, fromMonth, fromYear, om, oc, soc, od, months);
        return wb;
    }
}
