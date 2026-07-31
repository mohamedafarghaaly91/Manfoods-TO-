using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// The single active-or-resolved turnover Action Plan for one store. Only one
/// row may be Active per StoreName at a time (enforced by a partial unique
/// index — see scripts/migrate.sql); a resolved store that regresses later
/// gets a brand new plan rather than reopening this one.
/// </summary>
[Table("store_action_plans")]
public class StoreActionPlan
{
    [Column("id")]
    public int Id { get; set; }

    [Column("store_name")]
    public string StoreName { get; set; } = "";

    // "Active" | "Resolved"
    [Column("status")]
    public string Status { get; set; } = "Active";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("created_month")]
    public int CreatedMonth { get; set; }

    [Column("created_year")]
    public int CreatedYear { get; set; }

    [Column("resolved_at")]
    public DateTime? ResolvedAt { get; set; }

    // "Auto_Improvement" once resolution logic fires. No manual close path in V1.
    [Column("resolved_reason")]
    public string? ResolvedReason { get; set; }

    [Column("baseline_turnover_rate")]
    public double? BaselineTurnoverRate { get; set; }

    [Column("baseline_early_leaver_rate")]
    public double? BaselineEarlyLeaverRate { get; set; }

    [Column("baseline_retention_rate")]
    public double? BaselineRetentionRate { get; set; }

    [Column("detected_issues_summary")]
    public string DetectedIssuesSummary { get; set; } = "";

    [Column("healthy_streak_count")]
    public int HealthyStreakCount { get; set; }

    // Not part of the originally-specified column list — added because the
    // auto-resolve rule (2 *consecutive monthly cycles*) can't be evaluated
    // safely-to-rerun without knowing which period last moved the streak.
    // Without it, re-running detection twice for the same period (e.g. after
    // a single-file re-upload correction) would double-count that cycle.
    [Column("last_evaluated_month")]
    public int? LastEvaluatedMonth { get; set; }

    [Column("last_evaluated_year")]
    public int? LastEvaluatedYear { get; set; }
}
