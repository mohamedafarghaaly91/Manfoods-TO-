using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

/// <summary>
/// Admin-only management of the Action Center severity-band cutoffs (how many
/// distinct fired signals make a plan Medium/High/Critical) — the Action
/// Plan Severity settings tab. See IActionPlanSeverityConfigService.
/// </summary>
[ApiController]
[Route("api/action-plan-severity")]
[EnableRateLimiting("api")]
[RequireAuth]
[RequireRole("Admin")]
public class ActionPlanSeverityConfigApiController : ControllerBase
{
    private readonly IActionPlanSeverityConfigService _severityConfig;

    public ActionPlanSeverityConfigApiController(IActionPlanSeverityConfigService severityConfig)
    {
        _severityConfig = severityConfig;
    }

    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await _severityConfig.GetAsync());

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory() => Ok(await _severityConfig.GetHistoryAsync());

    public class SaveRequest
    {
        public int MediumMinSignals { get; set; }
        public int HighMinSignals { get; set; }
        public int CriticalMinSignals { get; set; }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveRequest request)
    {
        var updatedByName = HttpContext.Session.GetAssignedName() ?? HttpContext.Session.GetEmail() ?? "";
        var (success, message) = await _severityConfig.SaveAsync(
            request.MediumMinSignals, request.HighMinSignals, request.CriticalMinSignals, updatedByName);
        if (!success) return BadRequest(message);
        return Ok();
    }
}
