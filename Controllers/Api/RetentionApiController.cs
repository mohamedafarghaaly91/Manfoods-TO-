using Microsoft.AspNetCore.Mvc;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/retention")]
[RequireAuth]
public class RetentionApiController : ControllerBase
{
    private readonly IRetentionService _retention;
    private readonly IDashboardService _dashboard;

    public RetentionApiController(IRetentionService retention, IDashboardService dashboard)
    {
        _retention = retention;
        _dashboard = dashboard;
    }

    private (string role, string? assignedName) Identity() =>
        (HttpContext.Session.GetRole(), HttpContext.Session.GetEmail());

    [HttpGet("stores")]
    public async Task<IActionResult> Stores()
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetStoreListAsync(role, assignedName));
    }

    [HttpGet("operation-managers")]
    public async Task<IActionResult> OperationManagers()
    {
        var (role, assignedName) = Identity();
        return Ok(await _dashboard.GetOperationManagersAsync(null, null, role, assignedName));
    }

    [HttpGet("operation-consultants")]
    public async Task<IActionResult> OperationConsultants()
    {
        var (role, assignedName) = Identity();
        return Ok(await _dashboard.GetOperationConsultantsAsync(null, null, role, assignedName));
    }

    [HttpGet("milestones")]
    public async Task<IActionResult> Milestones([FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] int? toMonth, [FromQuery] int? toYear,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetMilestonesAsync(store, role, assignedName, fromMonth, fromYear, toMonth, toYear, om, oc, months));
    }

    [HttpGet("survival-curve")]
    public async Task<IActionResult> SurvivalCurve([FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] int? toMonth, [FromQuery] int? toYear,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetSurvivalCurveAsync(store, role, assignedName, fromMonth, fromYear, toMonth, toYear, om, oc, months));
    }

    [HttpGet("trend")]
    public async Task<IActionResult> Trend([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] int? sinceYear)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetTrendAsync(store, role, assignedName, om, oc, sinceYear));
    }

    [HttpGet("store-leaderboard")]
    public async Task<IActionResult> StoreLeaderboard(
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] int? toMonth, [FromQuery] int? toYear,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetStoreLeaderboardAsync(role, assignedName, fromMonth, fromYear, toMonth, toYear, om, oc, months));
    }

    [HttpGet("tenure-distribution")]
    public async Task<IActionResult> TenureDistribution([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc,
        [FromQuery] int? month, [FromQuery] int? year)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetTenureDistributionAsync(store, role, assignedName, om, oc, month, year));
    }

    [HttpGet("tenure-distribution-by-store")]
    public async Task<IActionResult> TenureDistributionByStore([FromQuery] string? store, [FromQuery] string? om, [FromQuery] string? oc,
        [FromQuery] int? month, [FromQuery] int? year)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetTenureDistributionByStoreAsync(store, role, assignedName, om, oc, month, year));
    }

    [HttpGet("insights")]
    public async Task<IActionResult> Insights([FromQuery] string? store,
        [FromQuery] int? fromMonth, [FromQuery] int? fromYear, [FromQuery] int? toMonth, [FromQuery] int? toYear,
        [FromQuery] string? om, [FromQuery] string? oc, [FromQuery] string? months)
    {
        var (role, assignedName) = Identity();
        return Ok(await _retention.GetInsightsAsync(store, role, assignedName, fromMonth, fromYear, toMonth, toYear, om, oc, months));
    }
}
