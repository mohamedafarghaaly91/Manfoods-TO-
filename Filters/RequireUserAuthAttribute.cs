using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MvcApp.Extensions;

namespace MvcApp.Filters;

public class RequireUserAuthAttribute : SessionAuthFilterAttribute
{
    protected override IActionResult OnUnauthenticated() => new RedirectResult("/login");

    protected override IActionResult? OnRoleCheck(string role) =>
        role == "Admin" ? new RedirectResult("/login") : null;

    protected override IActionResult? OnMustChangePasswordCheck(ActionExecutingContext context, ISession session)
    {
        if (!session.GetMustChangePassword()) return null;
        var controller = context.RouteData.Values["controller"]?.ToString();
        var action = context.RouteData.Values["action"]?.ToString();
        if (controller == "Account" && action == "ChangePassword") return null;
        return new RedirectResult("/home/account/changepassword");
    }
}
