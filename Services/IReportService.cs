using ClosedXML.Excel;

namespace MvcApp.Services;

public interface IReportService
{
    Task<XLWorkbook> BuildStoreComparisonReportAsync(int month, int year, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null);
    Task<XLWorkbook> BuildNinetyDayReportAsync(string role, string? assignedName, string? store = null);
    Task<XLWorkbook> BuildRetentionReportAsync(string role, string? assignedName, string? store = null);
    Task<XLWorkbook> BuildExitInterviewReportAsync(string role, string? assignedName, string? store = null, string? om = null, string? oc = null);
    Task<XLWorkbook> BuildScorecardReportAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, int? year = null);
    Task<XLWorkbook> BuildEarlyWarningReportAsync(string role, string? assignedName, string? store = null);
    Task<XLWorkbook> BuildTrendMatrixReportAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null, string? months = null);
    Task<XLWorkbook> BuildNinetyDayTrendMatrixReportAsync(string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null, int? sinceYear = null);
    Task<XLWorkbook> BuildActionCenterReportAsync(string role, string? assignedName);
    Task<XLWorkbook> BuildStoresOverviewReportAsync(int month, int year, string role, string? assignedName, string? om = null, string? oc = null, string? soc = null, string? od = null);
    Task<XLWorkbook> BuildWorkforceReportAsync(int month, int year, string role, string? assignedName, string? store = null, string? om = null, string? oc = null, string? soc = null, string? od = null, int? sinceYear = null);
    Task<XLWorkbook> BuildOcOmComparisonReportAsync(int month, int year, string role, string? assignedName, int? fromMonth = null, int? fromYear = null, string? om = null, string? oc = null, string? soc = null, string? od = null, string? months = null);
}
