using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using PetHospital.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Controllers;

/// <summary>Danh mục thuốc – hàng hóa, tồn kho, thẻ kho, điều chỉnh</summary>
[RequirePerm(Perm.StockView, Perm.StockManage)]
public class ProductsController : AppController
{
    public ProductsController(PetDbContext db) : base(db) { }

    public async Task<IActionResult> Index(ListFilter f, ProductKind? kind, string? alert, string? export)
    {
        var q = Db.Products.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var k = f.Q.Trim();
            q = q.Where(p => p.Name.Contains(k) || p.Code.Contains(k) || (p.Ingredient != null && p.Ingredient.Contains(k)));
        }
        if (kind.HasValue) q = q.Where(p => p.Kind == kind);
        var warnDays = await Svc<SettingsService>().GetIntAsync(SettingsService.ExpiryWarnDays, 60);
        var warn = DateTime.Today.AddDays(warnDays);
        if (alert == "low") q = q.Where(p => p.IsActive && p.Stock <= p.MinStock);
        else if (alert == "expiry") q = q.Where(p => p.Batches.Any(b => b.Quantity > 0 && b.ExpiryDate != null && b.ExpiryDate <= warn));
        q = q.OrderBy(p => p.Kind).ThenBy(p => p.Name);

        if (export == "xlsx")
        {
            var all = await q.ToListAsync();
            var cost = Me.CanSeeCost;
            return Excel("TonKho", "Tồn kho", cost
                    ? new[] { "Mã", "Tên", "Loại", "ĐVT", "Tồn", "Tồn tối thiểu", "Giá bán", "Giá vốn BQ", "Giá trị tồn" }
                    : new[] { "Mã", "Tên", "Loại", "ĐVT", "Tồn", "Tồn tối thiểu", "Giá bán" },
                all.Select(p => (IReadOnlyList<object?>)(cost
                    ? new object?[] { p.Code, p.Name, p.Kind.GetDisplayName(), p.Unit, p.Stock, p.MinStock, p.SalePrice, p.CostPrice, Math.Round(p.Stock * p.CostPrice, 0) }
                    : new object?[] { p.Code, p.Name, p.Kind.GetDisplayName(), p.Unit, p.Stock, p.MinStock, p.SalePrice })));
        }

