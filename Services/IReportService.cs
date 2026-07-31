using ClosedXML.Excel;

namespace MvcApp.Services;

public interface IReportService
{
    Task<XLWorkbook> BuildSummaryReportAsync(int month, int year, string role, string? assignedName, string? store = null);
    Task<XLWorkbook> BuildStoreComparisonReportAsync(int month, int year, string role, string? assignedName, string? om = null, string? oc = null);
    Task<XLWorkbook> BuildNinetyDayReportAsync(string role, string? assignedName, string? store = null);
    Task<XLWorkbook> BuildRetentionReportAsync(string role, string? assignedName, string? store = null);
    Task<XLWorkbook> BuildExitInterviewReportAsync(string role, string? assignedName, string? store = null, string? om = null, string? oc = null);
    Task<XLWorkbook> BuildScorecardReportAsync(string role, string? assignedName, string? om = null, string? oc = null);
    Task<XLWorkbook> BuildEarlyWarningReportAsync(string role, string? assignedName, string? store = null);
    Task<XLWorkbook> BuildTrendMatrixReportAsync(string role, string? assignedName, string? om = null, string? oc = null, int? sinceYear = null, string? months = null);
    Task<XLWorkbook> BuildNinetyDayTrendMatrixReportAsync(string role, string? assignedName, string? om = null, string? oc = null, string? months = null, int? sinceYear = null);
}
