using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MvcApp.Extensions;

namespace MvcApp.Filters;

public class RequireAdminAuthAttribute : SessionAuthFilterAttribute
{
    protected override IActionResult OnUnauthenticated() => new RedirectResult("/adminlogin");

    protected override IActionResult? OnRoleCheck(string role) =>
        role == "Admin" ? null : new RedirectResult("/adminlogin");

    protected override IActionResult? OnMustChangePasswordCheck(ActionExecutingContext context, ISession session)
    {
        if (!session.GetMustChangePassword()) return null;
        var controller = context.RouteData.Values["controller"]?.ToString();
        var action = context.RouteData.Values["action"]?.ToString();
        if (controller == "Account" && action == "ChangePassword") return null;
        return new RedirectResult("/admin/account/changepassword");
    }
}
