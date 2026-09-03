using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Models.ViewModels;
using MvcApp.Services;
using Microsoft.Extensions.Localization;
using MvcApp.Resources;

namespace MvcApp.Areas.Admin.Controllers;

[Area("Admin")]
public class AccountController : Controller
{
    private readonly IAuthService _auth;
    private readonly IUserService _users;
    private readonly IStringLocalizer<SharedResource> _L;
    public AccountController(IAuthService auth, IUserService users, IStringLocalizer<SharedResource> localizer) { _auth = auth; _users = users; _L = localizer; }

    [HttpGet("/adminlogin")]
    public IActionResult Login()
    {
        if (HttpContext.Session.GetUserId() != null && HttpContext.Session.IsAdmin())
            return RedirectToAction("Workforce", "Dashboard", new { area = "Admin" });
        return View(new LoginViewModel());
    }

    [HttpPost("/adminlogin"), ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var (user, _) = await _auth.ValidateAsync(vm.Email, vm.Password);
        if (user == null) { ModelState.AddModelError("", _L["Msg_InvalidCredentials"]); return View(vm); }

        if (user.Role != "Admin")
        {
            ModelState.AddModelError("", _L["Msg_AdminOnly"]);
            return View(vm);
        }

        HttpContext.Session.SetUserSession(user.Id, user.Email, user.Role, user.AssignedName);
        return RedirectToAction("Workforce", "Dashboard", new { area = "Admin" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return Redirect("/adminlogin");
    }

    [HttpGet]
    public IActionResult Recover() => View(new AdminRecoveryViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Recover(AdminRecoveryViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        if (!await _users.VerifyRecoveryKeyAsync(vm.RecoveryKey))
        {
            ModelState.AddModelError("RecoveryKey", _L["Msg_InvalidRecoveryKey"]);
            return View(vm);
        }

        var ok = await _users.ResetAdminPasswordAsync(vm.Email, vm.NewPassword);
        if (!ok) { ModelState.AddModelError("Email", _L["Msg_NoAdminWithEmail"]); return View(vm); }

        TempData["Success"] = _L["Msg_AdminPasswordReset"].Value;
        return Redirect("/adminlogin");
    }

    [HttpGet]
    [RequireAdminAuth]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    [RequireAdminAuth]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var userId = HttpContext.Session.GetUserId();
        if (userId == null) return Redirect("/adminlogin");
        var ok = await _auth.ChangePasswordAsync(userId.Value, vm.CurrentPassword, vm.NewPassword);
        if (!ok) { ModelState.AddModelError("CurrentPassword", _L["Msg_CurrentPasswordIncorrect"]); return View(vm); }
        TempData["Success"] = _L["Msg_PasswordChanged"].Value;
        return RedirectToAction("ChangePassword");
    }
}
