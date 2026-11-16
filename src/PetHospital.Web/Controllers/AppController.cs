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

    /// <summary>Trả file Excel</summary>
    protected FileContentResult Excel(string fileName, string sheet, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows) =>
        File(Spreadsheet.ToXlsx(sheet, headers, rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{fileName}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
}
