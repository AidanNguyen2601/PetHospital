using PetHospital.Web.Data;
using PetHospital.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Infrastructure;

/// <summary>Sổ kho: nhập theo lô, xuất theo hạn dùng gần nhất trước (FEFO), giá vốn bình quân</summary>
public class StockService
{
    private readonly PetDbContext _db;
    private readonly CurrentUser _me;
    public StockService(PetDbContext db, CurrentUser me) { _db = db; _me = me; }

    private int? UserId => _me.IsAuthenticated ? _me.Id : null;

    /// <summary>Ghi sổ phiếu nhập: tạo lô, cộng tồn, tính lại giá vốn bình quân</summary>
    public async Task PostReceiptAsync(StockReceipt r)
    {
        foreach (var line in r.Lines)
        {
            var product = await _db.Products.FirstAsync(p => p.Id == line.ProductId);
            var batch = new ProductBatch
            {
                ProductId = product.Id, LotNo = line.LotNo, ExpiryDate = line.ExpiryDate, Quantity = line.Quantity,
                UnitCost = line.UnitCost, ReceivedAt = r.Date
            };
            _db.ProductBatches.Add(batch);
            var before = Math.Max(0, product.Stock);
            var after = before + line.Quantity;
            product.CostPrice = after > 0 ? Math.Round((before * product.CostPrice + line.Quantity * line.UnitCost) / after, 2) : line.UnitCost;
            product.Stock += line.Quantity;
            _db.StockMoves.Add(new StockMove
            {
                ProductId = product.Id, Batch = batch, Type = StockMoveType.Receipt, Quantity = line.Quantity, UnitCost = line.UnitCost,
                Balance = product.Stock, RefType = nameof(StockReceipt), RefId = r.Id, RefCode = r.Code, Note = r.Supplier, UserId = UserId
            });
        }
        r.State = DocState.Posted;
        r.PostedAt = DateTime.Now;
    }

    /// <summary>Xuất kho theo FEFO. Trả về tổng giá vốn xuất.</summary>
    public async Task<decimal> IssueAsync(Product product, decimal qty, StockMoveType type, string refType, int refId, string refCode, string? note = null)
    {
        var remaining = qty;
        decimal cost = 0;
        var batches = await _db.ProductBatches.Where(b => b.ProductId == product.Id && b.Quantity > 0)
            .OrderBy(b => b.ExpiryDate == null).ThenBy(b => b.ExpiryDate).ThenBy(b => b.Id).ToListAsync();
        foreach (var b in batches)
        {
            if (remaining <= 0) break;
            var take = Math.Min(b.Quantity, remaining);
            if (take <= 0) continue;
            b.Quantity -= take;
            remaining -= take;
            product.Stock -= take;
            cost += take * b.UnitCost;
            _db.StockMoves.Add(new StockMove
            {
                ProductId = product.Id, BatchId = b.Id, Type = type, Quantity = -take, UnitCost = b.UnitCost, Balance = product.Stock,
                RefType = refType, RefId = refId, RefCode = refCode, Note = note, UserId = UserId
            });
        }
        if (remaining > 0)
        {
            // Hàng không theo lô (tồn đầu kỳ chưa khai báo lô): xuất theo giá vốn bình quân
            product.Stock -= remaining;
            cost += remaining * product.CostPrice;
            _db.StockMoves.Add(new StockMove
            {
                ProductId = product.Id, Type = type, Quantity = -remaining, UnitCost = product.CostPrice, Balance = product.Stock,
                RefType = refType, RefId = refId, RefCode = refCode, Note = note, UserId = UserId
            });
        }
        return Math.Round(cost, 0);
    }

    /// <summary>Hoàn nhập toàn bộ lượng đã xuất cho một chứng từ (hủy hóa đơn)</summary>
    public async Task ReturnAsync(string refType, int refId, string refCode, string? note)
    {
        var moves = await _db.StockMoves.Where(m => m.RefType == refType && m.RefId == refId && m.Quantity < 0).ToListAsync();
        foreach (var m in moves)
        {
            var product = await _db.Products.FirstAsync(p => p.Id == m.ProductId);
            var qty = -m.Quantity;
            if (m.BatchId.HasValue)
            {
                var batch = await _db.ProductBatches.FirstOrDefaultAsync(b => b.Id == m.BatchId);
                if (batch != null) batch.Quantity += qty;
            }
            product.Stock += qty;
            _db.StockMoves.Add(new StockMove
            {
                ProductId = product.Id, BatchId = m.BatchId, Type = StockMoveType.Return, Quantity = qty, UnitCost = m.UnitCost, Balance = product.Stock,
                RefType = refType, RefId = refId, RefCode = refCode, Note = note, UserId = UserId
            });
        }
    }

    /// <summary>Điều chỉnh tồn (kiểm kê, hàng hỏng / hết hạn). delta &gt; 0: tạo lô mới; delta &lt; 0: xuất FEFO.</summary>
    public async Task AdjustAsync(Product product, decimal delta, string? lotNo, DateTime? expiry, string reason)
    {
        var code = $"DC{DateTime.Now:yyMMddHHmmss}";
        if (delta > 0)
        {
            var batch = new ProductBatch { ProductId = product.Id, LotNo = lotNo, ExpiryDate = expiry, Quantity = delta, UnitCost = product.CostPrice };
            _db.ProductBatches.Add(batch);
            product.Stock += delta;
            _db.StockMoves.Add(new StockMove
            {
                ProductId = product.Id, Batch = batch, Type = StockMoveType.AdjustIn, Quantity = delta, UnitCost = product.CostPrice, Balance = product.Stock,
                RefType = "Adjust", RefCode = code, Note = reason, UserId = UserId
            });
        }
        else if (delta < 0)
            await IssueAsync(product, -delta, StockMoveType.AdjustOut, "Adjust", 0, code, reason);
    }
}

/// <summary>Hóa đơn: lập từ phiếu khám / nội trú, phát hành (xuất kho), thu tiền, hủy</summary>
public class InvoiceService
{
    private readonly PetDbContext _db;
    private readonly StockService _stock;
    private readonly CurrentUser _me;
    public InvoiceService(PetDbContext db, StockService stock, CurrentUser me) { _db = db; _stock = stock; _me = me; }

    public Task<string> NextCodeAsync() => Codes.NextAsync(_db.Invoices.Select(i => i.Code), "HD");

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
        var products = await _db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
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
