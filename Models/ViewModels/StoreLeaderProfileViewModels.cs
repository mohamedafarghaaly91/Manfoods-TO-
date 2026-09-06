namespace MvcApp.Models.ViewModels;

public class StoreLeaderProfileViewModel
{
    public string Name { get; set; } = "";
    public ScorecardRow? Summary { get; set; }
    public List<LeaderHistoryRow> History { get; set; } = new();
    public ExitSentimentSummary ExitSentiment { get; set; } = new();
    public List<ChartDataItem> ExitReasons { get; set; } = new();
    public List<ChartDataItem> WouldReturn { get; set; } = new();
    public List<ChartDataItem> OverallExperience { get; set; } = new();
    public List<ChartDataItem> WorkloadCondition { get; set; } = new();
    public List<ChartDataItem> Training { get; set; } = new();
    public List<ChartDataItem> FairTreatment { get; set; } = new();
    public List<ChartDataItem> JobTitles { get; set; } = new();
    public List<EngagementDriverItem> EngagementDrivers { get; set; } = new();
    public List<ExitInterviewCommentItem> Comments { get; set; } = new();
    public List<ExitReasonTrendPoint> ReasonsTrend { get; set; } = new();
    public List<ExitReasonReturnItem> ReasonVsWouldReturn { get; set; } = new();
}