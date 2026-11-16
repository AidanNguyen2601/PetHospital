using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using PetHospital.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Controllers;

[RequirePerm(Perm.InvoiceView, Perm.InvoiceManage)]
public class InvoicesController : AppController
{
    public InvoicesController(PetDbContext db) : base(db) { }

    public async Task<IActionResult> Index(ListFilter f, string tab = "pending", InvoiceStatus? status = null, DateTime? from = null, DateTime? to = null, string? export = null)
    {
        var q = Db.Invoices.AsNoTracking().Include(i => i.Owner).Include(i => i.Pet).Include(i => i.CreatedBy).AsQueryable();
        if (tab == "pending")
            q = q.Where(i => i.Status == InvoiceStatus.Draft || i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Partial);
        else
        {
            tab = "all";
            from ??= DateTime.Today.AddDays(-30);
            q = q.Where(i => i.Date >= from);
            if (to.HasValue) { var end = to.Value.Date.AddDays(1); q = q.Where(i => i.Date < end); }
            if (status.HasValue) q = q.Where(i => i.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var k = f.Q.Trim();
            q = q.Where(i => i.Code.Contains(k) || (i.CustomerName != null && i.CustomerName.Contains(k)) || i.Owner!.FullName.Contains(k) || i.Owner.Phone.Contains(k) || i.Pet!.Name.Contains(k));
        }
        q = q.OrderByDescending(i => i.Date).ThenByDescending(i => i.Id);
        if (export == "xlsx")
        {
            var rows = await q.Take(10000).ToListAsync();
            return Excel("HoaDon", "Hóa đơn", new[] { "Số HĐ", "Ngày", "Khách hàng", "Điện thoại", "Thú cưng", "Tạm tính", "Giảm giá", "Tổng tiền", "Đã thu", "Còn nợ", "Trạng thái" },
                rows.Select(i => (IReadOnlyList<object?>)new object?[] { i.Code, i.Date, i.Owner?.FullName ?? i.CustomerName, i.Owner?.Phone, i.Pet?.Name, i.SubTotal, i.Discount, i.Total, i.Paid,
                    i.Status is InvoiceStatus.Cancelled or InvoiceStatus.Draft ? 0 : i.Total - i.Paid, i.Status.GetDisplayName() }));
        }
        var total = await q.CountAsync();
        var valid = q.Where(i => i.Status != InvoiceStatus.Cancelled && i.Status != InvoiceStatus.Draft);
        ViewBag.SumTotal = await valid.SumAsync(i => (decimal?)i.Total) ?? 0m;
        ViewBag.SumPaid = await valid.SumAsync(i => (decimal?)i.Paid) ?? 0m;
        var items = await q.Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync();
        ViewBag.Tab = tab; ViewBag.Status = status; ViewBag.From = from; ViewBag.To = to; ViewBag.Filter = f;
        return View(new PagedList<Invoice>(items, total, f.Page, f.PageSize));
    }

    public async Task<IActionResult> Details(int id)
    {
        var inv = await Db.Invoices.AsNoTracking().Include(i => i.Owner).Include(i => i.Pet).Include(i => i.CreatedBy)
            .Include(i => i.Lines).ThenInclude(l => l.Vet).Include(i => i.Payments).ThenInclude(p => p.User)
            .AsSplitQuery().FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return NotFound();
        ViewBag.VisitCode = inv.VisitId.HasValue ? await Db.Visits.Where(v => v.Id == inv.VisitId).Select(v => v.Code).FirstOrDefaultAsync() : null;
        ViewBag.AdmissionCode = inv.AdmissionId.HasValue ? await Db.Admissions.Where(v => v.Id == inv.AdmissionId).Select(v => v.Code).FirstOrDefaultAsync() : null;
        return View(inv);
    }

    // ---------- Lập / sửa hóa đơn nháp (bán lẻ, bổ sung dòng) ----------

    [RequirePerm(Perm.InvoiceManage)]
    public async Task<IActionResult> Create(int? ownerId, int? petId)
    {
        var m = new InvoiceFormModel { OwnerId = ownerId, PetId = petId };
        if (petId.HasValue && ownerId == null) m.OwnerId = await Db.Pets.Where(p => p.Id == petId).Select(p => (int?)p.OwnerId).FirstOrDefaultAsync();
        await FormData(m);
        return View("Form", m);
    }

    [RequirePerm(Perm.InvoiceManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var inv = await Db.Invoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return NotFound();
        if (inv.Status != InvoiceStatus.Draft) return BackWithError("Chỉ sửa được hóa đơn nháp.", nameof(Details), new { id });
        var m = new InvoiceFormModel
        {
            Id = inv.Id, OwnerId = inv.OwnerId, PetId = inv.PetId, CustomerName = inv.CustomerName, Discount = inv.Discount, Note = inv.Note,
            Lines = inv.Lines.OrderBy(l => l.Id).Select(l => new InvoiceLineInput
            {
                Item = l.ServiceItemId.HasValue ? $"S:{l.ServiceItemId}" : l.ProductId.HasValue ? $"P:{l.ProductId}" : "",
                Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice, Unit = l.Unit
            }).ToList()
        };
        ViewBag.Code = inv.Code;
        await FormData(m);
        return View("Form", m);
    }

    private async Task FormData(InvoiceFormModel m)
    {
        var l = Svc<Lookups>();
        ViewBag.ServiceList = await l.ServicesAsync();
        ViewBag.ProductList = await l.ProductsAsync();
        ViewBag.Pets = await l.PetsOfOwnerAsync(m.OwnerId, m.PetId);
        ViewBag.OwnerText = m.OwnerId.HasValue ? await Db.Owners.Where(o => o.Id == m.OwnerId).Select(o => o.FullName + " – " + o.Phone).FirstOrDefaultAsync() : null;
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.InvoiceManage)]
    public async Task<IActionResult> Save(InvoiceFormModel m, string? command)
    {
        foreach (var k in ModelState.Keys.Where(k => k.StartsWith("Lines[")).ToList()) ModelState.Remove(k);
        var lines = m.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Item) || !string.IsNullOrWhiteSpace(l.Description)).ToList();
        if (lines.Count == 0) ModelState.AddModelError("", "Nhập ít nhất một dòng hàng / dịch vụ.");
        if (lines.Any(l => l.Quantity <= 0 || l.UnitPrice < 0)) ModelState.AddModelError("", "Số lượng phải lớn hơn 0, đơn giá không âm.");
        if (m.OwnerId == null && string.IsNullOrWhiteSpace(m.CustomerName)) m.CustomerName = "Khách lẻ";
        if (m.PetId.HasValue && !await Db.Pets.AnyAsync(p => p.Id == m.PetId && p.OwnerId == m.OwnerId)) m.PetId = null;

