using System.Text.Json;
using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using PetHospital.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Controllers;

/// <summary>
/// Báo cáo động: quản trị dữ liệu tự thiết kế báo cáo bằng câu lệnh SELECT hoặc chọn View (có tham số, biểu đồ, dòng tổng);
/// người dùng có quyền xem báo cáo chạy, xuất Excel, in.
/// </summary>
[RequirePerm(Perm.Reports)]
public class DynamicReportsController : AppController
{
    public DynamicReportsController(PetDbContext db) : base(db) { }

    private DynamicReportEngine Engine => Svc<DynamicReportEngine>();

    /// <summary>Người dùng được xem báo cáo: người thiết kế xem mọi báo cáo; người khác theo danh sách vai trò</summary>
    public static bool CanView(CurrentUser me, DynamicReport r) =>
        me.Has(Perm.ReportDesign) || (r.IsActive && (me.IsAdmin || !r.RoleList().Any() || r.RoleList().Any(me.InRole)));

    // ===================== Chạy báo cáo =====================

    public async Task<IActionResult> Show(int id, DateTime? from, DateTime? to, int? branch, string? export)
    {
        var r = await Db.DynamicReports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (r == null || !CanView(Me, r)) return NotFound();
        var today = DateTime.Today;
        var f = (from ?? new DateTime(today.Year, today.Month, 1)).Date;
        var t = (to ?? today).Date;
        if (t < f) (f, t) = (t, f);
        var scope = await ReportScopeAsync(branch);
        var page = new ReportPage
        {
            Name = r.Name, Icon = "database", Dated = DynamicReportEngine.UsesDates(r), BranchSelect = DynamicReportEngine.UsesBranch(r),
            From = f, To = t, Scope = scope, ChartMoney = true
        };

        // Tham số tự khai báo: ?p.Ten=giá trị
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["From"] = f, ["To"] = t, ["ToEnd"] = t.AddDays(1), ["BranchId"] = scope.BranchId, ["UserId"] = Me.Id
        };
        var prms = DynamicReportEngine.Params(r);
        var inputs = new Dictionary<string, string>();
        var errors = new List<string>();
        foreach (var p in prms)
        {
            var text = Request.Query.ContainsKey("p." + p.Name) ? Request.Query["p." + p.Name].ToString() : DynamicReportEngine.DefaultText(p);
            inputs[p.Name] = text;
            page.Extra["p." + p.Name] = text;
            values[p.Name] = DynamicReportEngine.Convert(p, text, out var err);
            if (err != null) errors.Add(err);
        }
        ViewBag.Report = r;
        ViewBag.Params = prms;
        ViewBag.Inputs = inputs;
        ViewBag.Page = page;

