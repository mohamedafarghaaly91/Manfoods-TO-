using Microsoft.AspNetCore.Mvc;
using MvcApp.Extensions;
using MvcApp.Filters;
using MvcApp.Services;

namespace MvcApp.Areas.Admin.Controllers;

[Area("Admin")]
[RequireAdminAuth]
public class DashboardController : Controller
{
    private readonly IUploadService _uploads;
    private readonly IUserService _users;

    public DashboardController(IUploadService uploads, IUserService users)
    {
        _uploads = uploads;
        _users = users;
    }

    public IActionResult Analytics() => View();

    public IActionResult Turnover() => View();

    public IActionResult Reports() => View();

    public async Task<IActionResult> Uploads()
    {
        var logs = await _uploads.GetLogsAsync();
        return View(logs);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadActiveEmployees(MvcApp.Models.ViewModels.UploadViewModel vm)
    {
        if (!ModelState.IsValid || vm.File == null) { TempData["Error"] = "Please select a file and specify month/year."; return RedirectToAction("Uploads"); }
        try { var email = HttpContext.Session.GetEmail(); var (_, msg, _) = await _uploads.UploadActiveEmployeesAsync(vm.File, vm.Month, vm.Year, email); TempData["Success"] = msg; }
        catch (Exception ex) { TempData["Error"] = $"Upload failed: {ex.Message}"; }
        return RedirectToAction("Uploads");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadResignations(MvcApp.Models.ViewModels.UploadViewModel vm)
    {
        if (!ModelState.IsValid || vm.File == null) { TempData["Error"] = "Please select a file and specify month/year."; return RedirectToAction("Uploads"); }
        try { var email = HttpContext.Session.GetEmail(); var (_, msg, _) = await _uploads.UploadResignationsAsync(vm.File, vm.Month, vm.Year, email); TempData["Success"] = msg; }
        catch (Exception ex) { TempData["Error"] = $"Upload failed: {ex.Message}"; }
        return RedirectToAction("Uploads");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadStoreReference(MvcApp.Models.ViewModels.UploadViewModel vm)
    {
        if (!ModelState.IsValid || vm.File == null) { TempData["Error"] = "Please select a file and specify month/year."; return RedirectToAction("Uploads"); }
        try { var email = HttpContext.Session.GetEmail(); var (_, msg, _) = await _uploads.UploadStoreReferenceAsync(vm.File, vm.Month, vm.Year, email); TempData["Success"] = msg; }
        catch (Exception ex) { TempData["Error"] = $"Upload failed: {ex.Message}"; }
        return RedirectToAction("Uploads");
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequireRole("Admin_Full")]
    public async Task<IActionResult> DeleteUploadLog(int id)
    {
        await _uploads.DeleteLogAsync(id);
        TempData["Success"] = "Upload log and associated data deleted.";
        return RedirectToAction("Uploads");
    }

    [RequireRole("Admin_Full")]
    public async Task<IActionResult> Users()
    {
        var users = await _users.GetAllAsync();
        return View(users);
    }

    [RequireRole("Admin_Full")]
    public async Task<IActionResult> CreateUser()
    {
        var (managers, consultants) = await _users.GetAssignableNamesAsync();
        ViewBag.Managers = managers; ViewBag.Consultants = consultants;
        return View(new MvcApp.Models.ViewModels.CreateUserViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequireRole("Admin_Full")]
    public async Task<IActionResult> CreateUser(MvcApp.Models.ViewModels.CreateUserViewModel vm)
    {
        if (!ModelState.IsValid) { var (m, c) = await _users.GetAssignableNamesAsync(); ViewBag.Managers = m; ViewBag.Consultants = c; return View(vm); }
        await _users.CreateAsync(vm);
        TempData["Success"] = "User created successfully.";
        return RedirectToAction("Users");
    }

    [RequireRole("Admin_Full")]
    public async Task<IActionResult> EditUser(int id)
    {
        var user = await _users.GetByIdAsync(id);
        if (user == null) return NotFound();
        var (m, c) = await _users.GetAssignableNamesAsync();
        ViewBag.Managers = m; ViewBag.Consultants = c;
        return View(new MvcApp.Models.ViewModels.EditUserViewModel { Id = user.Id, Email = user.Email, Role = user.Role, AssignedName = user.AssignedName });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequireRole("Admin_Full")]
    public async Task<IActionResult> EditUser(int id, MvcApp.Models.ViewModels.EditUserViewModel vm)
    {
        vm.Id = id;
        if (!ModelState.IsValid) { var (m, c) = await _users.GetAssignableNamesAsync(); ViewBag.Managers = m; ViewBag.Consultants = c; return View(vm); }
        await _users.UpdateAsync(id, vm);
        TempData["Success"] = "User updated.";
        return RedirectToAction("Users");
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequireRole("Admin_Full")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _users.DeleteAsync(id);
        TempData["Success"] = "User deleted.";
        return RedirectToAction("Users");
    }
}