        var total = await q.CountAsync();
        var items = await q.Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync();
        var ids = items.Select(p => p.Id).ToList();
        ViewBag.NearestExpiry = await Db.ProductBatches.Where(b => ids.Contains(b.ProductId) && b.Quantity > 0 && b.ExpiryDate != null)
            .GroupBy(b => b.ProductId).Select(g => new { g.Key, D = g.Min(x => x.ExpiryDate) }).ToDictionaryAsync(x => x.Key, x => x.D);
        ViewBag.Warn = warn;
        ViewBag.StockValue = Me.CanSeeCost ? await Db.Products.SumAsync(p => (decimal?)(p.Stock > 0 ? p.Stock * p.CostPrice : 0)) ?? 0m : 0m;
        ViewBag.LowCount = await Db.Products.CountAsync(p => p.IsActive && p.Stock <= p.MinStock);
        ViewBag.ExpiryCount = await Db.ProductBatches.Where(b => b.Quantity > 0 && b.ExpiryDate != null && b.ExpiryDate <= warn).Select(b => b.ProductId).Distinct().CountAsync();
        ViewBag.Kind = kind; ViewBag.Alert = alert; ViewBag.Filter = f;
        return View(new PagedList<Product>(items, total, f.Page, f.PageSize));
    }

    /// <summary>Thẻ kho: lô còn hàng + lịch sử nhập xuất</summary>
    public async Task<IActionResult> Details(int id, DateTime? from)
    {
        var p = await Db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        from ??= DateTime.Today.AddDays(-90);
        ViewBag.Batches = await Db.ProductBatches.AsNoTracking().Where(b => b.ProductId == id && b.Quantity > 0)
            .OrderBy(b => b.ExpiryDate == null).ThenBy(b => b.ExpiryDate).ToListAsync();
        ViewBag.Moves = await Db.StockMoves.AsNoTracking().Include(m => m.Batch).Include(m => m.User)
            .Where(m => m.ProductId == id && m.At >= from).OrderByDescending(m => m.At).ThenByDescending(m => m.Id).Take(500).ToListAsync();
        ViewBag.From = from;
        ViewBag.Warn = DateTime.Today.AddDays(await Svc<SettingsService>().GetIntAsync(SettingsService.ExpiryWarnDays, 60));
        return View(p);
    }

    [RequirePerm(Perm.StockManage)]
    public IActionResult Create() => View("Form", new Product());

    [RequirePerm(Perm.StockManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var p = await Db.Products.FindAsync(id);
        return p == null ? NotFound() : View("Form", p);
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.StockManage)]
    public async Task<IActionResult> Save(Product m)
    {
        m.Code = (m.Code ?? "").Trim().ToUpperInvariant();
        if (await Db.Products.AnyAsync(p => p.Code == m.Code && p.Id != m.Id)) ModelState.AddModelError(nameof(m.Code), "Mã hàng đã tồn tại.");
        if (!ModelState.IsValid) return View("Form", m);
        Product p;
        if (m.Id == 0) { p = new Product { CostPrice = m.CostPrice }; Db.Products.Add(p); }
        else p = await Db.Products.FirstOrDefaultAsync(x => x.Id == m.Id) ?? throw new InvalidOperationException();
        p.Code = m.Code; p.Name = m.Name.Trim(); p.Kind = m.Kind; p.Unit = m.Unit.Trim(); p.Ingredient = m.Ingredient; p.DefaultUsage = m.DefaultUsage;
        p.SalePrice = m.SalePrice; p.MinStock = m.MinStock; p.TrackExpiry = m.TrackExpiry; p.BoosterDays = m.Kind == ProductKind.Vaccine ? m.BoosterDays : null;
        p.IsActive = m.IsActive;
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã lưu {p.Name}.", nameof(Details), new { id = p.Id });
    }

    /// <summary>Điều chỉnh tồn theo kiểm kê thực tế / hủy hàng hỏng, hết hạn</summary>
    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.StockManage)]
    public async Task<IActionResult> Adjust(int id, decimal actual, string? lotNo, DateTime? expiry, string reason)
    {
        var p = await Db.Products.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        if (actual < 0) return BackWithError("Số lượng thực tế không âm.", nameof(Details), new { id });
        if (string.IsNullOrWhiteSpace(reason)) return BackWithError("Nhập lý do điều chỉnh.", nameof(Details), new { id });
        var delta = actual - p.Stock;
        if (delta == 0) return BackWithError("Tồn thực tế bằng tồn sổ sách – không cần điều chỉnh.", nameof(Details), new { id });
        await Svc<StockService>().AdjustAsync(p, delta, lotNo, expiry, reason.Trim());
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã điều chỉnh tồn {p.Name}: {(delta > 0 ? "+" : "")}{delta.ToQty()} {p.Unit}.", nameof(Details), new { id });
    }

    /// <summary>Xuất hủy một lô (hết hạn / hỏng)</summary>
    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.StockManage)]
    public async Task<IActionResult> DisposeBatch(int id, int batchId)
    {
        var b = await Db.ProductBatches.Include(x => x.Product).FirstOrDefaultAsync(x => x.Id == batchId && x.ProductId == id);
        if (b == null || b.Quantity <= 0) return NotFound();
        var p = b.Product!;
        var qty = b.Quantity;
        b.Quantity = 0;
        p.Stock -= qty;
        Db.StockMoves.Add(new StockMove
        {
            ProductId = p.Id, BatchId = b.Id, Type = StockMoveType.AdjustOut, Quantity = -qty, UnitCost = b.UnitCost, Balance = p.Stock,
            RefType = "Adjust", RefCode = $"HUY{DateTime.Now:yyMMddHHmm}", Note = $"Xuất hủy lô {b.LotNo} (HSD {b.ExpiryDate:dd/MM/yyyy})", UserId = CurrentUserId
        });
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã xuất hủy lô {b.LotNo}: {qty.ToQty()} {p.Unit}.", nameof(Details), new { id });
    }
}

/// <summary>Phiếu nhập kho</summary>
[RequirePerm(Perm.StockView, Perm.StockManage)]
public class ReceiptsController : AppController
{
    public ReceiptsController(PetDbContext db) : base(db) { }

