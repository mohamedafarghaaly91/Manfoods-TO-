using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// One detection cycle's metric readout for a StoreActionPlan — captured every
/// time StoreActionPlanService.EvaluateStoreAsync runs for a store with an
/// active plan, regardless of whether any signal fired that cycle. This is
/// what lets the Action Center show real "baseline vs. now" progress instead
/// of a single frozen snapshot from when the plan was created.
/// </summary>
[Table("action_plan_metric_snapshots")]
public class ActionPlanMetricSnapshot
{
    [Column("id")]
    public int Id { get; set; }

    [Column("store_action_plan_id")]
    public int StoreActionPlanId { get; set; }

    [Column("month")]
    public int Month { get; set; }

    [Column("year")]
    public int Year { get; set; }

    [Column("turnover_rate")]
    public double? TurnoverRate { get; set; }

    [Column("early_leaver_rate")]
    public double? EarlyLeaverRate { get; set; }

    [Column("retention_rate")]
    public double? RetentionRate { get; set; }

    [Column("signal_count")]
    public int SignalCount { get; set; }

    [Column("recorded_at")]
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
