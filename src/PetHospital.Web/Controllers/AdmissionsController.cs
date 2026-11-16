using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Controllers;

[RequirePerm(Perm.Admission)]
public class AdmissionsController : AppController
{
    public AdmissionsController(PetDbContext db) : base(db) { }

    /// <summary>Sơ đồ chuồng + danh sách đang lưu chuồng / lịch sử</summary>
    public async Task<IActionResult> Index(ListFilter f, string tab = "board")
    {
        ViewBag.Tab = tab;
        if (tab == "board")
        {
            ViewBag.Cages = await Db.Cages.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync();
            ViewBag.Active = await Db.Admissions.AsNoTracking().Include(a => a.Pet).ThenInclude(p => p!.Species).Include(a => a.Owner).Include(a => a.Vet)
                .Where(a => a.Status == AdmissionStatus.Active).ToListAsync();
            return View(new PagedList<Admission>(new List<Admission>(), 0, 1, f.PageSize));
        }
        var q = Db.Admissions.AsNoTracking().Include(a => a.Pet).Include(a => a.Owner).Include(a => a.Cage).AsQueryable();
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var k = f.Q.Trim();
            q = q.Where(a => a.Code.Contains(k) || a.Pet!.Name.Contains(k) || a.Owner!.FullName.Contains(k) || a.Owner.Phone.Contains(k));
        }
        q = q.OrderByDescending(a => a.AdmitAt);
        var total = await q.CountAsync();
        var items = await q.Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync();
        ViewBag.Filter = f;
        return View(new PagedList<Admission>(items, total, f.Page, f.PageSize));
    }

    public async Task<IActionResult> Create(int? petId, int? visitId, int? cageId, AdmissionKind kind = AdmissionKind.Boarding)
    {
        var a = new Admission { Kind = kind, CageId = cageId, ExpectedDischarge = DateTime.Today.AddDays(2) };
        if (visitId.HasValue)
        {
            var v = await Db.Visits.AsNoTracking().FirstOrDefaultAsync(x => x.Id == visitId);
            if (v != null) { a.VisitId = v.Id; petId = v.PetId; a.VetId = v.VetId; a.Kind = AdmissionKind.Hospitalization; a.Reason = v.Diagnosis; }
        }
        if (petId.HasValue)
        {
            var p = await Db.Pets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == petId);
            if (p != null) { a.PetId = p.Id; a.OwnerId = p.OwnerId; }
        }
        if (cageId.HasValue) a.DailyRate = await Db.Cages.Where(c => c.Id == cageId).Select(c => c.DailyRate).FirstOrDefaultAsync();
        await FormData(a);
        return View("Form", a);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var a = await Db.Admissions.FindAsync(id);
        if (a == null) return NotFound();
        if (a.Status != AdmissionStatus.Active) return BackWithError("Phiếu đã đóng.", nameof(Details), new { id });
        await FormData(a);
        return View("Form", a);
    }

    private async Task FormData(Admission a)
    {
        var l = Svc<Lookups>();
        ViewBag.Cages = await l.CagesAsync(a.CageId, a.Id == 0 ? (int?)null : a.Id);
        ViewBag.Vets = await l.VetsAsync(a.VetId, "-- Không --");
        ViewBag.Pets = await l.PetsOfOwnerAsync(a.OwnerId == 0 ? (int?)null : a.OwnerId, a.PetId == 0 ? (int?)null : a.PetId);
        ViewBag.OwnerText = a.OwnerId > 0 ? await Db.Owners.Where(o => o.Id == a.OwnerId).Select(o => o.FullName + " – " + o.Phone).FirstOrDefaultAsync() : null;
        ViewBag.CageRates = await Db.Cages.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.DailyRate);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Admission m)
    {
        foreach (var k in new[] { nameof(m.Pet), nameof(m.Owner), nameof(m.Cage), nameof(m.Vet) }) ModelState.Remove(k);
        var pet = await Db.Pets.AsNoTracking().FirstOrDefaultAsync(p => p.Id == m.PetId && p.OwnerId == m.OwnerId);
        if (pet == null) ModelState.AddModelError(nameof(m.PetId), "Chọn khách hàng và thú cưng.");
        if (m.CageId.HasValue && await Db.Admissions.AnyAsync(a => a.CageId == m.CageId && a.Status == AdmissionStatus.Active && a.Id != m.Id))
            ModelState.AddModelError(nameof(m.CageId), "Chuồng đang có thú cưng khác.");
        if (m.Id == 0 && pet != null && await Db.Admissions.AnyAsync(a => a.PetId == pet.Id && a.Status == AdmissionStatus.Active))
            ModelState.AddModelError(nameof(m.PetId), "Thú cưng đang lưu chuồng.");
        if (!ModelState.IsValid)
        {
            await FormData(m);
            return View("Form", m);
        }

        Admission a;
        if (m.Id == 0)
        {
            a = new Admission
            {
                Code = await Codes.NextAsync(Db.Admissions.Select(x => x.Code), "NT"), PetId = pet!.Id, OwnerId = pet.OwnerId, VisitId = m.VisitId,
                AdmitAt = DateTime.Now, CreatedById = CurrentUserId
            };
            Db.Admissions.Add(a);
        }
        else
        {
            a = await Db.Admissions.FirstOrDefaultAsync(x => x.Id == m.Id) ?? throw new InvalidOperationException();
            if (a.Status != AdmissionStatus.Active) return BackWithError("Phiếu đã đóng.", nameof(Details), new { id = a.Id });
        }
        a.Kind = m.Kind; a.CageId = m.CageId; a.VetId = m.VetId; a.ExpectedDischarge = m.ExpectedDischarge; a.Reason = m.Reason;
        a.CareNote = m.CareNote; a.DailyRate = m.DailyRate;
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã lưu phiếu {a.Code}.", nameof(Details), new { id = a.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var a = await Db.Admissions.AsNoTracking().Include(x => x.Pet).ThenInclude(p => p!.Species).Include(x => x.Owner).Include(x => x.Cage).Include(x => x.Vet)
            .Include(x => x.Notes).ThenInclude(n => n.User).FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound();
        ViewBag.Invoice = a.InvoiceId.HasValue ? await Db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == a.InvoiceId) : null;
        return View(a);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNote(int id, decimal? temperature, decimal? weightKg, string? appetite, string? note, string? treatment)
    {
        var a = await Db.Admissions.FindAsync(id);
        if (a == null) return NotFound();
        if (a.Status != AdmissionStatus.Active) return BackWithError("Phiếu đã đóng.", nameof(Details), new { id });
        if (temperature is < 25 or > 45) return BackWithError("Nhiệt độ 25–45 °C.", nameof(Details), new { id });
        if (string.IsNullOrWhiteSpace(note) && string.IsNullOrWhiteSpace(treatment) && temperature == null && weightKg == null)
            return BackWithError("Nhập ít nhất một thông tin theo dõi.", nameof(Details), new { id });
        Db.AdmissionNotes.Add(new AdmissionNote { AdmissionId = id, Temperature = temperature, WeightKg = weightKg, Appetite = appetite, Note = note, Treatment = treatment, UserId = CurrentUserId });
        await Db.SaveChangesAsync();
        return BackWithSuccess("Đã ghi theo dõi.", nameof(Details), new { id });
    }

    /// <summary>Xuất chuồng: chốt số ngày, lập hóa đơn nháp tiền lưu chuồng</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Discharge(int id, string? dischargeNote)
    {
        var a = await Db.Admissions.FindAsync(id);
        if (a == null) return NotFound();
        if (a.Status != AdmissionStatus.Active) return BackWithError("Phiếu đã đóng.", nameof(Details), new { id });
        a.DischargeAt = DateTime.Now;
        a.DischargeNote = dischargeNote;
        a.Status = AdmissionStatus.Discharged;
        await Db.SaveChangesAsync();
        var msg = $"Đã xuất chuồng ({a.BillableDays()} ngày).";
        if (a.DailyRate > 0 && a.InvoiceId == null)
        {
            var inv = await Svc<InvoiceService>().CreateFromAdmissionAsync(a);
            await Db.SaveChangesAsync();
            msg += $" Đã lập hóa đơn {inv.Code} chuyển thu ngân.";
        }
        return BackWithSuccess(msg, nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var a = await Db.Admissions.FindAsync(id);
        if (a == null) return NotFound();
        if (a.Status != AdmissionStatus.Active) return BackWithError("Phiếu đã đóng.", nameof(Details), new { id });
        if (await Db.AdmissionNotes.AnyAsync(n => n.AdmissionId == id))
            return BackWithError("Phiếu đã có ghi chép theo dõi – dùng \"Xuất chuồng\" thay vì hủy.", nameof(Details), new { id });
        a.Status = AdmissionStatus.Cancelled;
        a.DischargeAt = DateTime.Now;
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã hủy phiếu {a.Code}.", nameof(Index));
    }
}
