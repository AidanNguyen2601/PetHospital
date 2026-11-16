using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Controllers;

[RequirePerm(Perm.CustomerView)]
public class PetsController : AppController
{
    public PetsController(PetDbContext db) : base(db) { }

    public async Task<IActionResult> Index(ListFilter f, int? speciesId)
    {
        var q = Db.Pets.AsNoTracking().Include(p => p.Owner).Include(p => p.Species).Include(p => p.Breed).AsQueryable();
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var k = f.Q.Trim();
            q = q.Where(p => p.Name.Contains(k) || p.Code.Contains(k) || (p.Microchip != null && p.Microchip.Contains(k))
                || p.Owner!.FullName.Contains(k) || p.Owner.Phone.Contains(k));
        }
        if (speciesId.HasValue) q = q.Where(p => p.SpeciesId == speciesId);
        q = q.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id);
        var total = await q.CountAsync();
        var items = await q.Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync();
        var ids = items.Select(p => p.Id).ToList();
        ViewBag.LastVisit = await Db.Visits.Where(v => ids.Contains(v.PetId) && v.Status != VisitStatus.Cancelled)
            .GroupBy(v => v.PetId).Select(g => new { g.Key, Last = g.Max(x => x.VisitAt) }).ToDictionaryAsync(x => x.Key, x => x.Last);
        ViewBag.Species = await Svc<Lookups>().SpeciesAsync(speciesId, "-- Tất cả loài --");
        ViewBag.Filter = f;
        return View(new PagedList<Pet>(items, total, f.Page, f.PageSize));
    }

    public async Task<IActionResult> Details(int id)
    {
        var p = await Db.Pets.AsNoTracking().Include(x => x.Owner).Include(x => x.Species).Include(x => x.Breed).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        ViewBag.Visits = await Db.Visits.AsNoTracking().Include(v => v.Vet).Include(v => v.Medicines).ThenInclude(m => m.Product)
            .Include(v => v.Services).ThenInclude(s => s.ServiceItem)
            .Where(v => v.PetId == id).OrderByDescending(v => v.VisitAt).ToListAsync();
        ViewBag.Vaccinations = await Db.Vaccinations.AsNoTracking().Include(v => v.Vet).Where(v => v.PetId == id).OrderByDescending(v => v.Date).ToListAsync();
        ViewBag.Admissions = await Db.Admissions.AsNoTracking().Include(a => a.Cage).Where(a => a.PetId == id).OrderByDescending(a => a.AdmitAt).ToListAsync();
        ViewBag.Appointments = await Db.Appointments.AsNoTracking().Include(a => a.Vet).Include(a => a.ServiceItem)
            .Where(a => a.PetId == id && a.StartAt >= DateTime.Today && (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Confirmed))
            .OrderBy(a => a.StartAt).ToListAsync();
        return View(p);
    }

    /// <summary>In hồ sơ bệnh án</summary>
    public async Task<IActionResult> Record(int id)
    {
        var p = await Db.Pets.AsNoTracking().Include(x => x.Owner).Include(x => x.Species).Include(x => x.Breed).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        ViewBag.Visits = await Db.Visits.AsNoTracking().Include(v => v.Vet).Include(v => v.Medicines).ThenInclude(m => m.Product)
            .Include(v => v.Services).ThenInclude(s => s.ServiceItem)
            .Where(v => v.PetId == id && v.Status == VisitStatus.Completed).OrderByDescending(v => v.VisitAt).ToListAsync();
        ViewBag.Vaccinations = await Db.Vaccinations.AsNoTracking().Where(v => v.PetId == id).OrderByDescending(v => v.Date).ToListAsync();
        ViewData["Layout"] = "_PrintLayout";
        return View(p);
    }

    [RequirePerm(Perm.CustomerEdit)]
    public async Task<IActionResult> Create(int? ownerId)
    {
        if (ownerId == null) return BackWithError("Chọn khách hàng trước khi thêm thú cưng.", "Index", new { controller = "Owners" });
        var owner = await Db.Owners.AsNoTracking().FirstOrDefaultAsync(o => o.Id == ownerId);
        if (owner == null) return NotFound();
        await FormData(null);
        ViewBag.OwnerName = $"{owner.FullName} – {owner.Phone}";
        return View("Form", new Pet { OwnerId = owner.Id });
    }

    [RequirePerm(Perm.CustomerEdit)]
    public async Task<IActionResult> Edit(int id)
    {
        var p = await Db.Pets.Include(x => x.Owner).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        await FormData(p.SpeciesId);
        ViewBag.OwnerName = $"{p.Owner!.FullName} – {p.Owner.Phone}";
        return View("Form", p);
    }

    private async Task FormData(int? speciesId)
    {
        ViewBag.Species = await Svc<Lookups>().SpeciesAsync(speciesId);
        ViewBag.Breeds = await Svc<Lookups>().BreedsAsync();
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.CustomerEdit)]
    public async Task<IActionResult> Save(Pet m)
    {
        ModelState.Remove(nameof(m.Owner));
        if (m.BirthDate > DateTime.Today) ModelState.AddModelError(nameof(m.BirthDate), "Ngày sinh không được sau hôm nay.");
        if (m.BreedId.HasValue && !await Db.Breeds.AnyAsync(b => b.Id == m.BreedId && b.SpeciesId == m.SpeciesId)) m.BreedId = null;
        if (!string.IsNullOrWhiteSpace(m.Microchip) && await Db.Pets.AnyAsync(p => p.Microchip == m.Microchip && p.Id != m.Id))
            ModelState.AddModelError(nameof(m.Microchip), "Số vi mạch đã được gắn cho thú cưng khác.");
        var owner = await Db.Owners.AsNoTracking().FirstOrDefaultAsync(o => o.Id == m.OwnerId);
        if (owner == null) return NotFound();
        if (!ModelState.IsValid)
        {
            await FormData(m.SpeciesId);
            ViewBag.OwnerName = $"{owner.FullName} – {owner.Phone}";
            return View("Form", m);
        }

        Pet p;
        if (m.Id == 0)
        {
            p = new Pet { OwnerId = m.OwnerId, Code = await Codes.NextPlainAsync(Db.Pets.Select(x => x.Code), "TC") };
            Db.Pets.Add(p);
        }
        else p = await Db.Pets.FirstOrDefaultAsync(x => x.Id == m.Id) ?? throw new InvalidOperationException();
        p.Name = m.Name.Trim(); p.SpeciesId = m.SpeciesId; p.BreedId = m.BreedId; p.Gender = m.Gender; p.BirthDate = m.BirthDate; p.Color = m.Color;
        p.WeightKg = m.WeightKg; p.Microchip = string.IsNullOrWhiteSpace(m.Microchip) ? null : m.Microchip.Trim(); p.Neutered = m.Neutered;
        p.Allergies = m.Allergies; p.Note = m.Note; p.IsDeceased = m.IsDeceased;
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã lưu hồ sơ {p.Name}.", nameof(Details), new { id = p.Id });
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.CustomerEdit)]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await Db.Pets.FindAsync(id);
        if (p == null) return NotFound();
        var used = await Db.Visits.AnyAsync(v => v.PetId == id) || await Db.Vaccinations.AnyAsync(v => v.PetId == id)
            || await Db.Admissions.AnyAsync(a => a.PetId == id) || await Db.Invoices.AnyAsync(i => i.PetId == id) || await Db.Appointments.AnyAsync(a => a.PetId == id);
        if (used) return BackWithError("Thú cưng đã có bệnh án / hóa đơn nên không xóa được. Có thể đánh dấu \"Đã mất\" nếu cần.", nameof(Details), new { id });
        var ownerId = p.OwnerId;
        Db.Pets.Remove(p);
        await Db.SaveChangesAsync();
        return BackWithSuccess("Đã xóa thú cưng.", "Details", new { controller = "Owners", id = ownerId });
    }
}
