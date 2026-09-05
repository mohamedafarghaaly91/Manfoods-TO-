using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// Append-only audit trail of every change to ActionPlanSeverityBandConfig —
/// one row per save, snapshotting the full set of cutoffs that were in effect
/// after that change plus who made it and when.
/// </summary>
[Table("action_plan_severity_band_history")]
public class ActionPlanSeverityBandHistory
{
    [Column("id")]
    public int Id { get; set; }

    [Column("medium_min_signals")]
    public int MediumMinSignals { get; set; }

    [Column("high_min_signals")]
    public int HighMinSignals { get; set; }

    [Column("critical_min_signals")]
    public int CriticalMinSignals { get; set; }

    [Column("changed_by_name")]
    public string? ChangedByName { get; set; }

    [Column("changed_at")]
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
