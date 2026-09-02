using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IColorRulesService
{
    Task<List<ColorRule>> GetRulesAsync(string metric);
    Task SaveRulesAsync(string metric, List<ColorRule> rules);
}
