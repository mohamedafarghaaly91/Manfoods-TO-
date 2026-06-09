using ClosedXML.Excel;
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

    [HttpGet("admin/dashboard/download-template")]
    public IActionResult DownloadTemplate([FromQuery] string type)
    {
        using var wb = new XLWorkbook();
        string fileName;

        if (type == "active_employees")
        {
            fileName = "Template_Active_Employees.xlsx";
            var ws = wb.AddWorksheet("Active Employees");
            var headers = new[] { "Employee ID", "Name", "Store", "Job Title", "Grade", "Payroll Group", "Cost Center", "Gender", "Hire Date" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#C8102E");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            ws.Cell(2, 1).Value = "EMP001"; ws.Cell(2, 2).Value = "Ahmed Mohamed";
            ws.Cell(2, 3).Value = "Store 1"; ws.Cell(2, 4).Value = "Crew Member";
            ws.Cell(2, 5).Value = "L1"; ws.Cell(2, 6).Value = "Group A";
            ws.Cell(2, 7).Value = "CC001"; ws.Cell(2, 8).Value = "Male";
            ws.Cell(2, 9).Value = "2023-01-15";
            ws.Cell(3, 1).Value = "EMP002"; ws.Cell(3, 2).Value = "Sara Ali";
            ws.Cell(3, 3).Value = "Store 2"; ws.Cell(3, 4).Value = "Shift Manager";
            ws.Cell(3, 5).Value = "L3"; ws.Cell(3, 6).Value = "Group B";
            ws.Cell(3, 7).Value = "CC002"; ws.Cell(3, 8).Value = "Female";
            ws.Cell(3, 9).Value = "2022-06-01";
            ws.Columns().AdjustToContents();
        }
        else if (type == "resignations")
        {
            fileName = "Template_Resignations.xlsx";
            var ws = wb.AddWorksheet("Resignations");
            var headers = new[] { "Employee ID", "Name", "Store", "Job Title", "Gender", "Hire Date", "Resignation Date", "Payroll Group", "Cost Center" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#C8102E");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            ws.Cell(2, 1).Value = "EMP010"; ws.Cell(2, 2).Value = "Mohamed Hassan";
            ws.Cell(2, 3).Value = "Store 1"; ws.Cell(2, 4).Value = "Crew Member";
            ws.Cell(2, 5).Value = "Male"; ws.Cell(2, 6).Value = "2023-03-01";
            ws.Cell(2, 7).Value = "2025-05-20"; ws.Cell(2, 8).Value = "Group A";
            ws.Cell(2, 9).Value = "CC001";
            ws.Cell(3, 1).Value = "EMP011"; ws.Cell(3, 2).Value = "Nour Khaled";
            ws.Cell(3, 3).Value = "Store 3"; ws.Cell(3, 4).Value = "Cashier";
            ws.Cell(3, 5).Value = "Female"; ws.Cell(3, 6).Value = "2024-01-10";
            ws.Cell(3, 7).Value = "2025-05-28"; ws.Cell(3, 8).Value = "Group C";
            ws.Cell(3, 9).Value = "CC003";
            ws.Columns().AdjustToContents();
        }
        else if (type == "store_reference")
        {
            fileName = "Template_Store_Reference.xlsx";
            var ws = wb.AddWorksheet("Store Reference");
            var headers = new[] { "Store Name", "Store Leader", "Operation Consultant", "Operation Manager" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#C8102E");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            ws.Cell(2, 1).Value = "Store 1"; ws.Cell(2, 2).Value = "Khaled Ibrahim";
            ws.Cell(2, 3).Value = "Ahmed Samy"; ws.Cell(2, 4).Value = "Mohamed Nour";
            ws.Cell(3, 1).Value = "Store 2"; ws.Cell(3, 2).Value = "Sara Hassan";
            ws.Cell(3, 3).Value = "Mona Ali"; ws.Cell(3, 4).Value = "Mohamed Nour";
            ws.Cell(4, 1).Value = "Store 3"; ws.Cell(4, 2).Value = "Omar Tarek";
            ws.Cell(4, 3).Value = "Ahmed Samy"; ws.Cell(4, 4).Value = "Fatma Reda";
            ws.Columns().AdjustToContents();
        }
        else
        {
            return NotFound();
        }

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
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
