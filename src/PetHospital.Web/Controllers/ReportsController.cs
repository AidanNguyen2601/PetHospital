using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using PetHospital.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Controllers;

[RequirePerm(Perm.Reports)]
public class ReportsController : AppController
{
    public ReportsController(PetDbContext db) : base(db) { }

    public static readonly (string Id, string Name, string Icon, string Description)[] All =
    {
        ("revenue", "Doanh thu theo ngày", "coins", "Tiền thu theo ngày, chia theo hình thức thanh toán"),
        ("sales", "Doanh thu theo dịch vụ / hàng hóa", "receipt", "Số lượng, doanh thu, giá vốn, lãi gộp từng dịch vụ, thuốc, hàng hóa"),
        ("vets", "Hiệu suất bác sĩ", "stethoscope", "Số ca khám và doanh thu theo bác sĩ"),
        ("visits", "Lượt khám & chẩn đoán", "clipboard", "Lượt khám theo ngày và các chẩn đoán thường gặp"),
        ("debts", "Công nợ khách hàng", "wallet", "Hóa đơn chưa thu đủ theo khách hàng"),
        ("stock", "Nhập – xuất – tồn kho", "box", "Tồn đầu kỳ, nhập, xuất, tồn cuối kỳ từng mặt hàng"),
        ("vaccines", "Thống kê tiêm phòng", "syringe", "Số mũi tiêm theo vắc-xin và loài"),
        ("branches", "So sánh chi nhánh", "building", "Lượt khám, doanh thu, thực thu, công nợ, nội trú của từng chi nhánh")
    };

    public IActionResult Index() => View();

    /// <param name="branch">Phạm vi chi nhánh: trống = chi nhánh đang làm việc, 0 = toàn chuỗi (người dùng toàn chuỗi), &gt;0 = một chi nhánh được làm</param>
    public async Task<IActionResult> Show(string id, DateTime? from, DateTime? to, string? export, int? branch)
    {
        var def = All.FirstOrDefault(r => r.Id == id);
        if (def.Id == null) return NotFound();
        var f = (from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).Date;
        var t = (to ?? DateTime.Today).Date;
        if (t < f) (f, t) = (t, f);
        var end = t.AddDays(1);

        // ----- Phạm vi chi nhánh -----
        var branches = (await Db.Branches.AsNoTracking().OrderBy(b => b.SortOrder).ThenBy(b => b.Name).ToListAsync())
            .Where(b => Me.CanAccessBranch(b.Id)).ToList();
        int? scope = Me.BranchId;
        if (branch == 0 && Me.AllBranches) scope = null;
        else if (branch > 0 && branches.Any(b => b.Id == branch)) scope = branch;
        var options = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
        if (Me.AllBranches) options.Add(new("Toàn chuỗi (tất cả chi nhánh)", "0", scope == null));
        options.AddRange(branches.Select(b => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(b.Name, b.Id.ToString(), b.Id == scope)));
        ViewBag.BranchOptions = options;
        ViewBag.BranchParam = scope?.ToString() ?? "0";
        var scopeName = scope == null ? "Toàn chuỗi" : branches.FirstOrDefault(b => b.Id == scope)?.Name;
        Db.CurrentBranchId = scope;

        var table = id switch
        {
            "revenue" => await Revenue(f, end),
            "sales" => await Sales(f, end),
            "vets" => await Vets(f, end, scope),
            "visits" => await Visits(f, end),
            "debts" => await Debts(),
            "stock" => await Stock(f, end),
            "branches" => await Branches(f, end, branches),
            _ => await Vaccines(f, end)
        };
        table.Title = def.Name;
        table.Subtitle ??= $"Từ {f:dd/MM/yyyy} đến {t:dd/MM/yyyy}";
        if (id != "branches") table.Subtitle = $"{scopeName} · {table.Subtitle}";
        if (export == "xlsx")
        {
            var rows = table.Rows.ToList();
            if (table.Footer != null) rows.Add(table.Footer);
            return Excel(def.Id, def.Name.Truncate(30), table.Headers, rows.Select(r => (IReadOnlyList<object?>)r));
        }
        ViewBag.Def = def; ViewBag.From = f; ViewBag.To = t;
        if (export == "print") ViewData["Layout"] = "_PrintLayout";
        return View("Report", table);
    }

