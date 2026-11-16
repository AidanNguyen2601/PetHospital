using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Controllers;

[RequirePerm(Perm.Appointment, Perm.VisitExam)]
public class AppointmentsController : AppController
{
    public AppointmentsController(PetDbContext db) : base(db) { }

    /// <summary>Lịch hẹn theo ngày (dạng danh sách + lưới theo bác sĩ) hoặc theo tuần</summary>
    public async Task<IActionResult> Index(DateTime? date, string view = "day", int? vetId = null)
    {
        var day = (date ?? DateTime.Today).Date;
        DateTime from, to;
        if (view == "week")
        {
            var offset = ((int)day.DayOfWeek + 6) % 7; // thứ Hai đầu tuần
            from = day.AddDays(-offset); to = from.AddDays(7);
        }
        else { from = day; to = day.AddDays(1); view = "day"; }

        var q = Db.Appointments.AsNoTracking().Include(a => a.Owner).Include(a => a.Pet).ThenInclude(p => p!.Species)
            .Include(a => a.Vet).Include(a => a.ServiceItem).Where(a => a.StartAt >= from && a.StartAt < to);
        if (vetId.HasValue) q = q.Where(a => a.VetId == vetId);
        ViewBag.Items = await q.OrderBy(a => a.StartAt).ToListAsync();
        ViewBag.Date = day; ViewBag.From = from; ViewBag.To = to; ViewBag.View = view; ViewBag.VetId = vetId;
        ViewBag.Vets = await Svc<Lookups>().VetsAsync(vetId, "-- Tất cả bác sĩ --");
        return View();
    }

    public async Task<IActionResult> Create(int? ownerId, int? petId, DateTime? at)
    {
        var a = new Appointment { OwnerId = ownerId, PetId = petId };
        if (petId.HasValue && ownerId == null) a.OwnerId = await Db.Pets.Where(p => p.Id == petId).Select(p => (int?)p.OwnerId).FirstOrDefaultAsync();
        if (at.HasValue) a.StartAt = at.Value;
        else
        {
            var next = DateTime.Now.AddMinutes(30);
            a.StartAt = next.Date.AddHours(next.Hour).AddMinutes(next.Minute < 30 ? 30 : 60);
        }
        await FormData(a);
        return View("Form", a);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var a = await Db.Appointments.FindAsync(id);
        if (a == null) return NotFound();
        if (a.Status is AppointmentStatus.Completed or AppointmentStatus.CheckedIn)
            return BackWithError("Lịch hẹn đã tiếp nhận / hoàn thành, không sửa được.", nameof(Index), new { date = a.StartAt.Date });
        await FormData(a);
        return View("Form", a);
    }

