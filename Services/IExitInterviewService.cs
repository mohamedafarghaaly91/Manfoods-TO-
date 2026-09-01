using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IExitInterviewService
{
    Task<ExitInterviewFilterOptions> GetFilterOptionsAsync(string role, string? assignedName);
    Task<List<PeriodItem>> GetAvailablePeriodsAsync();
    Task<List<ChartDataItem>> GetReasonsForLeavingAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<ChartDataItem>> GetWouldReturnAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<ChartDataItem>> GetOverallExperienceAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<ChartDataItem>> GetWorkloadConditionAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<ChartDataItem>> GetTrainingAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<ChartDataItem>> GetFairTreatmentAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<ChartDataItem>> GetWorkPressureReasonAsync(ExitInterviewFilter filter, string role, string? assignedName);
    Task<List<EngagementDriverItem>> GetEngagementDriversAsync(ExitInterviewFilter filter, string role, string? assignedName);

    /// <summary>Combined "would return" + "overall experience" positive-sentiment
    /// rate, for use in leader/consultant/manager scorecards.</summary>
    Task<ExitSentimentSummary> GetSentimentSummaryAsync(ExitInterviewFilter filter, string role, string? assignedName);

    /// <summary>Batched version of GetSentimentSummaryAsync for a whole Scorecard
    /// column at once — one query for every name in "names" instead of one query
    /// per name, avoiding an N+1 when the Scorecard has many leaders/OC/OM. Every
    /// name in "names" is guaranteed a key in the result, defaulting to an empty
    /// summary (TotalResponses=0, PositivePercent=0) when it has no matching exit
    /// interviews — the same shape GetSentimentSummaryAsync would return for it.</summary>
    Task<Dictionary<string, ExitSentimentSummary>> GetSentimentSummariesByDimensionAsync(
        string dimension, IReadOnlyCollection<string> names, string role, string? assignedName);

    Task<List<ExitInterviewCommentItem>> GetCommentsAsync(ExitInterviewFilter filter, string role, string? assignedName);
}
