using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MvcApp.Filters;
using MvcApp.Models.ViewModels;
using MvcApp.Services;
using Microsoft.Extensions.Localization;
using MvcApp.Resources;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/settings")]
[EnableRateLimiting("api")]
[RequireAuth]
public class SettingsApiController : ControllerBase
{
    private readonly IColorRulesService _colorRules;
    private readonly ITableTotalColumnSettingsService _totalColumnSettings;
    private readonly IRecommendationTemplateService _recTemplates;
    private readonly IStringLocalizer<SharedResource> _L;
    public SettingsApiController(
        IColorRulesService colorRules,
        ITableTotalColumnSettingsService totalColumnSettings,
        IRecommendationTemplateService recTemplates,
        IStringLocalizer<SharedResource> localizer)
    {
        _colorRules = colorRules;
        _totalColumnSettings = totalColumnSettings;
        _recTemplates = recTemplates;
        _L = localizer;
    }

    [HttpGet("total-columns")]
    public async Task<IActionResult> GetTotalColumnSettings()
        => Ok(await _totalColumnSettings.GetAsync());

    [HttpPost("total-columns"), ValidateAntiForgeryToken, RequireRole("Admin")]
    public async Task<IActionResult> SaveTotalColumnSettings([FromBody] TableTotalColumnSettings settings)
    {
        if (settings == null) return BadRequest();
        await _totalColumnSettings.SaveAsync(settings);
        return Ok();
    }

    [HttpGet("color-rules/{metric}")]
    public async Task<IActionResult> GetColorRules(string metric)
    {
        if (!ColorRulesService.Metrics.Contains(metric, StringComparer.OrdinalIgnoreCase)) return NotFound();
        return Ok(await _colorRules.GetRulesAsync(metric));
    }

    [HttpPost("color-rules/{metric}"), ValidateAntiForgeryToken, RequireRole("Admin")]
    public async Task<IActionResult> SaveColorRules(string metric, [FromBody] List<ColorRule> rules)
    {
        if (!ColorRulesService.Metrics.Contains(metric, StringComparer.OrdinalIgnoreCase)) return NotFound();
        if (rules == null || rules.Count == 0) return BadRequest(_L["Api_AtLeastOneRule"].Value);
        if (rules.Count(r => r.UpTo == null) != 1 || rules[^1].UpTo != null)
            return BadRequest(_L["Api_OneOpenEndedRule"].Value);

        await _colorRules.SaveRulesAsync(metric, rules);
        return Ok();
    }

    [HttpGet("recommendation-templates")]
    public async Task<IActionResult> GetRecommendationTemplates() => Ok(await _recTemplates.GetAllAsync());

    public class SaveRecommendationTemplateRequest
    {
        public string SignalCode { get; set; } = "";
        public string Category { get; set; } = "";
        public int Index { get; set; }
        public string TextEn { get; set; } = "";
        public string TextAr { get; set; } = "";
    }

    [HttpPost("recommendation-templates"), ValidateAntiForgeryToken, RequireRole("Admin")]
    public async Task<IActionResult> SaveRecommendationTemplate([FromBody] SaveRecommendationTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.TextEn) || string.IsNullOrWhiteSpace(request?.TextAr))
            return BadRequest(_L["Api_BothLanguagesRequired"].Value);
        try
        {
            await _recTemplates.SaveAsync(request.SignalCode, request.Category, request.Index, request.TextEn, request.TextAr);
            return Ok();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
