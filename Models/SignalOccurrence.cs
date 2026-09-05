using System.ComponentModel.DataAnnotations.Schema;

namespace MvcApp.Models;

/// <summary>
/// One row per store/signal/period where a detection signal fired (or, for
/// historically-backfilled periods, is known to have fired based on
/// replaying ComputeSignalsAsync against retained source data). This is the
/// durable log the persistence rule ("2 of the last 3 data-available
/// evaluation periods") reads from — independent of StoreActionPlan/
/// ActionPlanRecommendation, so it survives plan resolution/creation
/// boundaries and lets persistence be evaluated even before any plan exists.
/// A period with no upload for a store has no row here at all for that
/// store/period/signal (not a "healthy" row) — see StoreActionPlanService's
/// persistence helper for how that absence is handled.
/// </summary>
[Table("signal_occurrences")]
public class SignalOccurrence
{
    [Column("id")]
    public int Id { get; set; }

    [Column("store_name")]
    public string StoreName { get; set; } = "";

    [Column("signal_code")]
    public string SignalCode { get; set; } = "";

    [Column("month")]
    public int Month { get; set; }

    [Column("year")]
    public int Year { get; set; }

    [Column("occurred_at")]
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    // True for rows written by the one-time historical backfill script
    // (replaying past periods) rather than live detection at the time the
    // period was actually evaluated. EARLY_WARNING_WATCHLIST is never
    // backfilled — see the backfill script for why.
    [Column("is_backfilled")]
    public bool IsBackfilled { get; set; }
}