        var table = new ReportTable { Title = r.Name };
        table.Subtitle = string.Join(" · ", new[] { page.BranchSelect ? scope.Name : null, page.Dated ? $"Từ {f:dd/MM/yyyy} đến {t:dd/MM/yyyy}" : $"Tại ngày {today:dd/MM/yyyy}" }
            .Where(x => x != null));
        if (errors.Count > 0) page.Error = string.Join(" ", errors);
        else
        {
            try
            {
                var invalid = await Engine.ValidateAsync(r);
                if (invalid != null) throw new InvalidOperationException(invalid);
                var res = await Engine.RunAsync(await Engine.BuildSqlAsync(r, scope.BranchId != null), values);
                Fill(table, res, r);
                if (res.Truncated) page.Note = $"Chỉ hiển thị {DynamicReportEngine.MaxRows:N0} dòng đầu – hãy thu hẹp điều kiện lọc.";
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                page.Error = "Không chạy được báo cáo: " + ex.Message;
            }
        }
        if (export == "xlsx" && page.Error == null)
        {
            var rows = table.Rows.ToList();
            if (table.Footer != null) rows.Add(table.Footer);
            return Excel("bao-cao-" + r.Id, r.Name.Truncate(30), table.Headers, rows.Select(x => (IReadOnlyList<object?>)x));
        }
        if (export == "print") ViewData["Layout"] = "_PrintLayout";
        return View(table);
    }

    /// <summary>Đổ kết quả truy vấn vào bảng báo cáo: cột số canh phải, dòng tổng, biểu đồ</summary>
    private static void Fill(ReportTable table, DynamicResult res, DynamicReport r)
    {
        table.Headers = res.Columns;
        for (var i = 0; i < res.Columns.Count; i++) if (res.IsNumeric(i)) table.NumericColumns.Add(i);
        table.Rows = res.Rows.Select(x => x.ToList()).ToList();
        if (r.ShowTotals && table.NumericColumns.Count > 0 && table.Rows.Count > 0)
        {
            var footer = new List<object?>();
            for (var i = 0; i < res.Columns.Count; i++)
            {
                var name = res.Columns[i];
                var sum = table.NumericColumns.Contains(i) && !name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("Mã", StringComparison.OrdinalIgnoreCase)
                          && !name.Equals("Hạng", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("Mũi", StringComparison.OrdinalIgnoreCase);
                footer.Add(sum ? table.Rows.Sum(row => row[i] == null ? 0m : System.Convert.ToDecimal(row[i])) : null);
            }
            if (footer[0] == null) footer[0] = $"Tổng cộng ({table.Rows.Count} dòng)";
            table.Footer = footer;
        }
        var li = res.Columns.FindIndex(c => c.Equals(r.ChartLabel ?? "", StringComparison.OrdinalIgnoreCase));
        var vi = res.Columns.FindIndex(c => c.Equals(r.ChartValue ?? "", StringComparison.OrdinalIgnoreCase));
        if (li >= 0 && vi >= 0 && res.IsNumeric(vi))
        {
            table.Chart = table.Rows
                .GroupBy(row => row[li] switch { DateTime d => d.ToString("dd/MM"), null => "(trống)", var v => v.ToString()!.Truncate(24) })
                .Select(g => (g.Key, g.Sum(row => row[vi] == null ? 0m : System.Convert.ToDecimal(row[vi])))).Take(60).ToList();
        }
    }

    // ===================== Thiết kế báo cáo =====================

    [RequirePerm(Perm.ReportDesign)]
    public async Task<IActionResult> Index()
    {
        ViewBag.ReadOnly = Engine.HasReadOnlyConnection;
        return View(await Db.DynamicReports.AsNoTracking().OrderBy(r => r.GroupName).ThenBy(r => r.SortOrder).ThenBy(r => r.Name).ToListAsync());
    }

    [RequirePerm(Perm.ReportDesign)]
    public async Task<IActionResult> Create(string? view)
    {
        var m = new DynamicReport
        {
            SortOrder = (await Db.DynamicReports.MaxAsync(r => (int?)r.SortOrder) ?? 0) + 1,
            SqlText = "SELECT [Nhóm], SUM([Thành tiền]) AS [Doanh thu]\nFROM rpt_DoanhThuChiTiet\nWHERE [Ngày] >= @From AND [Ngày] < @ToEnd\n  AND (@BranchId IS NULL OR BranchId = @BranchId)\nGROUP BY [Nhóm]\nORDER BY [Doanh thu] DESC"
        };
        if (!string.IsNullOrEmpty(view)) { m.Source = ReportSource.View; m.ViewName = view; }
        return await FormView(m, null);
    }

    [RequirePerm(Perm.ReportDesign)]
    public async Task<IActionResult> Edit(int id)
    {
        var m = await Db.DynamicReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        return m == null ? NotFound() : await FormView(m, null);
    }

    [RequirePerm(Perm.ReportDesign)]
    public async Task<IActionResult> Duplicate(int id)
    {
        var m = await Db.DynamicReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (m == null) return NotFound();
        m.Id = 0; m.Name = (m.Name + " (bản sao)").Truncate(150); m.UpdatedAt = null; m.UpdatedBy = null;
        return await FormView(m, null);
    }

    /// <summary>Chạy thử (cmd=preview) hoặc lưu (cmd=save)</summary>
    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.ReportDesign)]
    public async Task<IActionResult> Save(DynamicReport m, List<ReportParam>? prm, string[]? roles, string? cmd)
    {
        m.Name = (m.Name ?? "").Trim();
        m.GroupName = m.GroupName?.Trim();
        m.SqlText = m.Source == ReportSource.Sql ? m.SqlText?.Trim() : null;
        if (m.Source == ReportSource.Sql) { m.ViewName = m.DateColumn = m.BranchColumn = m.Columns = m.WhereClause = m.OrderBy = null; }
        var parms = DynamicReportEngine.SyncParams(m, prm);
        m.ParametersJson = parms.Count == 0 ? null : JsonSerializer.Serialize(parms);
        m.AllowedRoles = roles == null ? null : string.Join(",", roles.Where(r => AppRoles.All.Any(a => a.Code == r)));
        if (string.IsNullOrEmpty(m.AllowedRoles)) m.AllowedRoles = null;

        var invalid = await Engine.ValidateAsync(m);
        if (invalid != null) ModelState.AddModelError(m.Source == ReportSource.Sql ? nameof(m.SqlText) : nameof(m.ViewName), invalid);

        if (cmd == "preview" || !ModelState.IsValid) return await FormView(m, invalid == null ? await PreviewAsync(m, parms) : null);

        DynamicReport r;
        if (m.Id == 0) { r = new DynamicReport(); Db.DynamicReports.Add(r); }
        else r = await Db.DynamicReports.FirstOrDefaultAsync(x => x.Id == m.Id) ?? throw new InvalidOperationException();
        r.Name = m.Name; r.GroupName = m.GroupName; r.Description = m.Description?.Trim(); r.Source = m.Source; r.SqlText = m.SqlText;
        r.ViewName = m.ViewName; r.DateColumn = m.DateColumn; r.BranchColumn = m.BranchColumn; r.Columns = m.Columns?.Trim();
        r.WhereClause = m.WhereClause?.Trim(); r.OrderBy = m.OrderBy?.Trim(); r.ParametersJson = m.ParametersJson;
        r.ChartLabel = m.ChartLabel?.Trim(); r.ChartValue = m.ChartValue?.Trim(); r.ShowTotals = m.ShowTotals; r.AllowedRoles = m.AllowedRoles;
        r.SortOrder = m.SortOrder; r.IsActive = m.IsActive; r.UpdatedAt = DateTime.Now; r.UpdatedBy = Me.FullName;
        await Db.SaveChangesAsync();
        TempData["Success"] = $"Đã lưu báo cáo động \"{r.Name}\".";
        return RedirectToAction(nameof(Edit), new { id = r.Id });
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.ReportDesign)]
    public async Task<IActionResult> Delete(int id)
    {
        var r = await Db.DynamicReports.FindAsync(id);
        if (r == null) return NotFound();
        Db.DynamicReports.Remove(r);
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã xóa báo cáo động \"{r.Name}\".", nameof(Index));
    }

    /// <summary>Chạy thử với giá trị mặc định: kỳ = tháng này, chi nhánh đang làm việc, tối đa 200 dòng</summary>
    private async Task<(DynamicResult? Result, string? Error)> PreviewAsync(DynamicReport m, List<ReportParam> parms)
    {
        var today = DateTime.Today;
        var f = new DateTime(today.Year, today.Month, 1);
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["From"] = f, ["To"] = today, ["ToEnd"] = today.AddDays(1), ["BranchId"] = Me.BranchId > 0 ? Me.BranchId : null, ["UserId"] = Me.Id
        };
        foreach (var p in parms)
        {
            values[p.Name] = DynamicReportEngine.Convert(p, DynamicReportEngine.DefaultText(p), out var err);
            if (err != null) return (null, err);
        }
        try { return (await Engine.RunAsync(await Engine.BuildSqlAsync(m, values["BranchId"] != null), values, 200), null); }
        catch (SqlException ex) { return (null, ex.Message); }
    }

    private async Task<IActionResult> FormView(DynamicReport m, (DynamicResult? Result, string? Error)? preview)
    {
        ViewBag.Schema = await Engine.SchemaAsync();
        ViewBag.Groups = await Db.DynamicReports.Where(r => r.GroupName != null).Select(r => r.GroupName!).Distinct().OrderBy(g => g).ToListAsync();
        ViewBag.Params = DynamicReportEngine.Params(m);
        ViewBag.Preview = preview;
        ViewBag.ReadOnly = Engine.HasReadOnlyConnection;
        return View("Form", m);
    }
}
