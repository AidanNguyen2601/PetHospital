using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Controllers;

[RequirePerm(Perm.Vaccine, Perm.VisitExam)]
public class VaccinationsController : AppController
{
    public VaccinationsController(PetDbContext db) : base(db) { }

    /// <summary>Nhắc lịch tiêm: quá hạn + đến hạn trong N ngày; hoặc tra cứu sổ tiêm</summary>
    public async Task<IActionResult> Index(ListFilter f, string tab = "due", int? days = null, string? export = null)
    {
        var remind = days ?? await Svc<SettingsService>().GetIntAsync(SettingsService.VaccineRemindDays, 7);
        var q = Db.Vaccinations.AsNoTracking().Include(v => v.Pet).ThenInclude(p => p!.Owner).Include(v => v.Pet).ThenInclude(p => p!.Species).Include(v => v.Vet).AsQueryable();
        if (tab == "due")
        {
            var until = DateTime.Today.AddDays(remind);
            q = q.Where(v => !v.ReminderDone && v.NextDueDate != null && v.NextDueDate <= until && !v.Pet!.IsDeceased).OrderBy(v => v.NextDueDate);
        }
        else
        {
            tab = "all";
            q = q.OrderByDescending(v => v.Date).ThenByDescending(v => v.Id);
        }
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var k = f.Q.Trim();
            q = q.Where(v => v.VaccineName.Contains(k) || v.Pet!.Name.Contains(k) || v.Pet.Owner!.FullName.Contains(k) || v.Pet.Owner.Phone.Contains(k));
        }
        if (export == "xlsx")
        {
            var rows = await q.Take(5000).ToListAsync();
            return Excel("TiemPhong", "Tiêm phòng", new[] { "Thú cưng", "Mã", "Chủ nuôi", "Điện thoại", "Vắc-xin", "Mũi", "Ngày tiêm", "Hẹn mũi tiếp" },
                rows.Select(v => (IReadOnlyList<object?>)new object?[] { v.Pet!.Name, v.Pet.Code, v.Pet.Owner!.FullName, v.Pet.Owner.Phone, v.VaccineName, v.DoseNumber, v.Date, v.NextDueDate }));
        }
        var total = await q.CountAsync();
        var items = await q.Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync();
        ViewBag.Tab = tab; ViewBag.Days = remind; ViewBag.Filter = f;
        return View(new PagedList<Vaccination>(items, total, f.Page, f.PageSize));
    }

    /// <summary>Đánh dấu đã nhắc / khách tiêm nơi khác (không hiện trong danh sách nhắc)</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkDone(int id)
    {
        var v = await Db.Vaccinations.FindAsync(id);
        if (v == null) return NotFound();
        v.ReminderDone = true;
        await Db.SaveChangesAsync();
        return BackWithSuccess("Đã đánh dấu hoàn tất nhắc lịch.", nameof(Index));
    }

    /// <summary>Ghi sổ tiêm ngoài lượt khám (tiêm trước đây / tiêm nơi khác) – không trừ kho, không tính tiền</summary>
    public async Task<IActionResult> Create(int petId)
    {
        var pet = await Db.Pets.AsNoTracking().Include(p => p.Owner).FirstOrDefaultAsync(p => p.Id == petId);
        if (pet == null) return NotFound();
        ViewBag.Pet = pet;
        ViewBag.Vets = await Svc<Lookups>().VetsAsync(null, "-- Không ghi / tiêm nơi khác --");
        return View(new Vaccination { PetId = petId, Date = DateTime.Today });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Vaccination m)
    {
        ModelState.Remove(nameof(m.Pet)); ModelState.Remove(nameof(m.Product)); ModelState.Remove(nameof(m.Vet)); ModelState.Remove(nameof(m.Visit));
        if (m.Date > DateTime.Today) ModelState.AddModelError(nameof(m.Date), "Ngày tiêm không được sau hôm nay.");
        if (m.NextDueDate.HasValue && m.NextDueDate <= m.Date) ModelState.AddModelError(nameof(m.NextDueDate), "Ngày hẹn mũi tiếp phải sau ngày tiêm.");
        var pet = await Db.Pets.AsNoTracking().Include(p => p.Owner).FirstOrDefaultAsync(p => p.Id == m.PetId);
        if (pet == null) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Pet = pet;
            ViewBag.Vets = await Svc<Lookups>().VetsAsync(m.VetId, "-- Không ghi / tiêm nơi khác --");
            return View(m);
        }
        foreach (var old in await Db.Vaccinations.Where(x => x.PetId == m.PetId && !x.ReminderDone && x.VaccineName == m.VaccineName).ToListAsync())
            old.ReminderDone = true;
        Db.Vaccinations.Add(new Vaccination
        {
            PetId = m.PetId, VaccineName = m.VaccineName.Trim(), DoseNumber = m.DoseNumber, Date = m.Date, NextDueDate = m.NextDueDate,
            LotNo = m.LotNo, VetId = m.VetId, Note = m.Note
        });
        await Db.SaveChangesAsync();
        return BackWithSuccess("Đã ghi sổ tiêm.", "Details", new { controller = "Pets", id = m.PetId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var v = await Db.Vaccinations.FindAsync(id);
        if (v == null) return NotFound();
        if (v.VisitId.HasValue) return BackWithError("Mũi tiêm thuộc phiếu khám – xóa trong phiếu khám.", "Details", new { controller = "Pets", id = v.PetId });
        Db.Vaccinations.Remove(v);
        await Db.SaveChangesAsync();
        return BackWithSuccess("Đã xóa mũi tiêm.", "Details", new { controller = "Pets", id = v.PetId });
    }
}
