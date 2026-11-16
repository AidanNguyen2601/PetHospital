using PetHospital.Web.Data;
using PetHospital.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Infrastructure;

/// <summary>
/// Sổ kho theo từng chi nhánh: nhập theo lô, xuất theo hạn dùng gần nhất trước (FEFO), giá vốn bình quân tại chi nhánh,
/// chuyển kho giữa chi nhánh (giữ nguyên số lô, hạn dùng, giá vốn).
/// Mặc định thao tác trên kho của chi nhánh đang làm việc.
/// </summary>
public class StockService
{
    private readonly PetDbContext _db;
    private readonly CurrentUser _me;
    public StockService(PetDbContext db, CurrentUser me) { _db = db; _me = me; }

    private int? UserId => _me.IsAuthenticated ? _me.Id : null;

    /// <summary>Dòng tồn kho của mặt hàng tại chi nhánh (tạo mới nếu chưa có)</summary>
    public async Task<ProductStock> StockOfAsync(int productId, int? branchId = null)
    {
        var branch = branchId ?? _db.WriteBranchId;
        var local = _db.ProductStocks.Local.FirstOrDefault(s => s.ProductId == productId && s.BranchId == branch);
        if (local != null) return local;
        var s = await _db.ProductStocks.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.ProductId == productId && x.BranchId == branch);
        if (s == null)
        {
            var cost = await _db.Products.Where(p => p.Id == productId).Select(p => p.CostPrice).FirstAsync();
            s = new ProductStock { ProductId = productId, BranchId = branch, CostPrice = cost };
            _db.ProductStocks.Add(s);
        }
        return s;
    }

    /// <summary>Ghi sổ phiếu nhập: tạo lô, cộng tồn, tính lại giá vốn bình quân tại chi nhánh nhập</summary>
    public async Task PostReceiptAsync(StockReceipt r)
    {
        if (r.BranchId == 0) r.BranchId = _db.WriteBranchId;
        foreach (var line in r.Lines)
        {
            var product = await _db.Products.FirstAsync(p => p.Id == line.ProductId);
            var batch = new ProductBatch
            {
                BranchId = r.BranchId, ProductId = product.Id, LotNo = line.LotNo, ExpiryDate = line.ExpiryDate, Quantity = line.Quantity,
                UnitCost = line.UnitCost, ReceivedAt = r.Date
            };
            _db.ProductBatches.Add(batch);
            var ps = await StockOfAsync(product.Id, r.BranchId);
            AddIn(ps, line.Quantity, line.UnitCost);
            product.CostPrice = ps.CostPrice;
            _db.StockMoves.Add(new StockMove
            {
                BranchId = r.BranchId, ProductId = product.Id, Batch = batch, Type = StockMoveType.Receipt, Quantity = line.Quantity, UnitCost = line.UnitCost,
                Balance = ps.Quantity, RefType = nameof(StockReceipt), RefId = r.Id, RefCode = r.Code, Note = r.Supplier, UserId = UserId
            });
        }
        r.State = DocState.Posted;
        r.PostedAt = DateTime.Now;
    }

    /// <summary>Cộng tồn và tính lại giá vốn bình quân</summary>
    private static void AddIn(ProductStock ps, decimal qty, decimal unitCost)
    {
        var before = Math.Max(0, ps.Quantity);
        var after = before + qty;
        ps.CostPrice = after > 0 ? Math.Round((before * ps.CostPrice + qty * unitCost) / after, 2) : unitCost;
        ps.Quantity += qty;
    }

    /// <summary>Một phần hàng đã xuất: lô (nếu có), số lượng, giá vốn</summary>
    public sealed record Taken(ProductBatch? Batch, decimal Quantity, decimal UnitCost);

    /// <summary>Xuất kho chi nhánh đang làm việc theo FEFO. Trả về tổng giá vốn xuất.</summary>
    public async Task<decimal> IssueAsync(Product product, decimal qty, StockMoveType type, string refType, int refId, string refCode, string? note = null)
    {
        var taken = await IssueCoreAsync(product.Id, _db.WriteBranchId, qty, type, refType, refId, refCode, note);
        return Math.Round(taken.Sum(t => t.Quantity * t.UnitCost), 0);
    }

    private async Task<List<Taken>> IssueCoreAsync(int productId, int branchId, decimal qty, StockMoveType type, string refType, int refId, string refCode, string? note)
    {
        var ps = await StockOfAsync(productId, branchId);
        var remaining = qty;
        var taken = new List<Taken>();
        var batches = await _db.ProductBatches.IgnoreQueryFilters().Where(b => b.ProductId == productId && b.BranchId == branchId && b.Quantity > 0)
            .OrderBy(b => b.ExpiryDate == null).ThenBy(b => b.ExpiryDate).ThenBy(b => b.Id).ToListAsync();
        foreach (var b in batches)
        {
            if (remaining <= 0) break;
            var take = Math.Min(b.Quantity, remaining);
            if (take <= 0) continue;
            b.Quantity -= take;
            remaining -= take;
            ps.Quantity -= take;
            taken.Add(new Taken(b, take, b.UnitCost));
            _db.StockMoves.Add(new StockMove
            {
                BranchId = branchId, ProductId = productId, BatchId = b.Id, Type = type, Quantity = -take, UnitCost = b.UnitCost, Balance = ps.Quantity,
                RefType = refType, RefId = refId, RefCode = refCode, Note = note, UserId = UserId
            });
        }
        if (remaining > 0)
        {
            // Hàng không theo lô (tồn đầu kỳ chưa khai báo lô): xuất theo giá vốn bình quân
            ps.Quantity -= remaining;
            taken.Add(new Taken(null, remaining, ps.CostPrice));
            _db.StockMoves.Add(new StockMove
            {
                BranchId = branchId, ProductId = productId, Type = type, Quantity = -remaining, UnitCost = ps.CostPrice, Balance = ps.Quantity,
                RefType = refType, RefId = refId, RefCode = refCode, Note = note, UserId = UserId
            });
        }
        return taken;
    }

    /// <summary>Hoàn nhập toàn bộ lượng đã xuất cho một chứng từ (hủy hóa đơn) – vào đúng kho đã xuất</summary>
    public async Task ReturnAsync(string refType, int refId, string refCode, string? note)
    {
        var moves = await _db.StockMoves.IgnoreQueryFilters().Where(m => m.RefType == refType && m.RefId == refId && m.Quantity < 0).ToListAsync();
        foreach (var m in moves)
        {
            var ps = await StockOfAsync(m.ProductId, m.BranchId);
            var qty = -m.Quantity;
            if (m.BatchId.HasValue)
            {
                var batch = await _db.ProductBatches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Id == m.BatchId);
                if (batch != null) batch.Quantity += qty;
            }
            ps.Quantity += qty;
            _db.StockMoves.Add(new StockMove
            {
                BranchId = m.BranchId, ProductId = m.ProductId, BatchId = m.BatchId, Type = StockMoveType.Return, Quantity = qty, UnitCost = m.UnitCost, Balance = ps.Quantity,
                RefType = refType, RefId = refId, RefCode = refCode, Note = note, UserId = UserId
            });
        }
    }

    /// <summary>Điều chỉnh tồn tại chi nhánh đang làm việc. delta &gt; 0: tạo lô mới; delta &lt; 0: xuất FEFO.</summary>
    public async Task AdjustAsync(Product product, decimal delta, string? lotNo, DateTime? expiry, string reason)
    {
        var code = $"DC{DateTime.Now:yyMMddHHmmss}";
        if (delta > 0)
        {
            var ps = await StockOfAsync(product.Id);
            var batch = new ProductBatch { BranchId = ps.BranchId, ProductId = product.Id, LotNo = lotNo, ExpiryDate = expiry, Quantity = delta, UnitCost = ps.CostPrice };
            _db.ProductBatches.Add(batch);
            ps.Quantity += delta;
            _db.StockMoves.Add(new StockMove
            {
                BranchId = ps.BranchId, ProductId = product.Id, Batch = batch, Type = StockMoveType.AdjustIn, Quantity = delta, UnitCost = ps.CostPrice, Balance = ps.Quantity,
                RefType = "Adjust", RefCode = code, Note = reason, UserId = UserId
            });
        }
        else if (delta < 0)
            await IssueAsync(product, -delta, StockMoveType.AdjustOut, "Adjust", 0, code, reason);
    }

    /// <summary>Xuất hủy toàn bộ một lô (hết hạn / hỏng)</summary>
    public async Task DisposeBatchAsync(ProductBatch b)
    {
        var ps = await StockOfAsync(b.ProductId, b.BranchId);
        var qty = b.Quantity;
        b.Quantity = 0;
        ps.Quantity -= qty;
        _db.StockMoves.Add(new StockMove
        {
            BranchId = b.BranchId, ProductId = b.ProductId, BatchId = b.Id, Type = StockMoveType.AdjustOut, Quantity = -qty, UnitCost = b.UnitCost, Balance = ps.Quantity,
            RefType = "Adjust", RefCode = $"HUY{DateTime.Now:yyMMddHHmm}", Note = $"Xuất hủy lô {b.LotNo} (HSD {b.ExpiryDate:dd/MM/yyyy})", UserId = UserId
        });
    }

    /// <summary>
    /// Ghi sổ phiếu chuyển kho: xuất FEFO tại chi nhánh xuất, nhập vào chi nhánh nhận đúng số lô / hạn dùng / giá vốn.
    /// Trả về thông báo lỗi nếu không đủ tồn.
    /// </summary>
    public async Task<string?> PostTransferAsync(StockTransfer t)
    {
        if (t.State != DocState.Draft) return "Phiếu đã ghi sổ.";
        if (t.Lines.Count == 0) return "Phiếu chưa có dòng hàng.";
        if (t.BranchId == t.ToBranchId) return "Chi nhánh nhận phải khác chi nhánh xuất.";
        var need = t.Lines.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var names = await _db.Products.AsNoTracking().Where(p => need.Keys.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);
        var shortage = new List<string>();
        foreach (var (pid, q) in need)
        {
            var ps = await StockOfAsync(pid, t.BranchId);
            if (ps.Quantity < q) shortage.Add($"{names.GetValueOrDefault(pid)} (cần {q.ToQty()}, tồn {ps.Quantity.ToQty()})");
        }
        if (shortage.Count > 0) return "Không đủ tồn kho chi nhánh xuất: " + string.Join("; ", shortage);

        var toName = await _db.Branches.Where(b => b.Id == t.ToBranchId).Select(b => b.Name).FirstAsync();
        var fromName = await _db.Branches.Where(b => b.Id == t.BranchId).Select(b => b.Name).FirstAsync();
        foreach (var line in t.Lines)
        {
            var taken = await IssueCoreAsync(line.ProductId, t.BranchId, line.Quantity, StockMoveType.TransferOut, nameof(StockTransfer), t.Id, t.Code, "Chuyển đến " + toName);
            var dest = await StockOfAsync(line.ProductId, t.ToBranchId);
            foreach (var x in taken)
            {
                var batch = new ProductBatch
                {
                    BranchId = t.ToBranchId, ProductId = line.ProductId, LotNo = x.Batch?.LotNo, ExpiryDate = x.Batch?.ExpiryDate,
                    Quantity = x.Quantity, UnitCost = x.UnitCost, ReceivedAt = t.Date
                };
                _db.ProductBatches.Add(batch);
                AddIn(dest, x.Quantity, x.UnitCost);
                _db.StockMoves.Add(new StockMove
                {
                    BranchId = t.ToBranchId, ProductId = line.ProductId, Batch = batch, Type = StockMoveType.TransferIn, Quantity = x.Quantity, UnitCost = x.UnitCost,
                    Balance = dest.Quantity, RefType = nameof(StockTransfer), RefId = t.Id, RefCode = t.Code, Note = "Nhận từ " + fromName, UserId = UserId
                });
            }
            line.CostAmount = Math.Round(taken.Sum(x => x.Quantity * x.UnitCost), 0);
        }
        t.Total = t.Lines.Sum(l => l.CostAmount);
        t.State = DocState.Posted;
        t.PostedAt = DateTime.Now;
        return null;
    }
}