    private IQueryable<InvoiceLine> IssuedLines(DateTime from, DateTime end) =>
        Db.InvoiceLines.Where(l => l.Invoice!.IssuedAt >= from && l.Invoice.IssuedAt < end
            && l.Invoice.Status != InvoiceStatus.Cancelled && l.Invoice.Status != InvoiceStatus.Draft);

    private async Task<ReportTable> Revenue(DateTime from, DateTime end)
    {
        var data = await Db.Payments.Where(p => p.At >= from && p.At < end)
            .GroupBy(p => new { Day = p.At.Date, p.Method }).Select(g => new { g.Key.Day, g.Key.Method, Sum = g.Sum(x => x.Amount), C = g.Count() }).ToListAsync();
        var methods = Enum.GetValues<PaymentMethod>();
        var t = new ReportTable { Headers = new[] { "Ngày" }.Concat(methods.Select(m => m.GetDisplayName())).Concat(new[] { "Tổng thu", "Số phiếu thu" }).ToList() };
        for (var i = 1; i < t.Headers.Count; i++) t.NumericColumns.Add(i);
        t.Chart = new();
        for (var d = from; d < end; d = d.AddDays(1))
        {
            var day = data.Where(x => x.Day == d).ToList();
            var row = new List<object?> { d.ToString("dd/MM/yyyy") };
            row.AddRange(methods.Select(m => (object?)day.Where(x => x.Method == m).Sum(x => x.Sum)));
            row.Add(day.Sum(x => x.Sum));
            row.Add(day.Sum(x => x.C));
            t.Rows.Add(row);
            t.Chart.Add((d.ToString("dd/MM"), day.Sum(x => x.Sum)));
        }
        var footer = new List<object?> { "Tổng cộng" };
        footer.AddRange(methods.Select(m => (object?)data.Where(x => x.Method == m).Sum(x => x.Sum)));
        footer.Add(data.Sum(x => x.Sum));
        footer.Add(data.Sum(x => x.C));
        t.Footer = footer;
        return t;
    }