        var sIds = lines.Select(l => ParseItem(l.Item)).Where(x => x.Kind == 'S').Select(x => x.Id).ToList();
        var pIds = lines.Select(l => ParseItem(l.Item)).Where(x => x.Kind == 'P').Select(x => x.Id).ToList();
        var services = await Db.ServiceItems.AsNoTracking().Where(s => sIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        var products = await Db.Products.AsNoTracking().Where(p => pIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        Invoice inv;
        if (m.Id == 0)
        {
            inv = new Invoice { Code = await Svc<InvoiceService>().NextCodeAsync(), CreatedById = CurrentUserId, Date = DateTime.Now };
        }
        else
        {
            inv = await Db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == m.Id) ?? throw new InvalidOperationException();
            if (inv.Status != InvoiceStatus.Draft) return BackWithError("Chỉ sửa được hóa đơn nháp.", nameof(Details), new { id = inv.Id });
        }
        if (!ModelState.IsValid)
        {
            ViewBag.Code = inv.Code;
            await FormData(m);
            return View("Form", m);
        }

        inv.OwnerId = m.OwnerId; inv.PetId = m.PetId; inv.CustomerName = m.OwnerId == null ? m.CustomerName : null; inv.Discount = m.Discount; inv.Note = m.Note;
        // Giữ bác sĩ của dòng cũ (hóa đơn lập từ phiếu khám)
        var vetOf = inv.Lines.Where(l => l.VetId.HasValue).GroupBy(l => l.ServiceItemId?.ToString() ?? "P" + l.ProductId).ToDictionary(g => g.Key, g => g.First().VetId);
        var defaultVet = inv.Lines.Select(l => l.VetId).FirstOrDefault(v => v.HasValue);
        inv.Lines.Clear();
        foreach (var l in lines)
        {
            var (kind, itemId) = ParseItem(l.Item);
            var line = new InvoiceLine { Quantity = l.Quantity, UnitPrice = l.UnitPrice, Description = (l.Description ?? "").Trim(), Unit = l.Unit };
            if (kind == 'S' && services.TryGetValue(itemId, out var s))
            {
                line.Kind = InvoiceLineKind.Service; line.ServiceItemId = s.Id; line.Unit ??= s.Unit;
                if (line.Description == "") line.Description = s.Name;
                line.VetId = vetOf.GetValueOrDefault(s.Id.ToString()) ?? defaultVet;
            }
            else if (kind == 'P' && products.TryGetValue(itemId, out var p))
            {
                line.Kind = InvoiceLineKind.Product; line.ProductId = p.Id; line.Unit ??= p.Unit;
                if (line.Description == "") line.Description = p.Name;
                line.VetId = vetOf.GetValueOrDefault("P" + p.Id) ?? defaultVet;
            }
            else
            {
                line.Kind = InvoiceLineKind.Other;
                if (line.Description == "") line.Description = "Khác";
            }
            inv.Lines.Add(line);
        }
        inv.Recalc();
        if (inv.Id == 0) Db.Invoices.Add(inv);
        await Db.SaveChangesAsync();

        if (command == "issue")
        {
            var err = await Svc<InvoiceService>().IssueAsync(inv);
            if (err != null) return BackWithError($"Đã lưu nháp {inv.Code}. {err}", nameof(Details), new { id = inv.Id });
            await Db.SaveChangesAsync();
            return BackWithSuccess($"Đã phát hành hóa đơn {inv.Code} – tổng {inv.Total.ToMoney()} đ.", nameof(Details), new { id = inv.Id });
        }
        return BackWithSuccess($"Đã lưu hóa đơn nháp {inv.Code}.", nameof(Details), new { id = inv.Id });
    }

