using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using PetHospital.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Controllers;

public class HomeController : AppController
{
    public HomeController(PetDbContext db) : base(db) { }

    public async Task<IActionResult> Index()
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var settings = Svc<SettingsService>();
        var vm = new DashboardViewModel
        {
            AppointmentsToday = await Db.Appointments.CountAsync(a => a.StartAt >= today && a.StartAt < tomorrow && a.Status != AppointmentStatus.Cancelled),
            Waiting = await Db.Visits.CountAsync(v => v.Status == VisitStatus.Waiting),
            InProgress = await Db.Visits.CountAsync(v => v.Status == VisitStatus.InProgress),
            CompletedToday = await Db.Visits.CountAsync(v => v.Status == VisitStatus.Completed && v.CompletedAt >= today),
            Admitted = await Db.Admissions.CountAsync(a => a.Status == AdmissionStatus.Active)
        };

        vm.Upcoming = await Db.Appointments.AsNoTracking().Include(a => a.Owner).Include(a => a.Pet).Include(a => a.Vet).Include(a => a.ServiceItem)
            .Where(a => a.StartAt >= today && a.StartAt < tomorrow && (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Confirmed))
            .OrderBy(a => a.StartAt).Take(10).ToListAsync();
        vm.Queue = await Db.Visits.AsNoTracking().Include(v => v.Pet).ThenInclude(p => p!.Species).Include(v => v.Owner).Include(v => v.Vet)
            .Where(v => v.Status == VisitStatus.Waiting || v.Status == VisitStatus.InProgress).OrderBy(v => v.VisitAt).Take(15).ToListAsync();

        var remind = await settings.GetIntAsync(SettingsService.VaccineRemindDays, 7);
        var until = today.AddDays(remind);
        vm.VaccineDue = await Db.Vaccinations.AsNoTracking().Include(v => v.Pet).ThenInclude(p => p!.Owner)
            .Where(v => !v.ReminderDone && v.NextDueDate != null && v.NextDueDate <= until && !v.Pet!.IsDeceased)
            .OrderBy(v => v.NextDueDate).Take(10).ToListAsync();

        if (Me.HasAny(Perm.StockView, Perm.StockManage))
        {
            vm.LowStock = await Db.Products.AsNoTracking().Include(p => p.Stocks).Where(p => p.IsActive && p.Stocks.Sum(s => s.Quantity) <= p.MinStock)
                .OrderBy(p => p.Stocks.Sum(s => s.Quantity)).Take(10).ToListAsync();
            var warn = today.AddDays(await settings.GetIntAsync(SettingsService.ExpiryWarnDays, 60));
            vm.Expiring = await Db.ProductBatches.AsNoTracking().Include(b => b.Product)
                .Where(b => b.Quantity > 0 && b.ExpiryDate != null && b.ExpiryDate <= warn).OrderBy(b => b.ExpiryDate).Take(10).ToListAsync();
        }

        if (Me.HasAny(Perm.Reports, Perm.InvoiceManage))
        {
            vm.RevenueToday = await Db.Payments.Where(p => p.At >= today).SumAsync(p => (decimal?)p.Amount) ?? 0;
            vm.RevenueMonth = await Db.Payments.Where(p => p.At >= monthStart).SumAsync(p => (decimal?)p.Amount) ?? 0;
            var open = Db.Invoices.Where(i => i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Partial);
            vm.Receivable = await open.SumAsync(i => (decimal?)(i.Total - i.Paid)) ?? 0;
            vm.UnpaidInvoices = await open.CountAsync();

            var from = today.AddDays(-29);
            var daily = await Db.Payments.Where(p => p.At >= from)
                .GroupBy(p => p.At.Date).Select(g => new { Day = g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync();
            for (var d = from; d <= today; d = d.AddDays(1))
                vm.Revenue30.Add((d.ToString("dd/MM"), daily.FirstOrDefault(x => x.Day == d)?.Sum ?? 0));

            var issued = Db.InvoiceLines.Where(l => l.Invoice!.IssuedAt >= monthStart && l.Invoice.Status != InvoiceStatus.Cancelled && l.Invoice.Status != InvoiceStatus.Draft);
            var bySvc = await issued.Where(l => l.ServiceItemId != null).GroupBy(l => l.ServiceItem!.Kind)
                .Select(g => new { Kind = g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync();
            foreach (var x in bySvc.OrderByDescending(x => x.Sum)) vm.RevenueByKind.Add((x.Kind.GetDisplayName(), x.Sum));
            var prod = await issued.Where(l => l.ProductId != null).SumAsync(l => (decimal?)l.Amount) ?? 0;
            if (prod > 0) vm.RevenueByKind.Add(("Thuốc / hàng hóa", prod));
            var other = await issued.Where(l => l.ServiceItemId == null && l.ProductId == null).SumAsync(l => (decimal?)l.Amount) ?? 0;
            if (other > 0) vm.RevenueByKind.Add(("Lưu chuồng / khác", other));
        }
        return View(vm);
    }

    /// <summary>Tìm nhanh: khách hàng theo tên / điện thoại, thú cưng theo tên / mã / vi mạch</summary>
    public async Task<IActionResult> Search(string? q)
    {
        q = q?.Trim();
        if (string.IsNullOrEmpty(q)) return RedirectToAction(nameof(Index));
        var pet = await Db.Pets.AsNoTracking().FirstOrDefaultAsync(p => p.Code == q || p.Microchip == q);
        if (pet != null) return RedirectToAction("Details", "Pets", new { id = pet.Id });
        var owners = await Db.Owners.AsNoTracking().Where(o => o.Phone == q || o.Code == q).Select(o => o.Id).ToListAsync();
        if (owners.Count == 1) return RedirectToAction("Details", "Owners", new { id = owners[0] });
        return RedirectToAction("Index", "Owners", new { q });
    }

    [AllowAnonymous]
    public IActionResult Error() => View();
}
