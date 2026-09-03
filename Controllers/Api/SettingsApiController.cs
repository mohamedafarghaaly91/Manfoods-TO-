using Microsoft.AspNetCore.Mvc;
using MvcApp.Filters;
using MvcApp.Models.ViewModels;
using MvcApp.Services;
using Microsoft.Extensions.Localization;
using MvcApp.Resources;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/settings")]
[RequireAuth]
public class SettingsApiController : ControllerBase
{
    private readonly IColorRulesService _colorRules;
    private readonly IStringLocalizer<SharedResource> _L;
    public SettingsApiController(IColorRulesService colorRules, IStringLocalizer<SharedResource> localizer) { _colorRules = colorRules; _L = localizer; }

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
}