    public async Task<IActionResult> Index(ListFilter f)
    {
        var q = Db.StockReceipts.AsNoTracking().Include(r => r.CreatedBy).AsQueryable();
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var k = f.Q.Trim();
            q = q.Where(r => r.Code.Contains(k) || (r.Supplier != null && r.Supplier.Contains(k)) || (r.SupplierInvoiceNo != null && r.SupplierInvoiceNo.Contains(k)));
        }
        q = q.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id);
        var total = await q.CountAsync();
        var items = await q.Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync();
        ViewBag.Filter = f;
        return View(new PagedList<StockReceipt>(items, total, f.Page, f.PageSize));
    }

    public async Task<IActionResult> Details(int id)
    {
        var r = await Db.StockReceipts.AsNoTracking().Include(x => x.CreatedBy).Include(x => x.Lines).ThenInclude(l => l.Product).FirstOrDefaultAsync(x => x.Id == id);
        return r == null ? NotFound() : View(r);
    }

    [RequirePerm(Perm.StockManage)]
    public async Task<IActionResult> Create()
    {
        ViewBag.ProductList = await Svc<Lookups>().ProductsAsync();
        return View("Form", new ReceiptFormModel { Lines = new List<ReceiptLineInput> { new() } });
    }

    [RequirePerm(Perm.StockManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var r = await Db.StockReceipts.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        if (r.State != DocState.Draft) return BackWithError("Phiếu đã ghi sổ, không sửa được.", nameof(Details), new { id });
        ViewBag.ProductList = await Svc<Lookups>().ProductsAsync();
        return View("Form", new ReceiptFormModel
        {
            Receipt = r,
            Lines = r.Lines.OrderBy(l => l.Id).Select(l => new ReceiptLineInput { ProductId = l.ProductId, LotNo = l.LotNo, ExpiryDate = l.ExpiryDate, Quantity = l.Quantity, UnitCost = l.UnitCost }).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.StockManage)]
    public async Task<IActionResult> Save(ReceiptFormModel m, string? command)
    {
        foreach (var k in ModelState.Keys.Where(k => k.StartsWith("Lines[") || k.StartsWith("Receipt.CreatedBy")).ToList()) ModelState.Remove(k);
        var lines = m.Lines.Where(l => l.ProductId > 0).ToList();
        if (lines.Count == 0) ModelState.AddModelError("", "Nhập ít nhất một dòng hàng.");
        if (lines.Any(l => l.Quantity <= 0 || l.UnitCost < 0)) ModelState.AddModelError("", "Số lượng phải lớn hơn 0, đơn giá không âm.");
        var ids = lines.Select(l => l.ProductId!.Value).Distinct().ToList();
        var products = await Db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        foreach (var l in lines.Where(l => products.TryGetValue(l.ProductId!.Value, out var p) && p.TrackExpiry && l.ExpiryDate == null))
            ModelState.AddModelError("", $"{products[l.ProductId!.Value].Name}: nhập hạn dùng.");
        if (!ModelState.IsValid)
        {
            ViewBag.ProductList = await Svc<Lookups>().ProductsAsync();
            return View("Form", m);
        }

        StockReceipt r;
        if (m.Receipt.Id == 0)
        {
            r = new StockReceipt { Code = await Codes.NextAsync(Db.StockReceipts.Select(x => x.Code), "NK", m.Receipt.Date), CreatedById = CurrentUserId };
            Db.StockReceipts.Add(r);
        }
        else
        {
            r = await Db.StockReceipts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == m.Receipt.Id) ?? throw new InvalidOperationException();
            if (r.State != DocState.Draft) return BackWithError("Phiếu đã ghi sổ.", nameof(Details), new { id = r.Id });
        }
        r.Date = m.Receipt.Date; r.Supplier = m.Receipt.Supplier; r.SupplierInvoiceNo = m.Receipt.SupplierInvoiceNo; r.Note = m.Receipt.Note;
        r.Lines.Clear();
        foreach (var l in lines)
            r.Lines.Add(new StockReceiptLine
            {
                ProductId = l.ProductId!.Value, LotNo = l.LotNo?.Trim(), ExpiryDate = l.ExpiryDate, Quantity = l.Quantity, UnitCost = l.UnitCost,
                Amount = Math.Round(l.Quantity * l.UnitCost, 0)
            });
        r.Total = r.Lines.Sum(x => x.Amount);
        await Db.SaveChangesAsync();
        if (command == "post")
        {
            await Svc<StockService>().PostReceiptAsync(r);
            await Db.SaveChangesAsync();
            return BackWithSuccess($"Đã ghi sổ phiếu nhập {r.Code} – cộng tồn kho.", nameof(Details), new { id = r.Id });
        }
        return BackWithSuccess($"Đã lưu nháp phiếu nhập {r.Code}.", nameof(Details), new { id = r.Id });
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.StockManage)]
    public async Task<IActionResult> Post(int id)
    {
        var r = await Db.StockReceipts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        if (r.State != DocState.Draft) return BackWithError("Phiếu đã ghi sổ.", nameof(Details), new { id });
        if (r.Lines.Count == 0) return BackWithError("Phiếu chưa có dòng hàng.", nameof(Details), new { id });
        await Svc<StockService>().PostReceiptAsync(r);
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã ghi sổ phiếu nhập {r.Code}.", nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.StockManage)]
    public async Task<IActionResult> Delete(int id)
    {
        var r = await Db.StockReceipts.FindAsync(id);
        if (r == null) return NotFound();
        if (r.State != DocState.Draft) return BackWithError("Phiếu đã ghi sổ, không xóa được.", nameof(Details), new { id });
        Db.StockReceipts.Remove(r);
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã xóa phiếu nháp {r.Code}.", nameof(Index));
    }

    public async Task<IActionResult> Print(int id)
    {
        var r = await Db.StockReceipts.AsNoTracking().Include(x => x.CreatedBy).Include(x => x.Lines).ThenInclude(l => l.Product).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        ViewBag.Settings = Svc<SettingsService>();
        ViewData["Layout"] = "_PrintLayout";
        return View(r);
    }
}
