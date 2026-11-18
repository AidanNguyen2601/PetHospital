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

    public static readonly string[] Groups = { "Doanh thu & thu ngân", "Khám chữa bệnh", "Khách hàng", "Kho thuốc", "Chuỗi chi nhánh" };

    public static readonly StdReport[] All =
    {
        // Doanh thu & thu ngân
        new("revenue", "Doanh thu theo ngày", "coins", "Tiền thu theo ngày, chia theo hình thức thanh toán", Groups[0]),
        new("monthly", "Doanh thu theo tháng", "chart", "Số hóa đơn, tiền hàng, giảm giá, doanh thu, lãi gộp, thực thu, lượt khám từng tháng", Groups[0], "year"),
        new("sales", "Doanh thu theo dịch vụ / hàng hóa", "receipt", "Số lượng, doanh thu, giá vốn, lãi gộp từng dịch vụ, thuốc, hàng hóa", Groups[0]),
        new("cashbook", "Sổ thu tiền chi tiết", "wallet", "Từng lần thu tiền: thời điểm, hóa đơn, khách hàng, hình thức, thu ngân", Groups[0]),
        new("debts", "Công nợ khách hàng", "wallet", "Hóa đơn chưa thu đủ theo khách hàng", Groups[0], "none"),
        new("cancelled", "Hóa đơn đã hủy", "x", "Hóa đơn bị hủy trong kỳ: giá trị, số tiền đã thu, người lập", Groups[0]),
        // Khám chữa bệnh
        new("vets", "Hiệu suất bác sĩ", "stethoscope", "Số ca khám và doanh thu theo bác sĩ", Groups[1]),
        new("visits", "Lượt khám & chẩn đoán", "clipboard", "Lượt khám theo ngày và các chẩn đoán thường gặp", Groups[1], ChartMoney: false),
        new("species", "Lượt khám & doanh thu theo loài", "paw", "Lượt khám, số thú cưng, doanh thu, doanh thu bình quân theo loài", Groups[1]),
        new("appointments", "Tình hình lịch hẹn", "calendar", "Lịch hẹn theo ngày: hoàn thành, không đến, hủy, tỷ lệ đến khám", Groups[1], ChartMoney: false),
        new("vaccines", "Thống kê tiêm phòng", "syringe", "Số mũi tiêm theo vắc-xin và loài", Groups[1], ChartMoney: false),
        new("vaccine-due", "Mũi tiêm đến hạn / quá hạn", "bell", "Thú cưng đến hạn tiêm nhắc lại trong kỳ – danh sách gọi nhắc khách", Groups[1], "next30"),
        new("boarding", "Công suất chuồng & nội trú", "bed", "Số lượt, số ngày lưu, công suất sử dụng, doanh thu ước tính từng chuồng", Groups[1], ChartMoney: false),
        // Khách hàng
        new("customers", "Khách hàng chi tiêu nhiều nhất", "trophy", "Top khách hàng theo doanh thu trong kỳ: số lượt khám, hóa đơn, còn nợ", Groups[2]),
        new("new-customers", "Khách hàng mới", "contact", "Khách hàng tạo mới trong kỳ, số thú cưng, lượt khám và chi tiêu", Groups[2], ChartMoney: false),
        // Kho thuốc
        new("stock", "Nhập – xuất – tồn kho", "box", "Tồn đầu kỳ, nhập, xuất, tồn cuối kỳ từng mặt hàng", Groups[3]),
        new("expiry", "Thuốc hết hạn / sắp hết hạn", "alert", "Lô hàng còn tồn đã hết hạn hoặc sắp hết hạn dùng", Groups[3], "none"),
        new("low-stock", "Hàng dưới tồn tối thiểu", "download", "Mặt hàng tồn thấp hơn mức tối thiểu – gợi ý số lượng cần nhập", Groups[3], "none"),
        // Chuỗi
        new("branches", "So sánh chi nhánh", "building", "Lượt khám, doanh thu, thực thu, công nợ, nội trú của từng chi nhánh", Groups[4])
    };

    public async Task<IActionResult> Index()
    {
        var reports = await Db.DynamicReports.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.GroupName).ThenBy(r => r.SortOrder).ThenBy(r => r.Name).ToListAsync();
        ViewBag.Dynamic = reports.Where(r => DynamicReportsController.CanView(Me, r)).ToList();
        return View();
    }

    public async Task<IActionResult> Show(string id, DateTime? from, DateTime? to, string? export, int? branch)
    {
        var def = All.FirstOrDefault(r => r.Id == id);
        if (def == null) return NotFound();
        var today = DateTime.Today;
        var f = (from ?? def.Period switch
        {
            "year" => new DateTime(today.Year, 1, 1),
            "next30" => today,
            _ => new DateTime(today.Year, today.Month, 1)
        }).Date;
        var t = (to ?? (def.Period == "next30" ? today.AddDays(30) : today)).Date;
        if (t < f) (f, t) = (t, f);
        var end = t.AddDays(1);
        var scope = await ReportScopeAsync(branch);

        var table = id switch
        {
            "revenue" => await Revenue(f, end),
            "monthly" => await Monthly(f, end),
            "sales" => await Sales(f, end),
            "cashbook" => await Cashbook(f, end),
            "debts" => await Debts(),
            "cancelled" => await Cancelled(f, end),
            "vets" => await Vets(f, end, scope.BranchId),
            "visits" => await Visits(f, end),
            "species" => await SpeciesReport(f, end),
            "appointments" => await Appointments(f, end),
            "vaccine-due" => await VaccineDue(f, end),
            "boarding" => await Boarding(f, end),
            "customers" => await TopCustomers(f, end),
            "new-customers" => await NewCustomers(f, end, scope.BranchId),
            "stock" => await Stock(f, end),
            "expiry" => await Expiry(),
            "low-stock" => await LowStock(),
            "branches" => await Branches(f, end, scope.Branches),
            _ => await Vaccines(f, end)
        };
        table.Title = def.Name;
        table.Subtitle ??= $"Từ {f:dd/MM/yyyy} đến {t:dd/MM/yyyy}";
        if (id != "branches") table.Subtitle = $"{scope.Name} · {table.Subtitle}";
        if (export == "xlsx")
        {
            var rows = table.Rows.ToList();
            if (table.Footer != null) rows.Add(table.Footer);
            return Excel(def.Id, def.Name.Truncate(30), table.Headers, rows.Select(r => (IReadOnlyList<object?>)r));
        }
        ViewBag.Page = new ReportPage
        {
            Name = def.Name, Icon = def.Icon, Dated = def.Period != "none", BranchSelect = id != "branches",
            ChartMoney = def.ChartMoney, From = f, To = t, Scope = scope
        };
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

    // ===================== Báo cáo bổ sung =====================

    private IQueryable<Invoice> IssuedInvoices(DateTime from, DateTime end) =>
        Db.Invoices.Where(i => i.IssuedAt >= from && i.IssuedAt < end && i.Status != InvoiceStatus.Cancelled && i.Status != InvoiceStatus.Draft);

    private static string Pct(decimal part, decimal total) => total == 0 ? "" : (part * 100 / total).ToString("0.#", FormatExtensions.Vi) + "%";

    private async Task<ReportTable> Monthly(DateTime from, DateTime end)
    {
        var cost = Me.CanSeeCost;
        var inv = (await IssuedInvoices(from, end).GroupBy(i => new { i.IssuedAt!.Value.Year, i.IssuedAt.Value.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, C = g.Count(), Sub = g.Sum(x => x.SubTotal), Disc = g.Sum(x => x.Discount), Total = g.Sum(x => x.Total) })
                .ToListAsync()).ToDictionary(x => (x.Year, x.Month));
        var costs = cost
            ? (await IssuedLines(from, end).GroupBy(l => new { l.Invoice!.IssuedAt!.Value.Year, l.Invoice.IssuedAt.Value.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, S = g.Sum(x => x.CostAmount) }).ToListAsync()).ToDictionary(x => (x.Year, x.Month), x => x.S)
            : new Dictionary<(int, int), decimal>();
        var paid = (await Db.Payments.Where(p => p.At >= from && p.At < end).GroupBy(p => new { p.At.Year, p.At.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, S = g.Sum(x => x.Amount) }).ToListAsync()).ToDictionary(x => (x.Year, x.Month), x => x.S);
        var visits = (await Db.Visits.Where(v => v.VisitAt >= from && v.VisitAt < end && v.Status != VisitStatus.Cancelled).GroupBy(v => new { v.VisitAt.Year, v.VisitAt.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, C = g.Count() }).ToListAsync()).ToDictionary(x => (x.Year, x.Month), x => x.C);

        var t = new ReportTable
        {
            Headers = cost ? new() { "Tháng", "Số hóa đơn", "Tiền hàng", "Giảm giá", "Doanh thu", "Lãi gộp", "Thực thu", "Lượt khám" }
                           : new() { "Tháng", "Số hóa đơn", "Tiền hàng", "Giảm giá", "Doanh thu", "Thực thu", "Lượt khám" },
            Chart = new()
        };
        for (var i = 1; i < t.Headers.Count; i++) t.NumericColumns.Add(i);
        var sums = new decimal[t.Headers.Count];
        for (var m = new DateTime(from.Year, from.Month, 1); m < end; m = m.AddMonths(1))
        {
            var k = (m.Year, m.Month);
            inv.TryGetValue(k, out var x);
            var vals = new List<object?> { x?.C ?? 0, x?.Sub ?? 0m, x?.Disc ?? 0m, x?.Total ?? 0m };
            if (cost) vals.Add((x?.Total ?? 0) - costs.GetValueOrDefault(k));
            vals.Add(paid.GetValueOrDefault(k));
            vals.Add(visits.GetValueOrDefault(k));
            for (var i = 0; i < vals.Count; i++) sums[i + 1] += System.Convert.ToDecimal(vals[i]);
            t.Rows.Add(new List<object?> { m.ToString("MM/yyyy") }.Concat(vals).ToList());
            t.Chart.Add((m.ToString("MM/yy"), x?.Total ?? 0));
        }
        t.Footer = new List<object?> { "Tổng cộng" }.Concat(sums.Skip(1).Select((s, i) => i == 0 || i == sums.Length - 2 ? (object?)(int)s : s)).ToList();
        return t;
    }

    private async Task<ReportTable> Cashbook(DateTime from, DateTime end)
    {
        var data = await Db.Payments.Where(p => p.At >= from && p.At < end).OrderBy(p => p.At)
            .Select(p => new
            {
                p.At, p.Invoice!.Code, Name = p.Invoice.Owner != null ? p.Invoice.Owner.FullName : p.Invoice.CustomerName,
                p.Method, p.Amount, User = p.User != null ? p.User.FullName : null
            }).ToListAsync();
        var t = new ReportTable
        {
            Headers = new() { "Thời điểm", "Mã hóa đơn", "Khách hàng", "Hình thức", "Số tiền", "Thu ngân" }, NumericColumns = new() { 4 },
            Chart = data.GroupBy(x => x.Method).Select(g => (g.Key.GetDisplayName(), g.Sum(x => x.Amount))).ToList()
        };
        foreach (var d in data) t.Rows.Add(new List<object?> { d.At, d.Code, d.Name ?? "Khách lẻ", d.Method.GetDisplayName(), d.Amount, d.User });
        t.Footer = new List<object?> { $"{data.Count} lần thu", null, null, null, data.Sum(x => x.Amount), null };
        return t;
    }

    private async Task<ReportTable> Cancelled(DateTime from, DateTime end)
    {
        var data = await Db.Invoices.Where(i => i.Status == InvoiceStatus.Cancelled && i.CancelledAt >= from && i.CancelledAt < end).OrderBy(i => i.CancelledAt)
            .Select(i => new
            {
                i.Code, i.Date, i.CancelledAt, Name = i.Owner != null ? i.Owner.FullName : i.CustomerName, i.Total, i.Paid,
                User = i.CreatedBy != null ? i.CreatedBy.FullName : null, i.Note
            }).ToListAsync();
        var t = new ReportTable
        {
            Headers = new() { "Mã hóa đơn", "Ngày lập", "Thời điểm hủy", "Khách hàng", "Tổng tiền", "Đã thu", "Người lập", "Ghi chú" },
            NumericColumns = new() { 4, 5 }
        };
        foreach (var d in data) t.Rows.Add(new List<object?> { d.Code, d.Date, d.CancelledAt, d.Name ?? "Khách lẻ", d.Total, d.Paid, d.User, d.Note });
        t.Footer = new List<object?> { $"{data.Count} hóa đơn", null, null, null, data.Sum(x => x.Total), data.Sum(x => x.Paid), null, null };
        return t;
    }

    private async Task<ReportTable> SpeciesReport(DateTime from, DateTime end)
    {
        var visits = await Db.Visits.Where(v => v.VisitAt >= from && v.VisitAt < end && v.Status != VisitStatus.Cancelled)
            .GroupBy(v => v.Pet!.Species!.Name).Select(g => new { g.Key, C = g.Count(), Pets = g.Select(x => x.PetId).Distinct().Count() }).ToListAsync();
        var revenue = await IssuedLines(from, end).GroupBy(l => l.Invoice!.Pet!.Species!.Name).Select(g => new { g.Key, S = g.Sum(x => x.Amount) }).ToListAsync();
        var keys = visits.Select(x => x.Key).Concat(revenue.Select(x => x.Key)).Distinct().ToList();
        var t = new ReportTable
        {
            Headers = new() { "Loài", "Lượt khám", "Số thú cưng", "Doanh thu", "Bình quân / lượt khám", "Tỷ trọng doanh thu" },
            NumericColumns = new() { 1, 2, 3, 4, 5 }, Chart = new()
        };
        var total = revenue.Sum(x => x.S);
        foreach (var k in keys.OrderByDescending(k => revenue.FirstOrDefault(x => x.Key == k)?.S ?? 0))
        {
            var v = visits.FirstOrDefault(x => x.Key == k);
            var s = revenue.FirstOrDefault(x => x.Key == k)?.S ?? 0;
            var name = k ?? "Không rõ / bán lẻ";
            t.Rows.Add(new List<object?> { name, v?.C ?? 0, v?.Pets ?? 0, s, v is { C: > 0 } ? Math.Round(s / v.C, 0) : null, Pct(s, total) });
            t.Chart.Add((name, s));
        }
        t.Footer = new List<object?> { "Tổng cộng", visits.Sum(x => x.C), visits.Sum(x => x.Pets), total, null, null };
        return t;
    }

    private async Task<ReportTable> Appointments(DateTime from, DateTime end)
    {
        var data = await Db.Appointments.Where(a => a.StartAt >= from && a.StartAt < end)
            .GroupBy(a => new { Day = a.StartAt.Date, a.Status }).Select(g => new { g.Key.Day, g.Key.Status, C = g.Count() }).ToListAsync();
        var t = new ReportTable
        {
            Headers = new() { "Ngày", "Tổng lịch hẹn", "Hoàn thành", "Đã đến (chờ khám)", "Chưa đến (đã đặt / xác nhận)", "Không đến", "Đã hủy", "Tỷ lệ đến khám" },
            NumericColumns = new() { 1, 2, 3, 4, 5, 6, 7 }, Chart = new()
        };
        var totals = new int[6];
        for (var d = from; d < end; d = d.AddDays(1))
        {
            var day = data.Where(x => x.Day == d).ToList();
            int C(params AppointmentStatus[] st) => day.Where(x => st.Contains(x.Status)).Sum(x => x.C);
            var cols = new[] { day.Sum(x => x.C), C(AppointmentStatus.Completed), C(AppointmentStatus.CheckedIn),
                C(AppointmentStatus.Booked, AppointmentStatus.Confirmed), C(AppointmentStatus.NoShow), C(AppointmentStatus.Cancelled) };
            for (var i = 0; i < cols.Length; i++) totals[i] += cols[i];
            t.Rows.Add(new List<object?> { d.ToString("dd/MM/yyyy") }.Concat(cols.Select(c => (object?)c))
                .Append(Pct(cols[1] + cols[2], cols[0] - cols[5])).ToList());
            t.Chart.Add((d.ToString("dd/MM"), cols[0]));
        }
        t.Footer = new List<object?> { "Tổng cộng" }.Concat(totals.Select(c => (object?)c)).Append(Pct(totals[1] + totals[2], totals[0] - totals[5])).ToList();
        return t;
    }

    private async Task<ReportTable> VaccineDue(DateTime from, DateTime end)
    {
        var today = DateTime.Today;
        var data = await Db.Vaccinations
            .Where(v => v.NextDueDate >= from && v.NextDueDate < end && !v.Pet!.IsDeceased
                        && !Db.Vaccinations.IgnoreQueryFilters().Any(x => x.PetId == v.PetId && x.VaccineName == v.VaccineName && x.Date > v.Date))
            .OrderBy(v => v.NextDueDate)
            .Select(v => new
            {
                v.NextDueDate, v.VaccineName, v.DoseNumber, Pet = v.Pet!.Name, Species = v.Pet.Species != null ? v.Pet.Species.Name : null,
                Owner = v.Pet.Owner != null ? v.Pet.Owner.FullName : null, Phone = v.Pet.Owner != null ? v.Pet.Owner.Phone : null, v.Date, v.ReminderDone
            }).ToListAsync();
        var t = new ReportTable
        {
            Headers = new() { "Ngày đến hạn", "Trạng thái", "Vắc-xin", "Mũi tiếp theo", "Thú cưng", "Loài", "Chủ nuôi", "Điện thoại", "Ngày tiêm trước", "Đã nhắc" },
            NumericColumns = new() { 3 },
            Subtitle = $"Đến hạn từ {from:dd/MM/yyyy} đến {end.AddDays(-1):dd/MM/yyyy} · {data.Count} mũi · quá hạn {data.Count(x => x.NextDueDate < today)}"
        };
        foreach (var d in data)
            t.Rows.Add(new List<object?>
            {
                d.NextDueDate, d.NextDueDate < today ? "Quá hạn" : d.NextDueDate == today ? "Hôm nay" : "Sắp đến hạn",
                d.VaccineName, d.DoseNumber + 1, d.Pet, d.Species, d.Owner, d.Phone, d.Date, d.ReminderDone ? "✓" : ""
            });
        return t;
    }

    private async Task<ReportTable> Boarding(DateTime from, DateTime end)
    {
        var cages = await Db.Cages.AsNoTracking().OrderBy(c => c.Code).ToListAsync();
        var adm = await Db.Admissions.Where(a => a.Status != AdmissionStatus.Cancelled && a.AdmitAt < end && (a.DischargeAt == null || a.DischargeAt >= from))
            .Select(a => new { a.CageId, a.AdmitAt, a.DischargeAt, a.DailyRate }).ToListAsync();
        var last = end.AddDays(-1);
        int Days(DateTime admit, DateTime? discharge)
        {
            var s = admit.Date < from ? from : admit.Date;
            var e = (discharge ?? DateTime.Today).Date;
            if (e > last) e = last;
            return e < s ? 0 : (e - s).Days + 1;
        }
        var periodDays = Math.Max(1, (end - from).Days);
        var t = new ReportTable
        {
            Headers = new() { "Chuồng", "Loại", "Giá / ngày", "Lượt nhận trong kỳ", "Số ngày lưu", "Công suất", "Doanh thu ước tính" },
            NumericColumns = new() { 2, 3, 4, 5, 6 }, Chart = new()
        };
        int totalAdmits = 0, totalDays = 0; decimal totalRev = 0;
        var groups = cages.Where(c => c.IsActive || adm.Any(a => a.CageId == c.Id)).Select(c => ((int?)c.Id, $"{c.Code} – {c.Name}", c.IsIsolation ? "Cách ly" : "Thường", (decimal?)c.DailyRate)).ToList();
        if (adm.Any(a => a.CageId == null)) groups.Add((null, "(Không gán chuồng)", "", null));
        foreach (var (id, name, kind, rate) in groups)
        {
            var list = adm.Where(a => a.CageId == id).ToList();
            var admits = list.Count(a => a.AdmitAt >= from);
            var days = list.Sum(a => Days(a.AdmitAt, a.DischargeAt));
            var rev = list.Sum(a => Days(a.AdmitAt, a.DischargeAt) * a.DailyRate);
            totalAdmits += admits; totalDays += days; totalRev += rev;
            t.Rows.Add(new List<object?> { name, kind, rate, admits, days, id == null ? "" : Pct(days, periodDays), rev });
            if (id != null) t.Chart.Add((name.Split(' ')[0], days));
        }
        var activeCages = cages.Count(c => c.IsActive);
        t.Footer = new List<object?> { "Tổng cộng", null, null, totalAdmits, totalDays, Pct(adm.Where(a => a.CageId != null).Sum(a => Days(a.AdmitAt, a.DischargeAt)), periodDays * (decimal)Math.Max(1, activeCages)), totalRev };
        t.Subtitle = $"Từ {from:dd/MM/yyyy} đến {last:dd/MM/yyyy} · {activeCages} chuồng đang dùng · công suất = số ngày có thú lưu / số ngày trong kỳ";
        return t;
    }

    private async Task<ReportTable> TopCustomers(DateTime from, DateTime end)
    {
        var inv = await IssuedInvoices(from, end).Where(i => i.OwnerId != null).GroupBy(i => i.OwnerId!.Value)
            .Select(g => new { Id = g.Key, C = g.Count(), Total = g.Sum(x => x.Total), Debt = g.Sum(x => x.Total - x.Paid), Last = g.Max(x => x.IssuedAt) })
            .OrderByDescending(x => x.Total).Take(100).ToListAsync();
        var ids = inv.Select(x => x.Id).ToList();
        var owners = await Db.Owners.IgnoreQueryFilters().AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id);
        var visits = await Db.Visits.Where(v => ids.Contains(v.OwnerId) && v.VisitAt >= from && v.VisitAt < end && v.Status != VisitStatus.Cancelled)
            .GroupBy(v => v.OwnerId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        var t = new ReportTable
        {
            Headers = new() { "Hạng", "Mã khách", "Khách hàng", "Điện thoại", "Lượt khám", "Số hóa đơn", "Doanh thu", "Còn nợ", "Lần mua gần nhất" },
            NumericColumns = new() { 0, 4, 5, 6, 7 }, Chart = new(), Subtitle = $"Từ {from:dd/MM/yyyy} đến {end.AddDays(-1):dd/MM/yyyy} · 100 khách hàng có doanh thu cao nhất"
        };
        var rank = 0;
        foreach (var x in inv)
        {
            owners.TryGetValue(x.Id, out var o);
            t.Rows.Add(new List<object?> { ++rank, o?.Code, o?.FullName, o?.Phone, visits.GetValueOrDefault(x.Id), x.C, x.Total, x.Debt, x.Last });
            if (rank <= 15) t.Chart.Add(((o?.FullName ?? "").Truncate(18), x.Total));
        }
        t.Footer = new List<object?> { null, null, "Tổng cộng", null, visits.Values.Sum(), inv.Sum(x => x.C), inv.Sum(x => x.Total), inv.Sum(x => x.Debt), null };
        return t;
    }

    private async Task<ReportTable> NewCustomers(DateTime from, DateTime end, int? scope)
    {
        var data = await Db.Owners.Where(o => o.CreatedAt >= from && o.CreatedAt < end && (scope == null || o.BranchId == scope)).OrderBy(o => o.CreatedAt)
            .Select(o => new
            {
                o.Code, o.FullName, o.Phone, o.CreatedAt,
                Pets = Db.Pets.Count(p => p.OwnerId == o.Id),
                Visits = Db.Visits.Count(v => v.OwnerId == o.Id && v.Status != VisitStatus.Cancelled),
                Spent = Db.Invoices.Where(i => i.OwnerId == o.Id && (i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Partial || i.Status == InvoiceStatus.Paid))
                    .Sum(i => (decimal?)i.Total) ?? 0
            }).ToListAsync();
        var t = new ReportTable
        {
            Headers = new() { "Mã khách", "Khách hàng", "Điện thoại", "Ngày tạo", "Số thú cưng", "Số lượt khám", "Chi tiêu" },
            NumericColumns = new() { 4, 5, 6 }, Chart = new(),
            Subtitle = $"Từ {from:dd/MM/yyyy} đến {end.AddDays(-1):dd/MM/yyyy} · {data.Count} khách hàng mới · {data.Count(x => x.Visits > 0)} đã đến khám"
        };
        foreach (var d in data) t.Rows.Add(new List<object?> { d.Code, d.FullName, d.Phone, d.CreatedAt, d.Pets, d.Visits, d.Spent });
        for (var d = from; d < end; d = d.AddDays(1)) t.Chart.Add((d.ToString("dd/MM"), data.Count(x => x.CreatedAt.Date == d)));
        t.Footer = new List<object?> { null, "Tổng cộng", null, null, data.Sum(x => x.Pets), data.Sum(x => x.Visits), data.Sum(x => x.Spent) };
        return t;
    }

    private async Task<ReportTable> Expiry()
    {
        var warn = await Svc<SettingsService>().GetIntAsync(SettingsService.ExpiryWarnDays, 60);
        var today = DateTime.Today;
        var limit = today.AddDays(warn);
        var cost = Me.CanSeeCost;
        var names = await Db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name);
        var data = await Db.ProductBatches.Where(b => b.Quantity > 0 && b.ExpiryDate != null && b.ExpiryDate <= limit).OrderBy(b => b.ExpiryDate)
            .Select(b => new { b.BranchId, b.Product!.Code, b.Product.Name, b.Product.Unit, b.ExpiryDate, b.Quantity, b.UnitCost }).ToListAsync();
        var t = new ReportTable
        {
            Headers = cost ? new() { "Chi nhánh", "Mã hàng", "Tên hàng", "ĐVT", "Hạn dùng", "Tình trạng", "Số lượng", "Giá trị" }
                           : new() { "Chi nhánh", "Mã hàng", "Tên hàng", "ĐVT", "Hạn dùng", "Tình trạng", "Số lượng" },
            NumericColumns = cost ? new() { 6, 7 } : new() { 6 },
            Subtitle = $"Tại ngày {today:dd/MM/yyyy} · đã hết hạn hoặc hết hạn trong {warn} ngày tới (đổi số ngày tại Quản trị › Thông tin chuỗi & tham số)"
        };
        foreach (var d in data)
        {
            var left = (d.ExpiryDate!.Value.Date - today).Days;
            var row = new List<object?> { names.GetValueOrDefault(d.BranchId), d.Code, d.Name, d.Unit, d.ExpiryDate,
                left < 0 ? $"Đã hết hạn {-left} ngày" : left == 0 ? "Hết hạn hôm nay" : $"Còn {left} ngày", d.Quantity };
            if (cost) row.Add(Math.Round(d.Quantity * d.UnitCost, 0));
            t.Rows.Add(row);
        }
        if (cost) t.Footer = new List<object?> { "Tổng cộng", null, null, null, null, null, null, Math.Round(data.Sum(x => x.Quantity * x.UnitCost), 0) };
        return t;
    }

    private async Task<ReportTable> LowStock()
    {
        var cost = Me.CanSeeCost;
        var products = await Db.Products.AsNoTracking().Include(p => p.Stocks).Where(p => p.IsActive && p.MinStock > 0)
            .OrderBy(p => p.Kind).ThenBy(p => p.Name).ToListAsync();
        var t = new ReportTable
        {
            Headers = cost ? new() { "Mã hàng", "Tên hàng", "Nhóm", "ĐVT", "Tồn hiện tại", "Tồn tối thiểu", "Cần nhập thêm", "Giá vốn BQ", "Tiền nhập dự kiến" }
                           : new() { "Mã hàng", "Tên hàng", "Nhóm", "ĐVT", "Tồn hiện tại", "Tồn tối thiểu", "Cần nhập thêm" },
            NumericColumns = cost ? new() { 4, 5, 6, 7, 8 } : new() { 4, 5, 6 },
            Subtitle = $"Tại ngày {DateTime.Today:dd/MM/yyyy} · mặt hàng có tồn thấp hơn mức tối thiểu khai báo trong danh mục thuốc"
        };
        decimal sum = 0;
        foreach (var p in products.Where(p => p.Stock < p.MinStock))
        {
            var need = p.MinStock - p.Stock;
            var row = new List<object?> { p.Code, p.Name, p.Kind.GetDisplayName(), p.Unit, p.Stock, p.MinStock, need };
            if (cost) { row.Add(Math.Round(p.AvgCost, 0)); row.Add(Math.Round(need * p.AvgCost, 0)); sum += Math.Round(need * p.AvgCost, 0); }
            t.Rows.Add(row);
        }
        if (cost) t.Footer = new List<object?> { null, "Tổng cộng", null, null, null, null, null, null, sum };
        return t;
    }
}
