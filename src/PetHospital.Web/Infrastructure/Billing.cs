using PetHospital.Web.Data;
using PetHospital.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Infrastructure;

/// <summary>Hóa đơn: lập từ phiếu khám / nội trú, phát hành (xuất kho), thu tiền, hủy</summary>
public class InvoiceService
{
    private readonly PetDbContext _db;
    private readonly StockService _stock;
    private readonly CurrentUser _me;
    public InvoiceService(PetDbContext db, StockService stock, CurrentUser me) { _db = db; _stock = stock; _me = me; }

    public Task<string> NextCodeAsync() => Codes.NextAsync(_db, _db.Invoices.Select(i => i.Code), "HD");

    /// <summary>Lập hóa đơn nháp từ phiếu khám: dịch vụ + thuốc kê đơn + vắc-xin đã tiêm</summary>
    public async Task<Invoice> CreateFromVisitAsync(Visit v)
    {
        var inv = new Invoice
        {
            Code = await NextCodeAsync(), OwnerId = v.OwnerId, PetId = v.PetId, VisitId = v.Id, Date = DateTime.Now,
            CreatedById = _me.Id, Note = $"Phiếu khám {v.Code}"
        };
        var services = await _db.VisitServices.Include(s => s.ServiceItem).Where(s => s.VisitId == v.Id).ToListAsync();
        foreach (var s in services)
            inv.Lines.Add(new InvoiceLine
            {
                Kind = InvoiceLineKind.Service, ServiceItemId = s.ServiceItemId, Description = s.ServiceItem?.Name ?? "Dịch vụ",
                Unit = s.ServiceItem?.Unit, Quantity = s.Quantity, UnitPrice = s.UnitPrice, VetId = v.VetId
            });
        var meds = await _db.VisitMedicines.Include(m => m.Product).Where(m => m.VisitId == v.Id).ToListAsync();
        foreach (var m in meds)
            inv.Lines.Add(new InvoiceLine
            {
                Kind = InvoiceLineKind.Product, ProductId = m.ProductId, Description = m.Product?.Name ?? "Thuốc",
                Unit = m.Product?.Unit, Quantity = m.Quantity, UnitPrice = m.UnitPrice, VetId = v.VetId
            });
        var vaccines = await _db.Vaccinations.Include(x => x.Product).Where(x => x.VisitId == v.Id && x.ProductId != null).ToListAsync();
        foreach (var x in vaccines)
            inv.Lines.Add(new InvoiceLine
            {
                Kind = InvoiceLineKind.Product, ProductId = x.ProductId, Description = $"{x.Product!.Name} (mũi {x.DoseNumber})",
                Unit = x.Product.Unit, Quantity = 1, UnitPrice = x.Product.SalePrice, VetId = v.VetId
            });
        inv.Recalc();
        _db.Invoices.Add(inv);
        await _db.SaveChangesAsync();
        v.InvoiceId = inv.Id;
        return inv;
    }

    /// <summary>Lập hóa đơn nháp tiền lưu chuồng / nội trú theo số ngày</summary>
    public async Task<Invoice> CreateFromAdmissionAsync(Admission a)
    {
        var cage = a.CageId.HasValue ? await _db.Cages.AsNoTracking().FirstOrDefaultAsync(c => c.Id == a.CageId) : null;
        var days = a.BillableDays();
        var inv = new Invoice
        {
            Code = await NextCodeAsync(), OwnerId = a.OwnerId, PetId = a.PetId, AdmissionId = a.Id, Date = DateTime.Now,
            CreatedById = _me.Id, Note = $"Phiếu {a.Kind.GetDisplayName().ToLower()} {a.Code}"
        };
        inv.Lines.Add(new InvoiceLine
        {
            Kind = InvoiceLineKind.Other, Description = $"{a.Kind.GetDisplayName()} – chuồng {cage?.Code} ({a.AdmitAt:dd/MM HH:mm} → {(a.DischargeAt ?? DateTime.Now):dd/MM HH:mm})",
            Unit = "Ngày", Quantity = days, UnitPrice = a.DailyRate, VetId = a.VetId
        });
        inv.Recalc();
        _db.Invoices.Add(inv);
        await _db.SaveChangesAsync();
        a.InvoiceId = inv.Id;
        return inv;
    }

