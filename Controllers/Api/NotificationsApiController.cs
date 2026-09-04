using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Models.ViewModels;
using MvcApp.Services;

namespace MvcApp.Controllers.Api;

[ApiController]
[Route("api/notifications")]
[EnableRateLimiting("api")]
[RequireAuth]
public class NotificationsApiController : ControllerBase
{
    private readonly IStoreActionPlanService _actionPlans;
    private readonly IEarlyWarningService _earlyWarning;

    public NotificationsApiController(IStoreActionPlanService actionPlans, IEarlyWarningService earlyWarning)
    {
        _actionPlans = actionPlans;
        _earlyWarning = earlyWarning;
    }

    private (string role, string? assignedName) Identity() =>
        (HttpContext.Session.GetRole(), HttpContext.Session.GetEmail());

    /// <summary>Header notification-bell feed — computed live from Action Center
    /// (critical/stalled/overdue plans) and Early Warning (high-risk employees),
    /// scoped to the caller's role/store access exactly like every other page.
    /// Nothing is persisted: there's no read/unread state, just "what needs
    /// attention right now."</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (role, assignedName) = Identity();
        var items = new List<NotificationItem>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var stores = await _actionPlans.GetActionCenterStoresAsync(role, assignedName);
        foreach (var s in stores.Where(s => s.PlanStatus == "Active"))
        {
            if (s.Severity == "Critical")
                items.Add(new NotificationItem
                {
                    Type = "critical", Store = s.StoreName,
                    Title = "Critical action plan",
                    Description = $"{s.StoreName} has a Critical severity action plan open.",
                });
            if (s.IsStalled)
                items.Add(new NotificationItem
                {
                    Type = "stalled", Store = s.StoreName,
                    Title = "Stalled action plan",
                    Description = $"{s.StoreName}'s action plan hasn't improved recently.",
                });
            if (s.TargetResolutionDate.HasValue && s.TargetResolutionDate.Value < today)
                items.Add(new NotificationItem
                {
                    Type = "overdue", Store = s.StoreName,
                    Title = "Overdue target date",
                    Description = $"{s.StoreName}'s action plan passed its target resolution date.",
                });
        }

        var watchlist = await _earlyWarning.GetWatchlistAsync(null, role, assignedName);
        var highRisk = watchlist.Where(w => w.Stars >= 4).ToList();
        if (highRisk.Count > 0)
        {
            var storeCount = highRisk.Select(w => w.Store).Distinct().Count();
            items.Add(new NotificationItem
            {
                Type = "high-risk",
                Title = "High-risk employees",
                Description = $"{highRisk.Count} high-risk employee(s) flagged across {storeCount} store(s) on the Early Warning watchlist.",
            });
        }

        return Ok(items);
    }
}
