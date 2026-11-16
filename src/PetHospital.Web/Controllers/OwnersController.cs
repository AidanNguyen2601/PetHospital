using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Controllers;

[RequirePerm(Perm.CustomerView)]
public class OwnersController : AppController
{
    public OwnersController(PetDbContext db) : base(db) { }

    public async Task<IActionResult> Index(ListFilter f, bool debt = false, string? export = null)
    {
        var q = Db.Owners.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var k = f.Q.Trim();
            q = q.Where(o => o.FullName.Contains(k) || o.Phone.Contains(k) || o.Code.Contains(k) || (o.Email != null && o.Email.Contains(k))
                || o.Pets.Any(p => p.Name.Contains(k) || p.Code == k));
        }
        var open = Db.Invoices.Where(i => i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Partial);
        if (debt) q = q.Where(o => open.Any(i => i.OwnerId == o.Id));
        q = q.OrderByDescending(o => o.CreatedAt);

        if (export == "xlsx")
        {
            var all = await q.Select(o => new { o.Code, o.FullName, o.Phone, o.Email, o.Address, Pets = o.Pets.Count, o.CreatedAt }).ToListAsync();
            return Excel("KhachHang", "Khách hàng", new[] { "Mã KH", "Họ tên", "Điện thoại", "Email", "Địa chỉ", "Số thú cưng", "Ngày tạo" },
                all.Select(o => (IReadOnlyList<object?>)new object?[] { o.Code, o.FullName, o.Phone, o.Email, o.Address, o.Pets, o.CreatedAt }));
        }