    /// <summary>Phát hành hóa đơn: kiểm tra tồn, xuất kho, khóa nội dung. Trả về thông báo lỗi (nếu có).</summary>
    public async Task<string?> IssueAsync(Invoice inv)
    {
        if (inv.Status != InvoiceStatus.Draft) return "Hóa đơn đã phát hành.";
        if (inv.Lines.Count == 0) return "Hóa đơn chưa có dòng hàng / dịch vụ.";
        var need = inv.Lines.Where(l => l.ProductId.HasValue).GroupBy(l => l.ProductId!.Value).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var ids = need.Keys.ToList();
        var products = await _db.Products.Include(p => p.Stocks).Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var shortage = need.Where(n => products[n.Key].Stock < n.Value)
            .Select(n => $"{products[n.Key].Name} (cần {n.Value.ToQty()}, tồn {products[n.Key].Stock.ToQty()})").ToList();
        if (shortage.Count > 0) return "Không đủ tồn kho: " + string.Join("; ", shortage);

        foreach (var l in inv.Lines.Where(l => l.ProductId.HasValue))
            l.CostAmount = await _stock.IssueAsync(products[l.ProductId!.Value], l.Quantity, StockMoveType.Sale, nameof(Invoice), inv.Id, inv.Code, inv.CustomerName ?? inv.Note);
        inv.Recalc();
        inv.IssuedAt = DateTime.Now;
        inv.Status = InvoiceStatus.Unpaid;
        UpdateStatus(inv);
        return null;
    }

    public string? AddPayment(Invoice inv, decimal amount, PaymentMethod method, string? note)
    {
        if (inv.Status is InvoiceStatus.Draft or InvoiceStatus.Cancelled) return "Chỉ thu tiền hóa đơn đã phát hành.";
        if (amount <= 0) return "Số tiền thu phải lớn hơn 0.";
        if (amount > inv.Balance) return $"Số tiền thu vượt số còn phải thu ({inv.Balance.ToMoney()}).";
        inv.Payments.Add(new Payment { Amount = amount, Method = method, Note = note, UserId = _me.Id, At = DateTime.Now });
        inv.Paid += amount;
        UpdateStatus(inv);
        return null;
    }

    /// <summary>Hủy hóa đơn đã phát hành: hoàn nhập kho, ghi phiếu hoàn tiền nếu đã thu</summary>
    public async Task<string?> CancelAsync(Invoice inv, string reason)
    {
        if (inv.Status is InvoiceStatus.Draft or InvoiceStatus.Cancelled) return "Hóa đơn không ở trạng thái có thể hủy.";
        if (string.IsNullOrWhiteSpace(reason)) return "Nhập lý do hủy.";
        await _stock.ReturnAsync(nameof(Invoice), inv.Id, inv.Code, "Hủy hóa đơn: " + reason);
        if (inv.Paid > 0)
        {
            inv.Payments.Add(new Payment { Amount = -inv.Paid, Method = PaymentMethod.Cash, Note = "Hoàn tiền do hủy hóa đơn", UserId = _me.Id, At = DateTime.Now });
            inv.Paid = 0;
        }
        inv.Status = InvoiceStatus.Cancelled;
        inv.CancelledAt = DateTime.Now;
        inv.CancelReason = reason.Trim();
        // Cho phép lập lại hóa đơn cho phiếu khám / nội trú
        if (inv.VisitId.HasValue)
        {
            var v = await _db.Visits.FirstOrDefaultAsync(x => x.Id == inv.VisitId && x.InvoiceId == inv.Id);
            if (v != null) v.InvoiceId = null;
        }
        if (inv.AdmissionId.HasValue)
        {
            var a = await _db.Admissions.FirstOrDefaultAsync(x => x.Id == inv.AdmissionId && x.InvoiceId == inv.Id);
            if (a != null) a.InvoiceId = null;
        }
        return null;
    }

    public static void UpdateStatus(Invoice inv)
    {
        if (inv.Status is InvoiceStatus.Draft or InvoiceStatus.Cancelled) return;
        inv.Status = inv.Paid >= inv.Total ? InvoiceStatus.Paid : inv.Paid > 0 ? InvoiceStatus.Partial : InvoiceStatus.Unpaid;
    }
}