    private static (char Kind, int Id) ParseItem(string? item)
    {
        if (string.IsNullOrWhiteSpace(item) || item.Length < 3 || item[1] != ':') return (' ', 0);
        return int.TryParse(item[2..], out var id) ? (item[0], id) : (' ', 0);
    }

    // ---------- Phát hành, thu tiền, hủy ----------

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.InvoiceManage)]
    public async Task<IActionResult> Issue(int id, decimal? payAmount, PaymentMethod method = PaymentMethod.Cash)
    {
        var inv = await Db.Invoices.Include(i => i.Lines).Include(i => i.Payments).FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return NotFound();
        var svc = Svc<InvoiceService>();
        var err = await svc.IssueAsync(inv);
        if (err != null) return BackWithError(err, nameof(Details), new { id });
        if (payAmount is > 0)
        {
            err = svc.AddPayment(inv, Math.Min(payAmount.Value, inv.Total), method, null);
            if (err != null) TempData["Error"] = err;
        }
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã phát hành hóa đơn {inv.Code}.", nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.InvoiceManage)]
    public async Task<IActionResult> Pay(int id, decimal amount, PaymentMethod method, string? note)
    {
        var inv = await Db.Invoices.Include(i => i.Payments).FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return NotFound();
        var err = Svc<InvoiceService>().AddPayment(inv, amount, method, note);
        if (err != null) return BackWithError(err, nameof(Details), new { id });
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã thu {amount.ToMoney()} đ ({method.GetDisplayName()}).", nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.InvoiceCancel)]
    public async Task<IActionResult> Cancel(int id, string reason)
    {
        var inv = await Db.Invoices.Include(i => i.Payments).FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return NotFound();
        var err = await Svc<InvoiceService>().CancelAsync(inv, reason);
        if (err != null) return BackWithError(err, nameof(Details), new { id });
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã hủy hóa đơn {inv.Code}, hoàn nhập kho.", nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.InvoiceManage)]
    public async Task<IActionResult> Delete(int id)
    {
        var inv = await Db.Invoices.FindAsync(id);
        if (inv == null) return NotFound();
        if (inv.Status != InvoiceStatus.Draft) return BackWithError("Chỉ xóa được hóa đơn nháp. Hóa đơn đã phát hành dùng \"Hủy\".", nameof(Details), new { id });
        foreach (var v in await Db.Visits.Where(v => v.InvoiceId == id).ToListAsync()) v.InvoiceId = null;
        foreach (var a in await Db.Admissions.Where(a => a.InvoiceId == id).ToListAsync()) a.InvoiceId = null;
        Db.Invoices.Remove(inv);
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã xóa hóa đơn nháp {inv.Code}.", nameof(Index));
    }

    /// <summary>In hóa đơn: khổ A5 hoặc máy in nhiệt 80mm (?size=k80)</summary>
    public async Task<IActionResult> Print(int id, string size = "a5")
    {
        var inv = await Db.Invoices.AsNoTracking().Include(i => i.Owner).Include(i => i.Pet).Include(i => i.CreatedBy)
            .Include(i => i.Lines).Include(i => i.Payments).AsSplitQuery().FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null) return NotFound();
        ViewBag.Settings = Svc<SettingsService>();
        ViewBag.Size = size == "k80" ? "k80" : "a5";
        ViewData["Layout"] = "_PrintLayout";
        return View(inv);
    }
}