        var total = await q.CountAsync();
        var items = await q.Include(o => o.Pets).ThenInclude(p => p.Species)
            .Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync();
        var ids = items.Select(o => o.Id).ToList();
        ViewBag.Debts = await open.Where(i => i.OwnerId != null && ids.Contains(i.OwnerId.Value)).GroupBy(i => i.OwnerId!.Value)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.Total - x.Paid) }).ToDictionaryAsync(x => x.Key, x => x.Sum);
        ViewBag.Filter = f;
        ViewBag.Debt = debt;
        return View(new PagedList<Owner>(items, total, f.Page, f.PageSize));
    }

    public async Task<IActionResult> Details(int id)
    {
        var o = await Db.Owners.AsNoTracking().Include(x => x.Pets).ThenInclude(p => p.Species)
            .Include(x => x.Pets).ThenInclude(p => p.Breed).FirstOrDefaultAsync(x => x.Id == id);
        if (o == null) return NotFound();
        // Lịch sử khám ở mọi chi nhánh; hóa đơn, công nợ, lịch hẹn theo chi nhánh đang làm việc
        ViewBag.Visits = await Db.Visits.IgnoreQueryFilters().AsNoTracking().Include(v => v.Pet).Include(v => v.Vet).Where(v => v.OwnerId == id)
            .OrderByDescending(v => v.VisitAt).Take(30).ToListAsync();
        ViewBag.Invoices = Me.HasAny(Perm.InvoiceView, Perm.InvoiceManage)
            ? await Db.Invoices.AsNoTracking().Include(i => i.Pet).Where(i => i.OwnerId == id).OrderByDescending(i => i.Date).Take(30).ToListAsync()
            : new List<Invoice>();
        ViewBag.Appointments = await Db.Appointments.AsNoTracking().Include(a => a.Pet).Include(a => a.Vet)
            .Where(a => a.OwnerId == id && a.StartAt >= DateTime.Today).OrderBy(a => a.StartAt).ToListAsync();
        ViewBag.Debt = await Db.Invoices.Where(i => i.OwnerId == id && (i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Partial))
            .SumAsync(i => (decimal?)(i.Total - i.Paid)) ?? 0m;
        ViewBag.Spent = await Db.Invoices.Where(i => i.OwnerId == id && i.Status != InvoiceStatus.Cancelled && i.Status != InvoiceStatus.Draft)
            .SumAsync(i => (decimal?)i.Total) ?? 0m;
        return View(o);
    }

    [RequirePerm(Perm.CustomerEdit)]
    public IActionResult Create() => View("Form", new Owner());

    [RequirePerm(Perm.CustomerEdit)]
    public async Task<IActionResult> Edit(int id)
    {
        var o = await Db.Owners.FindAsync(id);
        return o == null ? NotFound() : View("Form", o);
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.CustomerEdit)]
    public async Task<IActionResult> Save(Owner m, string? next)
    {
        m.Phone = (m.Phone ?? "").Trim().Replace(" ", "").Replace(".", "");
        if (await Db.Owners.AnyAsync(o => o.Phone == m.Phone && o.Id != m.Id))
            ModelState.AddModelError(nameof(m.Phone), "Số điện thoại đã có trong danh bạ khách hàng.");
        if (!ModelState.IsValid) return View("Form", m);

        Owner o;
        if (m.Id == 0)
        {
            o = new Owner { Code = await Codes.NextPlainAsync(Db.Owners.Select(x => x.Code), "KH") };
            Db.Owners.Add(o);
        }
        else
        {
            o = await Db.Owners.FirstOrDefaultAsync(x => x.Id == m.Id) ?? throw new InvalidOperationException();
        }
        o.FullName = m.FullName.Trim(); o.Phone = m.Phone; o.Email = m.Email; o.Address = m.Address; o.Note = m.Note; o.IsActive = m.IsActive;
        await Db.SaveChangesAsync();
        TempData["Success"] = $"Đã lưu khách hàng {o.FullName}.";
        if (next == "pet") return RedirectToAction("Create", "Pets", new { ownerId = o.Id });
        return RedirectToAction(nameof(Details), new { id = o.Id });
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.CustomerEdit)]
    public async Task<IActionResult> Delete(int id)
    {
        var o = await Db.Owners.FindAsync(id);
        if (o == null) return NotFound();
        var used = await Db.Pets.IgnoreQueryFilters().AnyAsync(p => p.OwnerId == id) || await Db.Invoices.IgnoreQueryFilters().AnyAsync(i => i.OwnerId == id)
            || await Db.Appointments.IgnoreQueryFilters().AnyAsync(a => a.OwnerId == id) || await Db.Visits.IgnoreQueryFilters().AnyAsync(v => v.OwnerId == id);
        if (used)
        {
            o.IsActive = false;
            await Db.SaveChangesAsync();
            return BackWithSuccess("Khách hàng đã có dữ liệu phát sinh nên được chuyển sang ngừng hoạt động (không xóa).", nameof(Details), new { id });
        }
        Db.Owners.Remove(o);
        await Db.SaveChangesAsync();
        return BackWithSuccess("Đã xóa khách hàng.", nameof(Index));
    }

    /// <summary>Tìm khách hàng cho ô chọn (JSON): mã, tên, điện thoại</summary>
    public async Task<IActionResult> Lookup(string? q)
    {
        q = q?.Trim() ?? "";
        var list = await Db.Owners.AsNoTracking().Where(o => o.IsActive && (q == "" || o.FullName.Contains(q) || o.Phone.Contains(q) || o.Code.Contains(q)))
            .OrderBy(o => o.FullName).Take(20).Select(o => new { id = o.Id, text = o.FullName + " – " + o.Phone, code = o.Code }).ToListAsync();
        return Json(list);
    }

    /// <summary>Danh sách thú cưng của khách (JSON) cho ô chọn</summary>
    public async Task<IActionResult> Pets(int id)
    {
        var list = await Db.Pets.AsNoTracking().Where(p => p.OwnerId == id && !p.IsDeceased).OrderBy(p => p.Name)
            .Select(p => new { id = p.Id, text = p.Name + " (" + (p.Species != null ? p.Species.Name : "") + (p.Breed != null ? " " + p.Breed.Name : "") + ")", weight = p.WeightKg })
            .ToListAsync();
        return Json(list);
    }
}