    private async Task FormData(Appointment a)
    {
        var l = Svc<Lookups>();
        ViewBag.Vets = await l.VetsAsync(a.VetId, "-- Chưa chỉ định --");
        ViewBag.Pets = await l.PetsOfOwnerAsync(a.OwnerId, a.PetId);
        ViewBag.Services = await l.ServicesAsync();
        ViewBag.OwnerText = a.OwnerId.HasValue
            ? await Db.Owners.Where(o => o.Id == a.OwnerId).Select(o => o.FullName + " – " + o.Phone).FirstOrDefaultAsync()
            : null;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Appointment m)
    {
        ModelState.Remove(nameof(m.Owner)); ModelState.Remove(nameof(m.Pet)); ModelState.Remove(nameof(m.Vet)); ModelState.Remove(nameof(m.ServiceItem));
        if (m.PetId.HasValue && !await Db.Pets.AnyAsync(p => p.Id == m.PetId && p.OwnerId == m.OwnerId))
            ModelState.AddModelError(nameof(m.PetId), "Thú cưng không thuộc khách hàng đã chọn.");
        if (m.Id == 0 && m.StartAt < DateTime.Now.AddMinutes(-30))
            ModelState.AddModelError(nameof(m.StartAt), "Thời gian hẹn đã qua.");
        // Cảnh báo trùng lịch bác sĩ
        if (m.VetId.HasValue && ModelState.IsValid)
        {
            var end = m.StartAt.AddMinutes(m.DurationMinutes);
            // Bác sĩ có thể làm ở nhiều chi nhánh: kiểm tra trùng lịch toàn chuỗi
            var clash = await Db.Appointments.IgnoreQueryFilters().Include(a => a.Pet).Where(a => a.Id != m.Id && a.VetId == m.VetId
                    && (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Confirmed)
                    && a.StartAt < end && a.StartAt.AddMinutes(a.DurationMinutes) > m.StartAt)
                .Select(a => new { a.Code, a.StartAt, Pet = a.Pet != null ? a.Pet.Name : null }).FirstOrDefaultAsync();
            if (clash != null && Request.Form["force"] != "1")
            {
                ModelState.AddModelError("", $"Bác sĩ đã có lịch hẹn trùng giờ: {clash.Code} lúc {clash.StartAt:HH:mm} {clash.Pet}. Bấm \"Lưu\" lần nữa để vẫn đặt lịch.");
                ViewBag.Force = true;
            }
        }
        if (!ModelState.IsValid)
        {
            await FormData(m);
            return View("Form", m);
        }

        Appointment a;
        if (m.Id == 0)
        {
            a = new Appointment { Code = await Codes.NextAsync(Db, Db.Appointments.Select(x => x.Code), "LH", m.StartAt), CreatedById = CurrentUserId };
            Db.Appointments.Add(a);
        }
        else
        {
            a = await Db.Appointments.FirstOrDefaultAsync(x => x.Id == m.Id) ?? throw new InvalidOperationException();
            if (a.Status is AppointmentStatus.Completed or AppointmentStatus.CheckedIn) return BackWithError("Lịch hẹn đã tiếp nhận.", nameof(Index));
        }
        a.OwnerId = m.OwnerId; a.PetId = m.PetId; a.VetId = m.VetId; a.ServiceItemId = m.ServiceItemId; a.StartAt = m.StartAt;
        a.DurationMinutes = m.DurationMinutes; a.Reason = m.Reason; a.Note = m.Note;
        if (a.Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow) a.Status = AppointmentStatus.Booked;
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã lưu lịch hẹn {a.Code} lúc {a.StartAt:HH:mm dd/MM/yyyy}.", nameof(Index), new { date = a.StartAt.Date.ToString("yyyy-MM-dd") });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, AppointmentStatus status)
    {
        var a = await Db.Appointments.FindAsync(id);
        if (a == null) return NotFound();
        if (a.Status is AppointmentStatus.Completed or AppointmentStatus.CheckedIn)
            return BackWithError("Lịch hẹn đã tiếp nhận, không đổi trạng thái được.", nameof(Index), new { date = a.StartAt.Date.ToString("yyyy-MM-dd") });
        if (status is not (AppointmentStatus.Confirmed or AppointmentStatus.Cancelled or AppointmentStatus.NoShow or AppointmentStatus.Booked))
            return BadRequest();
        a.Status = status;
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Lịch hẹn {a.Code}: {status.GetDisplayName()}.", nameof(Index), new { date = a.StartAt.Date.ToString("yyyy-MM-dd") });
    }

    /// <summary>Khách đến: tạo phiếu khám (chờ khám) từ lịch hẹn</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckIn(int id)
    {
        var a = await Db.Appointments.Include(x => x.ServiceItem).FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound();
        if (a.Status is not (AppointmentStatus.Booked or AppointmentStatus.Confirmed))
            return BackWithError("Lịch hẹn không ở trạng thái chờ tiếp nhận.", nameof(Index));
        if (a.PetId == null) return BackWithError("Lịch hẹn chưa chọn thú cưng – sửa lịch hẹn để chọn trước khi tiếp nhận.", nameof(Edit), new { id });
        var pet = await Db.Pets.AsNoTracking().FirstAsync(p => p.Id == a.PetId);
        var v = new Visit
        {
            Code = await Codes.NextAsync(Db, Db.Visits.Select(x => x.Code), "PK"), PetId = pet.Id, OwnerId = pet.OwnerId, VetId = a.VetId, AppointmentId = a.Id,
            VisitAt = DateTime.Now, Status = VisitStatus.Waiting, Reason = a.Reason, WeightKg = pet.WeightKg, CreatedById = CurrentUserId
        };
        if (a.ServiceItem != null) v.Services.Add(new VisitService { ServiceItemId = a.ServiceItem.Id, Quantity = 1, UnitPrice = a.ServiceItem.Price });
        Db.Visits.Add(v);
        a.Status = AppointmentStatus.CheckedIn;
        await Db.SaveChangesAsync();
        a.VisitId = v.Id;
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã tiếp nhận {pet.Name} – phiếu khám {v.Code} vào hàng chờ.", "Index", new { controller = "Visits" });
    }
}
