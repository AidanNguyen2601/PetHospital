using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using PetHospital.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Controllers;

[RequirePerm(Perm.VisitView, Perm.VisitExam)]
public class VisitsController : AppController
{
    public VisitsController(PetDbContext db) : base(db) { }

    /// <summary>Hàng chờ khám + tra cứu phiếu khám</summary>
    public async Task<IActionResult> Index(ListFilter f, string tab = "queue", DateTime? from = null, DateTime? to = null, int? vetId = null)
    {
        var q = Db.Visits.AsNoTracking().Include(v => v.Pet).ThenInclude(p => p!.Species).Include(v => v.Owner).Include(v => v.Vet).AsQueryable();
        if (tab == "queue") q = q.Where(v => v.Status == VisitStatus.Waiting || v.Status == VisitStatus.InProgress);
        else
        {
            tab = "all";
            from ??= DateTime.Today.AddDays(-30);
            q = q.Where(v => v.VisitAt >= from);
            if (to.HasValue) { var end = to.Value.Date.AddDays(1); q = q.Where(v => v.VisitAt < end); }
        }
        if (vetId.HasValue) q = q.Where(v => v.VetId == vetId);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var k = f.Q.Trim();
            q = q.Where(v => v.Code.Contains(k) || v.Pet!.Name.Contains(k) || v.Owner!.FullName.Contains(k) || v.Owner.Phone.Contains(k)
                || (v.Diagnosis != null && v.Diagnosis.Contains(k)));
        }
        q = tab == "queue" ? q.OrderBy(v => v.Status == VisitStatus.InProgress ? 0 : 1).ThenBy(v => v.VisitAt) : q.OrderByDescending(v => v.VisitAt);
        var total = await q.CountAsync();
        var items = await q.Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync();
        ViewBag.Tab = tab; ViewBag.From = from; ViewBag.To = to; ViewBag.VetId = vetId; ViewBag.Filter = f;
        ViewBag.Vets = await Svc<Lookups>().VetsAsync(vetId, "-- Tất cả bác sĩ --");
        ViewBag.QueueCount = await Db.Visits.CountAsync(v => v.Status == VisitStatus.Waiting || v.Status == VisitStatus.InProgress);
        return View(new PagedList<Visit>(items, total, f.Page, f.PageSize));
    }

    /// <summary>Tiếp nhận khách vãng lai (không đặt lịch)</summary>
    [RequirePerm(Perm.Appointment, Perm.VisitExam)]
    public async Task<IActionResult> Create(int? ownerId, int? petId)
    {
        var v = new Visit();
        if (petId.HasValue)
        {
            var pet = await Db.Pets.AsNoTracking().FirstOrDefaultAsync(p => p.Id == petId);
            if (pet != null) { v.PetId = pet.Id; ownerId = pet.OwnerId; v.WeightKg = pet.WeightKg; }
        }
        if (ownerId.HasValue) v.OwnerId = ownerId.Value;
        await CreateData(ownerId, petId);
        if (Me.IsVet) v.VetId = CurrentUserId;
        return View(v);
    }

    private async Task CreateData(int? ownerId, int? petId)
    {
        var l = Svc<Lookups>();
        ViewBag.Vets = await l.VetsAsync(null, "-- Chưa chỉ định --");
        ViewBag.Pets = await l.PetsOfOwnerAsync(ownerId, petId);
        ViewBag.Services = await l.ServicesAsync();
        ViewBag.OwnerText = ownerId.HasValue ? await Db.Owners.Where(o => o.Id == ownerId).Select(o => o.FullName + " – " + o.Phone).FirstOrDefaultAsync() : null;
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.Appointment, Perm.VisitExam)]
    public async Task<IActionResult> Create(Visit m, int? serviceItemId)
    {
        var pet = await Db.Pets.AsNoTracking().FirstOrDefaultAsync(p => p.Id == m.PetId && p.OwnerId == m.OwnerId);
        if (pet == null)
        {
            ModelState.AddModelError(nameof(m.PetId), "Chọn khách hàng và thú cưng.");
            await CreateData(m.OwnerId == 0 ? (int?)null : m.OwnerId, null);
            return View(m);
        }
        var v = new Visit
        {
            Code = await Codes.NextAsync(Db, Db.Visits.Select(x => x.Code), "PK"), PetId = pet.Id, OwnerId = pet.OwnerId, VetId = m.VetId, VisitAt = DateTime.Now,
            Status = VisitStatus.Waiting, Reason = m.Reason, WeightKg = m.WeightKg ?? pet.WeightKg, CreatedById = CurrentUserId
        };
        if (serviceItemId.HasValue)
        {
            var s = await Db.ServiceItems.FindAsync(serviceItemId.Value);
            if (s != null) v.Services.Add(new VisitService { ServiceItemId = s.Id, Quantity = 1, UnitPrice = s.Price });
        }
        Db.Visits.Add(v);
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã tiếp nhận {pet.Name} – phiếu khám {v.Code}.", nameof(Index));
    }

    /// <summary>Bác sĩ nhận ca: chuyển sang "Đang khám"</summary>
    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.VisitExam)]
    public async Task<IActionResult> Start(int id)
    {
        var v = await Db.Visits.FindAsync(id);
        if (v == null) return NotFound();
        if (v.Status == VisitStatus.Waiting)
        {
            v.Status = VisitStatus.InProgress;
            v.StartedAt = DateTime.Now;
            if (v.VetId == null && Me.IsVet) v.VetId = CurrentUserId;
            await Db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Exam), new { id });
    }

    [RequirePerm(Perm.VisitExam)]
    public async Task<IActionResult> Exam(int id)
    {
        var v = await LoadAsync(id);
        if (v == null) return NotFound();
        if (v.Status is VisitStatus.Completed or VisitStatus.Cancelled) return RedirectToAction(nameof(Details), new { id });
        var vm = new VisitFormModel
        {
            Visit = v,
            Services = v.Services.Select(s => new LineInput { ItemId = s.ServiceItemId, Quantity = s.Quantity, UnitPrice = s.UnitPrice, Note = s.Note }).ToList(),
            Medicines = v.Medicines.Select(m => new LineInput { ItemId = m.ProductId, Quantity = m.Quantity, UnitPrice = m.UnitPrice, Note = m.Dosage, Days = m.Days }).ToList()
        };
        await ExamData(v);
        return View(vm);
    }

    /// <summary>Phiếu khám của chi nhánh đang làm việc; anyBranch: đọc phiếu của chi nhánh khác (khi dùng chung hồ sơ khách hàng, chỉ xem)</summary>
    private async Task<Visit?> LoadAsync(int id, bool anyBranch = false)
    {
        var q = Db.Visits.AsQueryable();
        if (anyBranch) q = q.IgnoreQueryFilters().AsNoTracking();
        return await q.Include(v => v.Pet).ThenInclude(p => p!.Species).Include(v => v.Pet).ThenInclude(p => p!.Breed)
            .Include(v => v.Owner).Include(v => v.Vet)
            .Include(v => v.Services).ThenInclude(s => s.ServiceItem).Include(v => v.Medicines).ThenInclude(m => m.Product)
            .AsSplitQuery().FirstOrDefaultAsync(v => v.Id == id);
    }

    /// <summary>Phiếu khám chi nhánh khác: chỉ xem (hồ sơ bệnh án dùng chung)</summary>
    private async Task<Visit?> LoadForViewAsync(int id)
    {
        var v = await LoadAsync(id);
        if (v != null || !ChainSettings.ShareCustomers) return v;
        v = await LoadAsync(id, anyBranch: true);
        if (v != null) ViewBag.OtherBranch = (await Svc<Lookups>().BranchNamesAsync()).GetValueOrDefault(v.BranchId);
        return v;
    }

    private async Task ExamData(Visit v)
    {
        var l = Svc<Lookups>();
        ViewBag.Vets = await l.VetsAsync(v.VetId, "-- Chọn bác sĩ --");
        ViewBag.ServiceList = await l.ServicesAsync();
        ViewBag.ProductList = (await l.ProductsAsync()).Where(p => p.Kind != ProductKind.Vaccine).ToList();
        ViewBag.VaccineList = await l.ProductsAsync(ProductKind.Vaccine);
        ViewBag.Vaccinations = await Db.Vaccinations.AsNoTracking().Include(x => x.Product).Where(x => x.VisitId == v.Id).ToListAsync();
        // Tiền sử của thú cưng ở mọi chi nhánh
        ViewBag.History = await Db.Visits.IgnoreQueryFilters().AsNoTracking().Include(x => x.Vet)
            .Where(x => x.PetId == v.PetId && x.Id != v.Id && x.Status == VisitStatus.Completed).OrderByDescending(x => x.VisitAt).Take(8).ToListAsync();
        ViewBag.PetVaccines = await Db.Vaccinations.IgnoreQueryFilters().AsNoTracking().Where(x => x.PetId == v.PetId && x.VisitId != v.Id).OrderByDescending(x => x.Date).Take(8).ToListAsync();
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.VisitExam)]
    public async Task<IActionResult> Exam(int id, VisitFormModel vm, string? command, bool createFollowUp = false)
    {
        var v = await LoadAsync(id);
        if (v == null) return NotFound();
        if (v.Status is VisitStatus.Completed or VisitStatus.Cancelled) return BackWithError("Phiếu khám đã đóng.", nameof(Details), new { id });

        var m = vm.Visit;
        foreach (var key in ModelState.Keys.Where(k => !k.StartsWith("Visit.") || k.StartsWith("Visit.Pet") || k.StartsWith("Visit.Owner") || k.StartsWith("Visit.Vet.")).ToList())
            ModelState.Remove(key);
        var complete = command == "complete";
        if (complete && string.IsNullOrWhiteSpace(m.Diagnosis)) ModelState.AddModelError("Visit.Diagnosis", "Nhập chẩn đoán trước khi kết thúc khám.");
        if (complete && m.VetId == null) ModelState.AddModelError("Visit.VetId", "Chọn bác sĩ khám.");
        if (m.FollowUpDate.HasValue && m.FollowUpDate < DateTime.Today) ModelState.AddModelError("Visit.FollowUpDate", "Ngày tái khám phải từ hôm nay.");

        // Dịch vụ & thuốc
        var serviceIds = vm.Services.Where(s => s.ItemId > 0).Select(s => s.ItemId!.Value).Distinct().ToList();
        var services = await Db.ServiceItems.Where(s => serviceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        var productIds = vm.Medicines.Where(s => s.ItemId > 0).Select(s => s.ItemId!.Value).Distinct().ToList();
        var products = await Db.Products.Include(p => p.Stocks).Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        foreach (var line in vm.Medicines.Where(x => x.ItemId > 0))
        {
            if (line.Quantity <= 0) ModelState.AddModelError("", "Số lượng thuốc phải lớn hơn 0.");
            else if (products.TryGetValue(line.ItemId!.Value, out var p) && p.Stock < line.Quantity)
                ModelState.AddModelError("", $"{p.Name}: tồn kho chỉ còn {p.Stock.ToQty()} {p.Unit}.");
        }

        if (!ModelState.IsValid)
        {
            vm.Visit = v;
            CopyFields(m, v);
            await ExamData(v);
            return View(vm);
        }

        CopyFields(m, v);
        v.Services.Clear();
        foreach (var s in vm.Services.Where(s => s.ItemId > 0 && services.ContainsKey(s.ItemId!.Value)))
            v.Services.Add(new VisitService { ServiceItemId = s.ItemId!.Value, Quantity = s.Quantity > 0 ? s.Quantity : 1, UnitPrice = s.UnitPrice ?? services[s.ItemId.Value].Price, Note = s.Note });
        v.Medicines.Clear();
        foreach (var x in vm.Medicines.Where(x => x.ItemId > 0 && products.ContainsKey(x.ItemId!.Value)))
            v.Medicines.Add(new VisitMedicine { ProductId = x.ItemId!.Value, Quantity = x.Quantity, UnitPrice = x.UnitPrice ?? products[x.ItemId.Value].SalePrice, Dosage = x.Note, Days = x.Days });
        if (v.Status == VisitStatus.Waiting) { v.Status = VisitStatus.InProgress; v.StartedAt ??= DateTime.Now; }

        string msg = $"Đã lưu phiếu khám {v.Code}.";
        if (complete)
        {
            v.Status = VisitStatus.Completed;
            v.CompletedAt = DateTime.Now;
            var pet = await Db.Pets.FirstAsync(p => p.Id == v.PetId);
            if (v.WeightKg.HasValue) pet.WeightKg = v.WeightKg;
            if (v.AppointmentId.HasValue)
            {
                var appt = await Db.Appointments.FindAsync(v.AppointmentId.Value);
                if (appt != null) appt.Status = AppointmentStatus.Completed;
            }
            if (createFollowUp && v.FollowUpDate.HasValue)
            {
                var at = v.FollowUpDate.Value.Date.AddHours(9);
                Db.Appointments.Add(new Appointment
                {
                    Code = await Codes.NextAsync(Db, Db.Appointments.Select(x => x.Code), "LH", at), OwnerId = v.OwnerId, PetId = v.PetId, VetId = v.VetId,
                    StartAt = at, DurationMinutes = 15, Reason = $"Tái khám ({v.Diagnosis})".Truncate(500), Status = AppointmentStatus.Booked,
                    ServiceItemId = await Db.ServiceItems.Where(s => s.Code == "KB02").Select(s => (int?)s.Id).FirstOrDefaultAsync(), CreatedById = CurrentUserId
                });
            }
            await Db.SaveChangesAsync();
            var hasCharge = v.Services.Count > 0 || v.Medicines.Count > 0 || await Db.Vaccinations.AnyAsync(x => x.VisitId == v.Id && x.ProductId != null);
            if (hasCharge && v.InvoiceId == null)
            {
                var inv = await Svc<InvoiceService>().CreateFromVisitAsync(v);
                await Db.SaveChangesAsync();
                msg = $"Đã kết thúc khám {v.Code} – lập hóa đơn {inv.Code} chuyển thu ngân.";
            }
            else msg = $"Đã kết thúc khám {v.Code}.";
            return BackWithSuccess(msg, nameof(Details), new { id });
        }
        await Db.SaveChangesAsync();
        return BackWithSuccess(msg, nameof(Exam), new { id });
    }

    private static void CopyFields(Visit m, Visit v)
    {
        v.VetId = m.VetId; v.Reason = m.Reason; v.Symptoms = m.Symptoms; v.WeightKg = m.WeightKg; v.Temperature = m.Temperature;
        v.HeartRate = m.HeartRate; v.RespiratoryRate = m.RespiratoryRate; v.Examination = m.Examination; v.Diagnosis = m.Diagnosis;
        v.Treatment = m.Treatment; v.Advice = m.Advice; v.FollowUpDate = m.FollowUpDate;
    }

    /// <summary>Ghi nhận mũi tiêm trong lượt khám (sổ tiêm + tính tiền vắc-xin khi lập hóa đơn)</summary>
    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.VisitExam, Perm.Vaccine)]
    public async Task<IActionResult> AddVaccination(int id, int? productId, string? vaccineName, int doseNumber, DateTime? nextDueDate, string? lotNo)
    {
        var v = await Db.Visits.FindAsync(id);
        if (v == null) return NotFound();
        if (v.Status is VisitStatus.Completed or VisitStatus.Cancelled) return BackWithError("Phiếu khám đã đóng.", nameof(Details), new { id });
        Product? p = productId.HasValue ? await Db.Products.Include(x => x.Stocks).FirstOrDefaultAsync(x => x.Id == productId.Value) : null;
        var name = p?.Name ?? vaccineName?.Trim();
        if (string.IsNullOrEmpty(name)) return BackWithError("Chọn vắc-xin hoặc nhập tên vắc-xin.", nameof(Exam), new { id });
        if (p != null && p.Stock < 1) return BackWithError($"{p.Name} đã hết hàng trong kho.", nameof(Exam), new { id });
        var next = nextDueDate ?? (p?.BoosterDays is > 0 ? DateTime.Today.AddDays(p.BoosterDays.Value) : null);
        // Mũi trước của cùng loại vắc-xin coi như đã nhắc
        foreach (var old in await Db.Vaccinations.IgnoreQueryFilters().Where(x => x.PetId == v.PetId && !x.ReminderDone && (x.ProductId == productId && productId != null || x.VaccineName == name)).ToListAsync())
            old.ReminderDone = true;
        Db.Vaccinations.Add(new Vaccination
        {
            PetId = v.PetId, ProductId = p?.Id, VaccineName = name, DoseNumber = Math.Max(1, doseNumber), Date = DateTime.Today, NextDueDate = next,
            LotNo = lotNo, VetId = v.VetId ?? (Me.IsVet ? (int?)CurrentUserId : null), VisitId = v.Id
        });
        if (v.Status == VisitStatus.Waiting) { v.Status = VisitStatus.InProgress; v.StartedAt = DateTime.Now; }
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã ghi nhận tiêm {name} (mũi {doseNumber}).", nameof(Exam), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.VisitExam, Perm.Vaccine)]
    public async Task<IActionResult> RemoveVaccination(int id, int vaccinationId)
    {
        var x = await Db.Vaccinations.Include(y => y.Visit).FirstOrDefaultAsync(y => y.Id == vaccinationId && y.VisitId == id);
        if (x == null) return NotFound();
        if (x.Visit!.Status is VisitStatus.Completed or VisitStatus.Cancelled) return BackWithError("Phiếu khám đã đóng.", nameof(Details), new { id });
        Db.Vaccinations.Remove(x);
        await Db.SaveChangesAsync();
        return BackWithSuccess("Đã xóa mũi tiêm.", nameof(Exam), new { id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var v = await LoadForViewAsync(id);
        if (v == null) return NotFound();
        ViewBag.Vaccinations = await Db.Vaccinations.IgnoreQueryFilters().AsNoTracking().Include(x => x.Product).Where(x => x.VisitId == id).ToListAsync();
        ViewBag.Invoice = v.InvoiceId.HasValue ? await Db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == v.InvoiceId) : null;
        ViewBag.Admission = await Db.Admissions.AsNoTracking().Include(a => a.Cage).FirstOrDefaultAsync(a => a.VisitId == id);
        return View(v);
    }

    /// <summary>In đơn thuốc / phiếu khám</summary>
    public async Task<IActionResult> Print(int id)
    {
        var v = await LoadForViewAsync(id);
        if (v == null) return NotFound();
        ViewBag.Vaccinations = await Db.Vaccinations.IgnoreQueryFilters().AsNoTracking().Where(x => x.VisitId == id).ToListAsync();
        ViewBag.Settings = Svc<SettingsService>();
        ViewData["Layout"] = "_PrintLayout";
        return View(v);
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.Appointment, Perm.VisitExam)]
    public async Task<IActionResult> Cancel(int id)
    {
        var v = await Db.Visits.FindAsync(id);
        if (v == null) return NotFound();
        if (v.Status is VisitStatus.Completed or VisitStatus.Cancelled) return BackWithError("Phiếu khám đã đóng.", nameof(Details), new { id });
        if (await Db.Vaccinations.AnyAsync(x => x.VisitId == id)) return BackWithError("Phiếu khám đã ghi nhận mũi tiêm – xóa mũi tiêm trước khi hủy.", nameof(Exam), new { id });
        v.Status = VisitStatus.Cancelled;
        if (v.AppointmentId.HasValue)
        {
            var a = await Db.Appointments.FindAsync(v.AppointmentId.Value);
            if (a != null && a.Status == AppointmentStatus.CheckedIn) a.Status = AppointmentStatus.Cancelled;
        }
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã hủy phiếu khám {v.Code}.", nameof(Index));
    }

    /// <summary>Mở lại phiếu khám đã kết thúc (khi hóa đơn còn nháp) để sửa</summary>
    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.VisitExam)]
    public async Task<IActionResult> Reopen(int id)
    {
        var v = await Db.Visits.FindAsync(id);
        if (v == null) return NotFound();
        if (v.Status != VisitStatus.Completed) return RedirectToAction(nameof(Exam), new { id });
        if (v.InvoiceId.HasValue)
        {
            var inv = await Db.Invoices.FindAsync(v.InvoiceId.Value);
            if (inv != null && inv.Status != InvoiceStatus.Draft && inv.Status != InvoiceStatus.Cancelled)
                return BackWithError($"Hóa đơn {inv.Code} đã phát hành – hủy hóa đơn trước khi sửa phiếu khám.", nameof(Details), new { id });
            if (inv != null && inv.Status == InvoiceStatus.Draft) Db.Invoices.Remove(inv);
            v.InvoiceId = null;
        }
        v.Status = VisitStatus.InProgress;
        v.CompletedAt = null;
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã mở lại phiếu khám {v.Code}.", nameof(Exam), new { id });
    }
}
