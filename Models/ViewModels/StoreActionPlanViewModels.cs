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
    public string SignalCode { get; set; } = "";
    public string Category { get; set; } = "";
    public string RecommendationText { get; set; } = "";
    public DateTime CreatedAt { get; set; }
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
}

/// <summary>One row per accessible store for the plan-overview list — status is
/// "Active" / "Resolved" (most recent plan) / "None" (no plan on record).</summary>
public class StoreActionPlanSummaryDto
{
    public string StoreName { get; set; } = "";
    public string PlanStatus { get; set; } = "None";
    public int? PlanId { get; set; }
    public string? ResponsibleName { get; set; }
    public string? ResponsibleRole { get; set; }
}
