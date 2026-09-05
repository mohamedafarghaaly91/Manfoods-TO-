namespace MvcApp.Models.ViewModels;

/// <summary>Head Manager if assigned, otherwise Operation Consultant — resolved
/// live from the latest StoreReference data, never persisted (decision: "do not
/// permanently store the current responsible person on StoreActionPlan").</summary>
public class ResponsibleParty
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Email { get; set; } = "";
}

public class ActionPlanRecommendationDto
{
    public int Id { get; set; }
    public string SignalCode { get; set; } = "";
    public string Category { get; set; } = "";
    public string RecommendationText { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletedByName { get; set; }
}

public class ActionPlanNoteDto
{
    public string AuthorName { get; set; } = "";
    public string AuthorRole { get; set; } = "";
    public string NoteText { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class StoreActionPlanDto
{
    public int Id { get; set; }
    public string StoreName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int CreatedMonth { get; set; }
    public int CreatedYear { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedReason { get; set; }
    public double? BaselineTurnoverRate { get; set; }
    public double? BaselineEarlyLeaverRate { get; set; }
    public double? BaselineRetentionRate { get; set; }
    public string DetectedIssuesSummary { get; set; } = "";
    public int HealthyStreakCount { get; set; }
    public ResponsibleParty? ResponsibleParty { get; set; }
    public List<ActionPlanRecommendationDto> Recommendations { get; set; } = new();
    public List<ActionPlanNoteDto> Notes { get; set; } = new();
    public bool CanAddNotes { get; set; }

    // ── Action Center additions — all optional, ignored by the legacy detail page ──
    public string Severity { get; set; } = "None"; // Critical | High | Medium | Low | None
    public bool IsChronic { get; set; }
    public int HistoricalPlanCount { get; set; }
    public bool IsStalled { get; set; }
    public string? AssignedToName { get; set; }
    public DateOnly? TargetResolutionDate { get; set; }
    public string? ClosedByName { get; set; }
    public string? ManualCloseReason { get; set; }
    public bool CanManage { get; set; } // Admin — assign/target-date/manual-close
    public List<ActionCenterMetricSnapshotDto> MetricSnapshots { get; set; } = new();

    // Purely a reporting bucket derived from CreatedAt — independent of
    // TargetResolutionDate, never stored.
    public int ReportingQuarter { get; set; }
    public int ReportingYear { get; set; }
}

/// <summary>One detection cycle's metrics for the progress sparkline/trend on
/// the Action Center detail page.</summary>
public class ActionCenterMetricSnapshotDto
{
    public string Label { get; set; } = ""; // "MMM yy"
    public int Month { get; set; }
    public int Year { get; set; }
    public double? TurnoverRate { get; set; }
    public double? EarlyLeaverRate { get; set; }
    public double? RetentionRate { get; set; }
    public int SignalCount { get; set; }
}

/// <summary>One row per accessible store in the Action Center — richer than
/// StoreActionPlanSummaryDto: severity, age, chronic/stalled flags, trend, and
/// task-completion progress, so the list page can act as a real dashboard.</summary>
public class ActionCenterStoreRowDto
{
    public string StoreName { get; set; } = "";
    public string PlanStatus { get; set; } = "None"; // Active | Resolved | None
    public int? PlanId { get; set; }
    public string Severity { get; set; } = "None"; // Critical | High | Medium | Low | None
    public int SignalCount { get; set; }
    public int AgeDays { get; set; }
    public bool IsChronic { get; set; }
    public bool IsStalled { get; set; }
    public string Trend { get; set; } = "New"; // Improving | Worsening | Flat | New
    public string? ResponsibleName { get; set; }
    public string? ResponsibleRole { get; set; }
    public string? AssignedToName { get; set; }
    public DateOnly? TargetResolutionDate { get; set; }
    public int TasksTotal { get; set; }
    public int TasksCompleted { get; set; }
    public int ReportingQuarter { get; set; }
    public int ReportingYear { get; set; }
}

/// <summary>Company-wide Action Center dashboard summary.</summary>
public class ActionCenterSummaryDto
{
    public int TotalActive { get; set; }
    public int OpenedThisMonth { get; set; }
    public int ResolvedThisMonth { get; set; }
    public double? AvgDaysToResolution { get; set; }
    public int StalledCount { get; set; }
    public int ChronicCount { get; set; }
    public int CriticalCount { get; set; }
    public List<ChartDataItem> TopReasons { get; set; } = new(); // by recommendation Category, active plans only
    public List<ChartDataItem> ByRegion { get; set; } = new(); // active plan count by Operation Manager
    public List<ActionCenterTrendPointDto> MonthlyTrend { get; set; } = new();
}

public class ActionCenterTrendPointDto
{
    public string Label { get; set; } = ""; // "MMM yy"
    public int Opened { get; set; }
    public int Resolved { get; set; }
}
