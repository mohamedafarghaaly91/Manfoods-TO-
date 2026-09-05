using MvcApp.Models;

namespace MvcApp.Services;

public interface IActionPlanSeverityConfigService
{
    Task<ActionPlanSeverityBandConfig> GetAsync();
    Task<(bool success, string message)> SaveAsync(int mediumMinSignals, int highMinSignals, int criticalMinSignals, string updatedByName);
    Task<List<ActionPlanSeverityBandHistory>> GetHistoryAsync();
}
