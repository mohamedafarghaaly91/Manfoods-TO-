using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface ITableTotalColumnSettingsService
{
    Task<TableTotalColumnSettings> GetAsync();
    Task SaveAsync(TableTotalColumnSettings settings);
}