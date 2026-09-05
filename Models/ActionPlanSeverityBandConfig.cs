using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// Admin-configurable cutoffs for how many distinct fired signals make an
/// Active plan Medium/High/Critical severity (see StoreActionPlanService.
/// ComputeSeverity). Single row (Id = 1) — there is only ever one active
/// configuration; changes are recomputed live for every plan, past and
/// present, and are audited in ActionPlanSeverityBandHistory.
/// </summary>
[Table("action_plan_severity_band_config")]
public class ActionPlanSeverityBandConfig
{
    [Column("id")]
    public int Id { get; set; } = 1;

    [Column("medium_min_signals")]
    public int MediumMinSignals { get; set; } = 1;

    [Column("high_min_signals")]
    public int HighMinSignals { get; set; } = 2;

    [Column("critical_min_signals")]
    public int CriticalMinSignals { get; set; } = 3;

    [Column("updated_by_name")]
    public string? UpdatedByName { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
