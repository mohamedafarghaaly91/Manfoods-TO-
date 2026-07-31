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
    Task<List<StoreActionPlanSummaryDto>> GetAccessibleStoresWithStatusAsync(string role, string? email);

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
}
