using Microsoft.AspNetCore.Mvc;

namespace MvcApp.Filters;

public class RequireRoleAttribute : SessionAuthFilterAttribute
{
    private readonly string[] _roles;

    public RequireRoleAttribute(params string[] roles)
    {
        _roles = roles;
    }

    protected override IActionResult OnUnauthenticated() => new RedirectToActionResult("Login", "Account", null);

    protected override IActionResult? OnRoleCheck(string role)
    {
        if (_roles.Contains(role)) return null;

        // No authentication scheme is registered (auth here is custom,
        // session-based), so ForbidResult would try to resolve a default
        // forbid scheme and throw instead of denying access. Return a
        // plain 403 so an unauthorized role is reliably rejected.
        return new StatusCodeResult(StatusCodes.Status403Forbidden);
    }
}