    private async Task<ReportTable> Sales(DateTime from, DateTime end)
    {
        var cost = Me.CanSeeCost;
        var data = await IssuedLines(from, end)
            .GroupBy(l => new { l.Kind, l.ServiceItemId, l.ProductId, l.Description })
            .Select(g => new { g.Key.Kind, g.Key.ServiceItemId, g.Key.ProductId, g.Key.Description, Qty = g.Sum(x => x.Quantity), Amount = g.Sum(x => x.Amount), Cost = g.Sum(x => x.CostAmount) })
            .ToListAsync();
        var svcNames = await Db.ServiceItems.AsNoTracking().ToDictionaryAsync(s => s.Id, s => (s.Code, s.Name, Group: s.Kind.GetDisplayName()));
        var prdNames = await Db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => (p.Code, p.Name, Group: p.Kind.GetDisplayName()));
        var rows = data.Select(x =>
        {
            (string Code, string Name, string Group) info = x.ServiceItemId.HasValue && svcNames.TryGetValue(x.ServiceItemId.Value, out var s) ? s
                : x.ProductId.HasValue && prdNames.TryGetValue(x.ProductId.Value, out var p) ? p : ("", x.Description, "Khác");
            return new { info.Code, info.Name, info.Group, x.Qty, x.Amount, x.Cost };
        }).GroupBy(x => new { x.Code, x.Name, x.Group })
          .Select(g => new { g.Key.Code, g.Key.Name, g.Key.Group, Qty = g.Sum(x => x.Qty), Amount = g.Sum(x => x.Amount), Cost = g.Sum(x => x.Cost) })
          .OrderByDescending(x => x.Amount).ToList();
        var t = new ReportTable
        {
            Headers = cost ? new() { "Mã", "Tên dịch vụ / hàng hóa", "Nhóm", "Số lượng", "Doanh thu", "Giá vốn", "Lãi gộp" }
                           : new() { "Mã", "Tên dịch vụ / hàng hóa", "Nhóm", "Số lượng", "Doanh thu" },
            NumericColumns = cost ? new() { 3, 4, 5, 6 } : new() { 3, 4 }
        };
        foreach (var r in rows)
            t.Rows.Add(cost ? new List<object?> { r.Code, r.Name, r.Group, r.Qty, r.Amount, r.Cost, r.Amount - r.Cost }
                            : new List<object?> { r.Code, r.Name, r.Group, r.Qty, r.Amount });
        t.Footer = cost ? new List<object?> { "", "Tổng cộng", "", null, rows.Sum(x => x.Amount), rows.Sum(x => x.Cost), rows.Sum(x => x.Amount - x.Cost) }
                        : new List<object?> { "", "Tổng cộng", "", null, rows.Sum(x => x.Amount) };
        t.Chart = rows.GroupBy(x => x.Group).Select(g => (g.Key, g.Sum(x => x.Amount))).OrderByDescending(x => x.Item2).ToList();
        return t;
    }

    private async Task<ReportTable> Vets(DateTime from, DateTime end, int? scope)
    {
        var visits = await Db.Visits.Where(v => v.Status == VisitStatus.Completed && v.CompletedAt >= from && v.CompletedAt < end && v.VetId != null)
            .GroupBy(v => v.VetId!.Value).Select(g => new { g.Key, C = g.Count(), Pets = g.Select(x => x.PetId).Distinct().Count() }).ToListAsync();
        var revenue = await IssuedLines(from, end).Where(l => l.VetId != null)
            .GroupBy(l => new { Vet = l.VetId!.Value, l.Kind }).Select(g => new { g.Key.Vet, g.Key.Kind, Sum = g.Sum(x => x.Amount) }).ToListAsync();
        var vetsInScope = await Db.Users.AsNoTracking().Where(u => u.IsVet && (scope == null || u.AllBranches || u.Branches.Any(b => b.BranchId == scope))).Select(u => u.Id).ToListAsync();
        var active = visits.Select(x => x.Key).Concat(revenue.Select(x => x.Vet)).Concat(vetsInScope).ToHashSet();
        var vets = await Db.Users.AsNoTracking().Where(u => active.Contains(u.Id)).OrderBy(u => u.FullName).ToListAsync();
        var t = new ReportTable
        {
            Headers = new() { "Bác sĩ", "Số ca khám", "Số thú cưng", "Doanh thu dịch vụ", "Doanh thu thuốc / hàng", "Khác", "Tổng doanh thu" },
            NumericColumns = new() { 1, 2, 3, 4, 5, 6 }, Chart = new()
        };
        foreach (var v in vets)
        {
            var c = visits.FirstOrDefault(x => x.Key == v.Id);
            var svc = revenue.Where(x => x.Vet == v.Id && x.Kind == InvoiceLineKind.Service).Sum(x => x.Sum);
            var prd = revenue.Where(x => x.Vet == v.Id && x.Kind == InvoiceLineKind.Product).Sum(x => x.Sum);
            var oth = revenue.Where(x => x.Vet == v.Id && x.Kind == InvoiceLineKind.Other).Sum(x => x.Sum);
            t.Rows.Add(new List<object?> { v.DisplayName, c?.C ?? 0, c?.Pets ?? 0, svc, prd, oth, svc + prd + oth });
            t.Chart.Add((v.FullName, svc + prd + oth));
        }
        t.Footer = new List<object?> { "Tổng cộng", visits.Sum(x => x.C), null, revenue.Where(x => x.Kind == InvoiceLineKind.Service).Sum(x => x.Sum),
            revenue.Where(x => x.Kind == InvoiceLineKind.Product).Sum(x => x.Sum), revenue.Where(x => x.Kind == InvoiceLineKind.Other).Sum(x => x.Sum), revenue.Sum(x => x.Sum) };
        return t;
    }

    private async Task<ReportTable> Visits(DateTime from, DateTime end)
    {
        var byDay = await Db.Visits.Where(v => v.VisitAt >= from && v.VisitAt < end && v.Status != VisitStatus.Cancelled)
            .GroupBy(v => v.VisitAt.Date).Select(g => new { Day = g.Key, C = g.Count() }).ToListAsync();
        var bySpecies = await Db.Visits.Where(v => v.VisitAt >= from && v.VisitAt < end && v.Status != VisitStatus.Cancelled)
            .GroupBy(v => v.Pet!.Species!.Name).Select(g => new { g.Key, C = g.Count() }).ToListAsync();
        var diagnoses = await Db.Visits.Where(v => v.VisitAt >= from && v.VisitAt < end && v.Status == VisitStatus.Completed && v.Diagnosis != null)
            .GroupBy(v => v.Diagnosis!).Select(g => new { g.Key, C = g.Count() }).OrderByDescending(x => x.C).Take(30).ToListAsync();
        var total = byDay.Sum(x => x.C);
        var t = new ReportTable
        {
            Headers = new() { "Chẩn đoán", "Số ca", "Tỷ lệ" }, NumericColumns = new() { 1, 2 },
            Subtitle = $"Từ {from:dd/MM/yyyy} đến {end.AddDays(-1):dd/MM/yyyy} · {total} lượt khám · " +
                       string.Join(", ", bySpecies.OrderByDescending(x => x.C).Select(x => $"{x.Key}: {x.C}")),
            Chart = new()
        };
        foreach (var d in diagnoses) t.Rows.Add(new List<object?> { d.Key, d.C, total == 0 ? "" : $"{d.C * 100.0 / total:0.#}%" });
        for (var d = from; d < end; d = d.AddDays(1)) t.Chart.Add((d.ToString("dd/MM"), byDay.FirstOrDefault(x => x.Day == d)?.C ?? 0));
        t.Footer = new List<object?> { "Tổng lượt khám", total, null };
        return t;
    }

    private async Task<ReportTable> Debts()
    {
        var raw = await Db.Invoices.Where(i => i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Partial)
            .GroupBy(i => new { i.OwnerId, i.CustomerName })
            .Select(g => new { g.Key.OwnerId, g.Key.CustomerName, C = g.Count(), Total = g.Sum(x => x.Total), Paid = g.Sum(x => x.Paid), Oldest = g.Min(x => x.Date) })
            .ToListAsync();
        var ownerIds = raw.Where(x => x.OwnerId.HasValue).Select(x => x.OwnerId!.Value).Distinct().ToList();
        var owners = await Db.Owners.AsNoTracking().Where(o => ownerIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id);
        var data = raw.Select(x => new
        {
            Name = x.OwnerId.HasValue && owners.TryGetValue(x.OwnerId.Value, out var o) ? o.FullName : x.CustomerName,
            Phone = x.OwnerId.HasValue && owners.TryGetValue(x.OwnerId.Value, out var o2) ? o2.Phone : null,
            x.C, x.Total, x.Paid, x.Oldest
        }).ToList();
        var t = new ReportTable
        {
            Headers = new() { "Khách hàng", "Điện thoại", "Số hóa đơn", "Tổng tiền", "Đã thu", "Còn nợ", "Hóa đơn cũ nhất" },
            NumericColumns = new() { 2, 3, 4, 5 }, Subtitle = $"Tại ngày {DateTime.Today:dd/MM/yyyy}"
        };
        foreach (var d in data.OrderByDescending(x => x.Total - x.Paid))
            t.Rows.Add(new List<object?> { d.Name ?? "Khách lẻ", d.Phone, d.C, d.Total, d.Paid, d.Total - d.Paid, d.Oldest.ToString("dd/MM/yyyy") });
        t.Footer = new List<object?> { "Tổng cộng", null, data.Sum(x => x.C), data.Sum(x => x.Total), data.Sum(x => x.Paid), data.Sum(x => x.Total - x.Paid), null };
        return t;
    }

    private async Task<ReportTable> Stock(DateTime from, DateTime end)
    {
        var cost = Me.CanSeeCost;
        var products = await Db.Products.AsNoTracking().Include(p => p.Stocks).OrderBy(p => p.Kind).ThenBy(p => p.Name).ToListAsync();
        var after = await Db.StockMoves.Where(m => m.At >= from).GroupBy(m => m.ProductId).Select(g => new { g.Key, Q = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Q);
        var period = await Db.StockMoves.Where(m => m.At >= from && m.At < end)
            .GroupBy(m => m.ProductId)
            .Select(g => new
            {
                g.Key,
                In = g.Where(x => x.Quantity > 0).Sum(x => x.Quantity),
                Out = g.Where(x => x.Quantity < 0).Sum(x => -x.Quantity),
                OutCost = g.Where(x => x.Quantity < 0).Sum(x => -x.Quantity * x.UnitCost)
            }).ToDictionaryAsync(x => x.Key);
        var afterEnd = await Db.StockMoves.Where(m => m.At >= end).GroupBy(m => m.ProductId).Select(g => new { g.Key, Q = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Q);
        var t = new ReportTable
        {
            Headers = cost ? new() { "Mã", "Tên hàng", "ĐVT", "Tồn đầu", "Nhập", "Xuất", "Tồn cuối", "Giá vốn xuất", "Giá trị tồn hiện tại" }
                           : new() { "Mã", "Tên hàng", "ĐVT", "Tồn đầu", "Nhập", "Xuất", "Tồn cuối" },
            NumericColumns = cost ? new() { 3, 4, 5, 6, 7, 8 } : new() { 3, 4, 5, 6 }
        };
        foreach (var p in products)
        {
            var opening = p.Stock - after.GetValueOrDefault(p.Id);
            var closing = p.Stock - afterEnd.GetValueOrDefault(p.Id);
            period.TryGetValue(p.Id, out var mv);
            if (opening == 0 && closing == 0 && mv == null) continue;
            var row = new List<object?> { p.Code, p.Name, p.Unit, opening, mv?.In ?? 0, mv?.Out ?? 0, closing };
            if (cost) { row.Add(Math.Round(mv?.OutCost ?? 0, 0)); row.Add(Math.Round(p.StockValue, 0)); }
            t.Rows.Add(row);
        }
        if (cost)
            t.Footer = new List<object?> { "", "Tổng cộng", "", null, null, null, null, Math.Round(period.Values.Sum(x => x.OutCost), 0),
                Math.Round(products.Sum(p => p.StockValue), 0) };
        return t;
    }

    /// <summary>So sánh các chi nhánh người dùng được xem</summary>
    private async Task<ReportTable> Branches(DateTime from, DateTime end, List<Branch> branches)
    {
        Db.CurrentBranchId = null;
        var ids = branches.Select(b => b.Id).ToList();
        var visits = await Db.Visits.Where(v => ids.Contains(v.BranchId) && v.VisitAt >= from && v.VisitAt < end && v.Status != VisitStatus.Cancelled)
            .GroupBy(v => v.BranchId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        var sales = await IssuedLines(from, end).Where(l => ids.Contains(l.Invoice!.BranchId))
            .GroupBy(l => l.Invoice!.BranchId).Select(g => new { g.Key, S = g.Sum(x => x.Amount), C = g.Sum(x => x.CostAmount) }).ToDictionaryAsync(x => x.Key);
        var paid = await Db.Payments.Where(p => ids.Contains(p.Invoice!.BranchId) && p.At >= from && p.At < end)
            .GroupBy(p => p.Invoice!.BranchId).Select(g => new { g.Key, S = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.S);
        var debts = await Db.Invoices.Where(i => ids.Contains(i.BranchId) && (i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Partial))
            .GroupBy(i => i.BranchId).Select(g => new { g.Key, S = g.Sum(x => x.Total - x.Paid) }).ToDictionaryAsync(x => x.Key, x => x.S);
        var boarding = await Db.Admissions.Where(a => ids.Contains(a.BranchId) && a.AdmitAt >= from && a.AdmitAt < end && a.Status != AdmissionStatus.Cancelled)
            .GroupBy(a => a.BranchId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        var newOwners = await Db.Owners.Where(o => ids.Contains(o.BranchId) && o.CreatedAt >= from && o.CreatedAt < end)
            .GroupBy(o => o.BranchId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        var stock = Me.CanSeeCost
            ? await Db.ProductStocks.Where(s => ids.Contains(s.BranchId) && s.Quantity > 0).GroupBy(s => s.BranchId)
                .Select(g => new { g.Key, S = g.Sum(x => x.Quantity * x.CostPrice) }).ToDictionaryAsync(x => x.Key, x => x.S)
            : new Dictionary<int, decimal>();
        var cost = Me.CanSeeCost;
        var t = new ReportTable
        {
            Headers = cost
                ? new() { "Chi nhánh", "Lượt khám", "Lượt nội trú", "Khách mới", "Doanh thu", "Lãi gộp", "Thực thu", "Công nợ hiện tại", "Giá trị tồn kho" }
                : new() { "Chi nhánh", "Lượt khám", "Lượt nội trú", "Khách mới", "Doanh thu", "Thực thu", "Công nợ hiện tại" },
            NumericColumns = cost ? new() { 1, 2, 3, 4, 5, 6, 7, 8 } : new() { 1, 2, 3, 4, 5, 6 },
            Chart = new()
        };
        foreach (var b in branches)
        {
            var s = sales.GetValueOrDefault(b.Id);
            var row = new List<object?> { b.Name, visits.GetValueOrDefault(b.Id), boarding.GetValueOrDefault(b.Id), newOwners.GetValueOrDefault(b.Id), s?.S ?? 0m };
            if (cost) row.Add((s?.S ?? 0) - (s?.C ?? 0));
            row.Add(paid.GetValueOrDefault(b.Id));
            row.Add(debts.GetValueOrDefault(b.Id));
            if (cost) row.Add(Math.Round(stock.GetValueOrDefault(b.Id), 0));
            t.Rows.Add(row);
            t.Chart.Add((b.Name, s?.S ?? 0));
        }
        var footer = new List<object?> { "Tổng cộng", visits.Values.Sum(), boarding.Values.Sum(), newOwners.Values.Sum(), sales.Values.Sum(x => x.S) };
        if (cost) footer.Add(sales.Values.Sum(x => x.S - x.C));
        footer.Add(paid.Values.Sum());
        footer.Add(debts.Values.Sum());
        if (cost) footer.Add(Math.Round(stock.Values.Sum(), 0));
        t.Footer = footer;
        return t;
    }

    private async Task<ReportTable> Vaccines(DateTime from, DateTime end)
    {
        var data = await Db.Vaccinations.Where(v => v.Date >= from && v.Date < end)
            .GroupBy(v => new { v.VaccineName, Species = v.Pet!.Species!.Name }).Select(g => new { g.Key.VaccineName, g.Key.Species, C = g.Count() }).ToListAsync();
        var species = data.Select(x => x.Species).Distinct().OrderBy(x => x).ToList();
        var t = new ReportTable
        {
            Headers = new[] { "Vắc-xin" }.Concat(species).Concat(new[] { "Tổng số mũi" }).ToList(),
            Chart = new()
        };
        for (var i = 1; i < t.Headers.Count; i++) t.NumericColumns.Add(i);
        foreach (var g in data.GroupBy(x => x.VaccineName).OrderByDescending(g => g.Sum(x => x.C)))
        {
            var row = new List<object?> { g.Key };
            row.AddRange(species.Select(s => (object?)g.Where(x => x.Species == s).Sum(x => x.C)));
            row.Add(g.Sum(x => x.C));
            t.Rows.Add(row);
            t.Chart.Add((g.Key.Truncate(30), g.Sum(x => x.C)));
        }
        var footer = new List<object?> { "Tổng cộng" };
        footer.AddRange(species.Select(s => (object?)data.Where(x => x.Species == s).Sum(x => x.C)));
        footer.Add(data.Sum(x => x.C));
        t.Footer = footer;
        return t;
    }
}
