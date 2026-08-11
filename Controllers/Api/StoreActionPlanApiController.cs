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
    /// Returns immediately (202 Accepted) and processes in the background so
    /// the request never times out regardless of how many periods/stores exist.
    /// </summary>
    [HttpPost("run-detection")]
    public async Task<IActionResult> RunDetectionAllPeriods()
    {
        var role = HttpContext.Session.GetRole();
        if (role != "Admin") return Forbid();

        var periods = await _dashboard.GetAvailablePeriodsAsync();
        if (periods.Count == 0) return Accepted(new { status = "no_periods", message = "No periods found." });

        // GetAvailablePeriodsAsync returns newest-first (correct for UI period
        // dropdowns, which this call also depends on elsewhere) — but detection's
        // HealthyStreakCount/LastEvaluatedMonth/auto-resolve logic requires
        // sequential, chronological (oldest→newest) processing per store. Reorder
        // the local copy only; do not touch GetAvailablePeriodsAsync itself.
        var orderedPeriods = periods.OrderBy(p => p.Year).ThenBy(p => p.Month).ToList();

        // Fire-and-forget: respond immediately so the browser doesn't time out,
        // then process every period in the background.
        _ = Task.Run(async () =>
        {
            foreach (var p in orderedPeriods)
            {
                try { await _actionPlans.RunDetectionForPeriodAsync(p.Month, p.Year); }
                catch { /* individual period failure should not stop the rest */ }
            }
        });

        return Accepted(new { status = "started", periods = periods.Count });
    }

    public class AddNoteRequest
    {
        public string NoteText { get; set; } = "";
    }
}
