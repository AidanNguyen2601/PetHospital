using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PetHospital.Web.Controllers;

/// <summary>Bộ lọc danh sách dùng chung (tìm kiếm + phân trang)</summary>
public class ListFilter
{
    public string? Q { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = Paging.DefaultPageSize;
}

/// <summary>Các hàm dùng chung cho controller giao diện</summary>
public abstract class AppController : Controller
{
    protected readonly PetDbContext Db;
    protected AppController(PetDbContext db) => Db = db;

    private CurrentUser? _me;
    protected CurrentUser Me => _me ??= HttpContext.RequestServices.GetRequiredService<CurrentUser>();
    protected int CurrentUserId => Me.Id;
    protected T Svc<T>() where T : notnull => HttpContext.RequestServices.GetRequiredService<T>();

    /// <summary>Số dòng mỗi trang: lấy từ URL (?PageSize=) và nhớ theo từng danh sách (cookie)</summary>
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var filter = context.ActionArguments.Values.OfType<ListFilter>().FirstOrDefault();
        if (filter != null)
        {
            var cookie = $"pet.ps.{ControllerContext.ActionDescriptor.ControllerName}";
            if (Request.Query.ContainsKey(nameof(filter.PageSize)))
            {
                filter.PageSize = Paging.Clamp(filter.PageSize);
                Response.Cookies.Append(cookie, filter.PageSize.ToString(),
                    new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax, HttpOnly = true });
            }
            else if (int.TryParse(Request.Cookies[cookie], out var saved)) filter.PageSize = Paging.Clamp(saved);
            else filter.PageSize = Paging.Clamp(filter.PageSize);
            if (filter.Page < 1) filter.Page = 1;
        }
        base.OnActionExecuting(context);
    }

    protected IActionResult NotAllowed(string? message = null)
    {
        TempData["Error"] = message ?? "Bạn không có quyền với bản ghi này.";
        return RedirectToAction("Index", "Home");
    }

    protected IActionResult BackWithError(string message, string action, object? routeValues = null)
    {
        TempData["Error"] = message;
        return RedirectToAction(action, routeValues);
    }

    protected IActionResult BackWithSuccess(string message, string action, object? routeValues = null)
    {
        TempData["Success"] = message;
        return RedirectToAction(action, routeValues);
    }

    protected static List<int> ParseIds(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out var v) ? v : 0).Where(v => v > 0).Distinct().ToList();

    /// <summary>
    /// Phạm vi chi nhánh của báo cáo: trống = chi nhánh đang làm việc, 0 = toàn chuỗi (người dùng toàn chuỗi), &gt;0 = một chi nhánh được làm.
    /// Đặt luôn bộ lọc chi nhánh của DbContext theo phạm vi.
    /// </summary>
    protected async Task<ViewModels.ReportScope> ReportScopeAsync(int? branch)
    {
        var branches = (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                Db.Branches.OrderBy(b => b.SortOrder).ThenBy(b => b.Name)))
            .Where(b => Me.CanAccessBranch(b.Id)).ToList();
        int? scope = Me.BranchId;
        if (branch == 0 && Me.AllBranches) scope = null;
        else if (branch > 0 && branches.Any(b => b.Id == branch)) scope = branch;
        var options = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
        if (Me.AllBranches) options.Add(new("Toàn chuỗi (tất cả chi nhánh)", "0", scope == null));
        options.AddRange(branches.Select(b => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(b.Name, b.Id.ToString(), b.Id == scope)));
        Db.CurrentBranchId = scope;
        return new(scope, scope?.ToString() ?? "0", scope == null ? "Toàn chuỗi" : branches.FirstOrDefault(b => b.Id == scope)?.Name, options, branches);
    }

    /// <summary>Trả file Excel</summary>
    protected FileContentResult Excel(string fileName, string sheet, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows) =>
        File(Spreadsheet.ToXlsx(L.Text(sheet), headers.Select(L.Text).ToList(), rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{fileName}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
}
