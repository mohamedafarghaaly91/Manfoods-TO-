namespace MvcApp.Services;

public interface IDataFreshnessService
{
    Task<DataFreshnessPeriod?> GetLatestDataPeriodAsync();
}

public sealed record DataFreshnessPeriod(int Month, int Year);