using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Controllers;

/// <summary>Dịch vụ & bảng giá</summary>
public class ServicesController : AppController
{
    public ServicesController(PetDbContext db) : base(db) { }

    public async Task<IActionResult> Index(ServiceKind? kind, string? q, string? export)
    {
        var list = Db.ServiceItems.AsNoTracking().AsQueryable();
        if (kind.HasValue) list = list.Where(s => s.Kind == kind);
        if (!string.IsNullOrWhiteSpace(q)) list = list.Where(s => s.Name.Contains(q) || s.Code.Contains(q));
        var items = await list.OrderBy(s => s.Kind).ThenBy(s => s.Code).ToListAsync();
        if (export == "xlsx")
            return Excel("BangGiaDichVu", "Bảng giá", new[] { "Mã", "Tên dịch vụ", "Nhóm", "ĐVT", "Đơn giá", "Thời lượng (phút)", "Đang áp dụng" },
                items.Select(s => (IReadOnlyList<object?>)new object?[] { s.Code, s.Name, s.Kind.GetDisplayName(), s.Unit, s.Price, s.DurationMinutes, s.IsActive ? "Có" : "Không" }));
        ViewBag.Kind = kind; ViewBag.Q = q;
        return View(items);
    }

    [RequirePerm(Perm.Catalog)]
    public IActionResult Create() => View("Form", new ServiceItem());

    [RequirePerm(Perm.Catalog)]
    public async Task<IActionResult> Edit(int id)
    {
        var s = await Db.ServiceItems.FindAsync(id);
        return s == null ? NotFound() : View("Form", s);
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.Catalog)]
    public async Task<IActionResult> Save(ServiceItem m)
    {
        m.Code = (m.Code ?? "").Trim().ToUpperInvariant();
        if (await Db.ServiceItems.AnyAsync(s => s.Code == m.Code && s.Id != m.Id)) ModelState.AddModelError(nameof(m.Code), "Mã dịch vụ đã tồn tại.");
        if (!ModelState.IsValid) return View("Form", m);
        if (m.Id == 0) Db.ServiceItems.Add(m);
        else Db.ServiceItems.Update(m);
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã lưu dịch vụ {m.Name}.", nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.Catalog)]
    public async Task<IActionResult> Delete(int id)
    {
        var s = await Db.ServiceItems.FindAsync(id);
        if (s == null) return NotFound();
        var used = await Db.VisitServices.AnyAsync(x => x.ServiceItemId == id) || await Db.InvoiceLines.AnyAsync(x => x.ServiceItemId == id)
            || await Db.Appointments.AnyAsync(x => x.ServiceItemId == id);
        if (used) { s.IsActive = false; await Db.SaveChangesAsync(); return BackWithSuccess("Dịch vụ đã phát sinh giao dịch nên được ngừng áp dụng (không xóa).", nameof(Index)); }
        Db.ServiceItems.Remove(s);
        await Db.SaveChangesAsync();
        return BackWithSuccess("Đã xóa dịch vụ.", nameof(Index));
    }
}

/// <summary>Loài & giống</summary>
[RequirePerm(Perm.Catalog, Perm.CustomerEdit)]
public class SpeciesController : AppController
{
    public SpeciesController(PetDbContext db) : base(db) { }

    public async Task<IActionResult> Index()
    {
        ViewBag.PetCounts = await Db.Pets.GroupBy(p => p.SpeciesId).Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key ?? 0, x => x.C);
        return View(await Db.Species.AsNoTracking().Include(s => s.Breeds).OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSpecies(int id, string name, int sortOrder)
    {
        name = (name ?? "").Trim();
        if (name == "") return BackWithError("Nhập tên loài.", nameof(Index));
        if (await Db.Species.AnyAsync(s => s.Name == name && s.Id != id)) return BackWithError("Loài đã tồn tại.", nameof(Index));
        var s = id == 0 ? new Species() : await Db.Species.FindAsync(id);
        if (s == null) return NotFound();
        s.Name = name; s.SortOrder = sortOrder;
        if (id == 0) Db.Species.Add(s);
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã lưu loài {name}.", nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBreed(int speciesId, string name)
    {
        name = (name ?? "").Trim();
        if (name == "") return BackWithError("Nhập tên giống.", nameof(Index));
        if (await Db.Breeds.AnyAsync(b => b.SpeciesId == speciesId && b.Name == name)) return BackWithError("Giống đã tồn tại.", nameof(Index));
        Db.Breeds.Add(new Breed { SpeciesId = speciesId, Name = name });
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã thêm giống {name}.", nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBreed(int id)
    {
        var b = await Db.Breeds.FindAsync(id);
        if (b == null) return NotFound();
        if (await Db.Pets.AnyAsync(p => p.BreedId == id)) return BackWithError("Giống đang được dùng cho thú cưng, không xóa được.", nameof(Index));
        Db.Breeds.Remove(b);
        await Db.SaveChangesAsync();
        return BackWithSuccess("Đã xóa giống.", nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSpecies(int id)
    {
        var s = await Db.Species.Include(x => x.Breeds).FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();
        if (await Db.Pets.AnyAsync(p => p.SpeciesId == id)) return BackWithError("Loài đang được dùng, không xóa được.", nameof(Index));
        Db.Breeds.RemoveRange(s.Breeds);
        Db.Species.Remove(s);
        await Db.SaveChangesAsync();
        return BackWithSuccess("Đã xóa loài.", nameof(Index));
    }
}

/// <summary>Chuồng / phòng lưu trú</summary>
[RequirePerm(Perm.Catalog, Perm.Admission)]
public class CagesController : AppController
{
    public CagesController(PetDbContext db) : base(db) { }

    public async Task<IActionResult> Index()
    {
        ViewBag.Busy = await Db.Admissions.Where(a => a.Status == AdmissionStatus.Active && a.CageId != null).Select(a => a.CageId!.Value).ToListAsync();
        return View(await Db.Cages.AsNoTracking().OrderBy(c => c.Code).ToListAsync());
    }

    [RequirePerm(Perm.Catalog)]
    public IActionResult Create() => View("Form", new Cage());

    [RequirePerm(Perm.Catalog)]
    public async Task<IActionResult> Edit(int id)
    {
        var c = await Db.Cages.FindAsync(id);
        return c == null ? NotFound() : View("Form", c);
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.Catalog)]
    public async Task<IActionResult> Save(Cage m)
    {
        m.Code = (m.Code ?? "").Trim().ToUpperInvariant();
        if (await Db.Cages.AnyAsync(c => c.Code == m.Code && c.Id != m.Id)) ModelState.AddModelError(nameof(m.Code), "Mã chuồng đã tồn tại.");
        if (!ModelState.IsValid) return View("Form", m);
        if (m.Id == 0) Db.Cages.Add(m); else Db.Cages.Update(m);
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã lưu chuồng {m.Code}.", nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.Catalog)]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await Db.Cages.FindAsync(id);
        if (c == null) return NotFound();
        if (await Db.Admissions.AnyAsync(a => a.CageId == id)) { c.IsActive = false; await Db.SaveChangesAsync(); return BackWithSuccess("Chuồng đã có lịch sử sử dụng nên được ngừng sử dụng.", nameof(Index)); }
        Db.Cages.Remove(c);
        await Db.SaveChangesAsync();
        return BackWithSuccess("Đã xóa chuồng.", nameof(Index));
    }
}
