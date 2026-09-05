using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IStoreActionPlanService
{
    /// <summary>The most recent Action Plan for a store (Active or Resolved) with its
    /// recommendations and notes. Returns null if the role/email can't access this
    /// store — deliberately indistinguishable from "store has no plan yet", matching
    /// this app's existing quiet-filtering access pattern rather than a 403.</summary>
    Task<StoreActionPlanDto?> GetForStoreAsync(string storeName, string role, string? email);

    /// <summary>Every store the given role/email can see, each with its current plan
    /// status ("Active" / "Resolved" / "None") and dynamically-resolved responsible party.</summary>

    /// <summary>Appends a note to the store's most recent plan. Caller must be
    /// Head_Manager or Operation_Consultant and have access to the store; fails if
    /// no plan exists yet for the store.</summary>
    Task<(bool success, string message, ActionPlanNoteDto? note)> AddNoteAsync(
        string storeName, string role, string? email, int authorUserId, string authorName, string noteText);

    /// <summary>Evaluates every store present in the given period's StoreReference
    /// data against the fixed turnover signal thresholds: creates a plan (with
    /// grouped recommendations) when a store has no active plan and a signal fires,
    /// adds newly-fired signals to an already-active plan, advances or resets each
    /// active plan's HealthyStreakCount, and auto-resolves plans healthy for 2
    /// consecutive monthly cycles. Safe to call more than once for the same period —
    /// re-running it does not create a second active plan for a store or duplicate
    /// a recommendation the plan already has.</summary>
    Task RunDetectionForPeriodAsync(int month, int year);

    // ────────────────────────────── Action Center ──────────────────────────────

    /// <summary>Company-wide (scoped to accessible stores) dashboard summary: active
    /// plan counts, this month's opened/resolved, average days to resolution,
    /// stalled/chronic/critical counts, top reasons by category, plans by region
    /// (Operation Manager), and a monthly opened-vs-resolved trend.</summary>
    Task<ActionCenterSummaryDto> GetActionCenterSummaryAsync(string role, string? email);

    /// <summary>Every accessible store with its plan status, computed severity,
    /// age, chronic/stalled flags, trend direction, and task-completion progress —
    /// the richer row set that makes the Action Center list page a real dashboard
    /// instead of plain cards.</summary>
    Task<List<ActionCenterStoreRowDto>> GetActionCenterStoresAsync(string role, string? email);

    /// <summary>Same plan lookup as GetForStoreAsync, but the DTO is filled in with
    /// the Action Center fields too: severity, chronic/stalled flags, metric
    /// snapshot history, assignment, and target date.</summary>
    Task<StoreActionPlanDto?> GetActionCenterDetailAsync(string storeName, string role, string? email);

    /// <summary>Marks a recommendation done/not-done. Same permission rule as notes
    /// (Head_Manager or Operation_Consultant with store access) plus Admin.</summary>
    Task<bool> ToggleRecommendationAsync(int recommendationId, bool isCompleted, string role, string? email, string actorName);

    /// <summary>Sets (or clears, when both are null/blank) the owner and target
    /// resolution date on a store's active plan. Admin only.</summary>
    Task<bool> SetAssignmentAsync(string storeName, string? assignedToName, DateOnly? targetResolutionDate, string role);

    /// <summary>Manually closes a store's active plan instead of waiting for the
    /// 2-clean-cycle auto-resolve rule. Admin only; requires a reason.</summary>
    Task<(bool success, string message)> ManualCloseAsync(string storeName, string reason, string role, string closedByName);

    /// <summary>Whether a signal has occurred in at least 2 of the store's last 3
    /// data-available evaluation periods (StoreReference-uploaded periods) up to
    /// and including the given period — reads the signal_occurrences log.</summary>
    Task<bool> IsSignalPersistentAsync(string storeName, string signalCode, int asOfMonth, int asOfYear);

    /// <summary>One-time historical backfill: replays the 6 signals whose source
    /// data (ActiveEmployees/Resignations/StoreReference/ExitInterviews) is
    /// retained per historical period against every past period with StoreReference
    /// data, logging signal_occurrences rows with IsBackfilled = true. Skips
    /// EARLY_WARNING_WATCHLIST (current-state only, not reconstructable) and any
    /// store/period/signal already logged. Admin-triggered, safe to re-run.</summary>
    Task<int> RunHistoricalSignalBackfillAsync();
}
