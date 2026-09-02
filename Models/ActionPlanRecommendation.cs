using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// One system-generated recommendation attached to a StoreActionPlan, from a
/// fixed template keyed by the signal that triggered it. Never edited after
/// creation; a signal that fires again on an already-active plan is simply
/// not re-added (see StoreActionPlanService's dedupe-by-SignalCode rule).
/// </summary>
[Table("action_plan_recommendations")]
public class ActionPlanRecommendation
{
    [Column("id")]
    public int Id { get; set; }

    [Column("store_action_plan_id")]
    public int StoreActionPlanId { get; set; }

    [Column("signal_code")]
    public string SignalCode { get; set; } = "";

    [Column("category")]
    public string Category { get; set; } = "";

    [Column("recommendation_text")]
    public string RecommendationText { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Action Center: turns each recommendation into a checkable task ──────
    [Column("is_completed")]
    public bool IsCompleted { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("completed_by_name")]
    public string? CompletedByName { get; set; }
}
