using MvcApp.Models.ViewModels;

namespace MvcApp.Services;

public interface IRecommendationTemplateService
{
    /// <summary>All 23 templates (defaults merged with any saved overrides),
    /// grouped in catalog order — for the Settings editor.</summary>
    Task<List<RecommendationTemplate>> GetAllAsync();

    /// <summary>Saves one template's text and stamps UpdatedAtUtc = now.</summary>
    Task SaveAsync(string signalCode, string category, int index, string textEn, string textAr);
}
