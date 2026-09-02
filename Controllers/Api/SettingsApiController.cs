using Microsoft.AspNetCore.Mvc;
using MvcApp.Filters;
using MvcApp.Models.ViewModels;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/settings")]
[RequireAuth]
public class SettingsApiController : ControllerBase
{
    private readonly IColorRulesService _colorRules;
    public SettingsApiController(IColorRulesService colorRules) { _colorRules = colorRules; }

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
        if (rules == null || rules.Count == 0) return BadRequest("At least one rule is required.");
        if (rules.Count(r => r.UpTo == null) != 1 || rules[^1].UpTo != null)
            return BadRequest("Exactly one rule must have an empty upper bound, and it must be the last one.");

        await _colorRules.SaveRulesAsync(metric, rules);
        return Ok();
    }
}
