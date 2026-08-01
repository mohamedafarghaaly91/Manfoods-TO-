using Microsoft.AspNetCore.Mvc;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/store-action-plan")]
[RequireAuth]
public class StoreActionPlanApiController : ControllerBase
{
    private readonly IStoreActionPlanService _actionPlans;
    private readonly IDashboardService _dashboard;

    public StoreActionPlanApiController(IStoreActionPlanService actionPlans, IDashboardService dashboard)
    {
        _actionPlans = actionPlans;
        _dashboard = dashboard;
    }

    [HttpGet]
    public async Task<IActionResult> GetForStore([FromQuery] string store)
    {
        if (string.IsNullOrWhiteSpace(store)) return BadRequest("store is required.");

        var role = HttpContext.Session.GetRole();
        var email = HttpContext.Session.GetEmail();
        var result = await _actionPlans.GetForStoreAsync(store, role, email);
        if (result == null) return NotFound();

        return Ok(result);
    }

    [HttpGet("stores")]
    public async Task<IActionResult> GetAccessibleStores()
    {
        var role = HttpContext.Session.GetRole();
        var email = HttpContext.Session.GetEmail();
        return Ok(await _actionPlans.GetAccessibleStoresWithStatusAsync(role, email));
    }

    [HttpPost("{store}/notes")]
    [RequireRole("Head_Manager", "Operation_Consultant")]
    public async Task<IActionResult> AddNote(string store, [FromBody] AddNoteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.NoteText)) return BadRequest("Note text is required.");

        var role = HttpContext.Session.GetRole();
        var email = HttpContext.Session.GetEmail();
        var userId = HttpContext.Session.GetUserId();
        if (userId == null) return Unauthorized();

        var authorName = HttpContext.Session.GetAssignedName() ?? email;

        var (success, message, note) = await _actionPlans.AddNoteAsync(store, role, email, userId.Value, authorName, request.NoteText);
        if (!success) return BadRequest(message);

        return Ok(note);
    }

    /// <summary>
    /// Admin-only: runs detection for every period that has uploaded data.
    /// Fills store_action_plans for periods uploaded before this feature existed.
    /// </summary>
    [HttpPost("run-detection")]
    public async Task<IActionResult> RunDetectionAllPeriods()
    {
        var role = HttpContext.Session.GetRole();
        if (role != "Admin") return Forbid();

        var periods = await _dashboard.GetAvailablePeriodsAsync();
        if (periods.Count == 0) return Ok(new { ran = 0, message = "No periods found." });

        foreach (var p in periods)
            await _actionPlans.RunDetectionForPeriodAsync(p.Month, p.Year);

        return Ok(new { ran = periods.Count, message = $"Detection ran for {periods.Count} period(s)." });
    }

    public class AddNoteRequest
    {
        public string NoteText { get; set; } = "";
    }
}
