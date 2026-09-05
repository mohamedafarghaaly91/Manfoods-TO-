using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

/// <summary>
/// Admin-only management of which role owns each store's Action Plan — the
/// Action Plan Role settings page. See IActionPlanRoleService for the
/// default/override resolution rules this drives.
/// </summary>
[ApiController]
[Route("api/action-plan-role")]
[EnableRateLimiting("api")]
[RequireAuth]
[RequireRole("Admin")]
public class ActionPlanRoleApiController : ControllerBase
{
    private readonly IActionPlanRoleService _actionPlanRoles;

    public ActionPlanRoleApiController(IActionPlanRoleService actionPlanRoles)
    {
        _actionPlanRoles = actionPlanRoles;
    }

    [HttpGet("stores")]
    public async Task<IActionResult> GetStores() => Ok(await _actionPlanRoles.GetAllAsync());

    public class SetRoleRequest
    {
        public string? Role { get; set; }
    }

    [HttpPost("{store}/set"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(string store, [FromBody] SetRoleRequest request)
    {
        var setByName = HttpContext.Session.GetAssignedName() ?? HttpContext.Session.GetEmail() ?? "";
        var (success, message) = await _actionPlanRoles.SetOverrideAsync(store, request?.Role, setByName);
        if (!success) return BadRequest(message);
        return Ok();
    }
}
