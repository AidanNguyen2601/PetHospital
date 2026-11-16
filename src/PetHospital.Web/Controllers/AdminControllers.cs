using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using PetHospital.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Controllers;

/// <summary>Người dùng & vai trò</summary>
[RequirePerm(Perm.Users)]
public class UsersController : AppController
{
    private readonly IPasswordHasher<AppUser> _hasher;
    public UsersController(PetDbContext db, IPasswordHasher<AppUser> hasher) : base(db) => _hasher = hasher;

    public async Task<IActionResult> Index(ListFilter f, string? role)
    {
        var q = Db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(f.Q)) q = q.Where(u => u.Username.Contains(f.Q) || u.FullName.Contains(f.Q) || (u.Email != null && u.Email.Contains(f.Q)));
        if (!string.IsNullOrEmpty(role)) q = q.Where(u => u.Roles.Contains(role));
        var total = await q.CountAsync();
        var items = await q.OrderBy(u => u.Username).Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync();
        ViewBag.Filter = f; ViewBag.Role = role;
        return View(new PagedList<AppUser>(items, total, f.Page, f.PageSize));
    }

    public IActionResult Create() => View("Form", new UserEditViewModel { Roles = { AppRoles.Receptionist } });

    public async Task<IActionResult> Edit(int id)
    {
        var u = await Db.Users.FindAsync(id);
        if (u == null) return NotFound();
        return View("Form", new UserEditViewModel
        {
            Id = u.Id, Username = u.Username, FullName = u.FullName, Title = u.Title, Email = u.Email, Phone = u.Phone,
            Roles = u.RoleList().ToList(), IsVet = u.IsVet, IsActive = u.IsActive
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(UserEditViewModel m)
    {
        AppUser? u = null;
        if (m.Id != 0)
        {
            u = await Db.Users.FindAsync(m.Id);
            if (u == null) return NotFound();
        }
        if (u == null && string.IsNullOrEmpty(m.Password)) ModelState.AddModelError(nameof(m.Password), "Nhập mật khẩu ban đầu.");
        if (await Db.Users.AnyAsync(x => x.Username == m.Username && x.Id != m.Id)) ModelState.AddModelError(nameof(m.Username), "Tên đăng nhập đã tồn tại.");
        m.Roles = m.Roles.Where(r => AppRoles.All.Any(x => x.Code == r)).Distinct().ToList();
        if (m.Roles.Count == 0) ModelState.AddModelError(nameof(m.Roles), "Chọn ít nhất một vai trò.");
        var wasAdmin = u?.RoleList().Contains(AppRoles.Admin) == true;
        if (m.Roles.Contains(AppRoles.Admin) != wasAdmin && !Me.IsAdmin)
            ModelState.AddModelError(nameof(m.Roles), "Chỉ quản trị hệ thống mới cấp / bỏ vai trò quản trị.");
        if (u != null && u.Id == CurrentUserId && wasAdmin && (!m.IsActive || !m.Roles.Contains(AppRoles.Admin)))
            ModelState.AddModelError("", "Không tự khóa / bỏ quyền quản trị của chính mình.");
        if (!ModelState.IsValid) return View("Form", m);

        if (u == null) { u = new AppUser(); Db.Users.Add(u); }
        u.Username = m.Username.Trim(); u.FullName = m.FullName.Trim(); u.Title = m.Title?.Trim(); u.Email = m.Email; u.Phone = m.Phone;
        u.Roles = string.Join(",", m.Roles); u.IsVet = m.IsVet || m.Roles.Contains(AppRoles.Vet); u.IsActive = m.IsActive;
        if (!string.IsNullOrEmpty(m.Password)) u.PasswordHash = _hasher.HashPassword(u, m.Password);
        await Db.SaveChangesAsync();
        return BackWithSuccess($"Đã lưu tài khoản {u.Username}. Thay đổi vai trò có hiệu lực từ lần đăng nhập sau.", nameof(Index));
    }
}

/// <summary>Ma trận vai trò – quyền chức năng</summary>
[RequirePerm(Perm.Users)]
public class RolesController : AppController
{
    public RolesController(PetDbContext db) : base(db) { }

    public async Task<IActionResult> Index()
    {
        ViewBag.Matrix = (await Db.RolePermissions.AsNoTracking().ToListAsync()).GroupBy(r => r.Role).ToDictionary(g => g.Key, g => g.Select(x => x.Permission).ToHashSet());
        ViewBag.UserCounts = (await Db.Users.AsNoTracking().Where(u => u.IsActive).Select(u => u.Roles).ToListAsync())
            .SelectMany(r => r.Split(',', StringSplitOptions.TrimEntries)).GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(IFormCollection form)
    {
        var all = await Db.RolePermissions.ToListAsync();
        foreach (var (role, _) in AppRoles.All)
        {
            if (role == AppRoles.Admin) continue;
            var selected = form[$"perm_{role}"].Where(v => !string.IsNullOrEmpty(v) && Perm.All.Any(p => p.Code == v)).Select(v => v!).ToHashSet();
            var current = all.Where(r => r.Role == role).ToList();
            Db.RolePermissions.RemoveRange(current.Where(r => !selected.Contains(r.Permission)));
            foreach (var p in selected.Where(p => current.All(c => c.Permission != p)))
                Db.RolePermissions.Add(new RolePermission { Role = role, Permission = p });
        }
        await Db.SaveChangesAsync();
        Svc<SecurityCache>().Invalidate();
        return BackWithSuccess("Đã lưu phân quyền (có hiệu lực ngay).", nameof(Index));
    }
}

/// <summary>Nhật ký hệ thống</summary>
[RequirePerm(Perm.System)]
public class AuditController : AppController
{
    public AuditController(PetDbContext db) : base(db) { }

    public async Task<IActionResult> Index(ListFilter f, string? entity, string? action, int? userId, DateTime? from, DateTime? to, string? entityId)
    {
        var q = Db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(entity)) q = q.Where(a => a.Entity == entity);
        if (!string.IsNullOrEmpty(entityId)) q = q.Where(a => a.EntityId == entityId);
        if (!string.IsNullOrEmpty(action)) q = q.Where(a => a.Action == action);
        if (userId.HasValue) q = q.Where(a => a.UserId == userId);
        if (from.HasValue) q = q.Where(a => a.At >= from);
        if (to.HasValue) { var t = to.Value.Date.AddDays(1); q = q.Where(a => a.At < t); }
        if (!string.IsNullOrWhiteSpace(f.Q)) q = q.Where(a => (a.Changes != null && a.Changes.Contains(f.Q)) || (a.UserName != null && a.UserName.Contains(f.Q)));
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(a => a.Id).Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync();
        ViewBag.Entities = await Db.AuditLogs.Select(a => a.Entity).Distinct().OrderBy(x => x).ToListAsync();
        ViewBag.Users = await Db.Users.AsNoTracking().OrderBy(u => u.FullName).ToListAsync();
        ViewBag.Filter = f; ViewBag.Entity = entity; ViewBag.Action = action; ViewBag.UserId = userId; ViewBag.From = from; ViewBag.To = to; ViewBag.EntityId = entityId;
        return View(new PagedList<AuditLog>(items, total, f.Page, f.PageSize));
    }
}

/// <summary>Thông tin phòng khám & tham số</summary>
[RequirePerm(Perm.System)]
public class SettingsController : AppController
{
    public SettingsController(PetDbContext db) : base(db) { }

    public async Task<IActionResult> Index()
    {
        var s = Svc<SettingsService>();
        var values = new Dictionary<string, string>();
        foreach (var (key, _, _) in SettingsService.All) values[key] = await s.GetAsync(key);
        return View(values);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(IFormCollection form)
    {
        var s = Svc<SettingsService>();
        foreach (var (key, label, _) in SettingsService.All)
        {
            var v = form[key].ToString().Trim();
            if ((key == SettingsService.VaccineRemindDays || key == SettingsService.ExpiryWarnDays) && (!int.TryParse(v, out var n) || n < 0 || n > 3650))
                return BackWithError($"{label}: nhập số ngày từ 0 đến 3650.", nameof(Index));
            await s.SetAsync(key, v.Truncate(1000));
        }
        await Db.SaveChangesAsync();
        return BackWithSuccess("Đã lưu thông tin phòng khám.", nameof(Index));
    }
}

/// <summary>Nạp dữ liệu mô phỏng luồng nghiệp vụ (demo / đào tạo / kiểm thử). Nạp thêm, không xóa dữ liệu có sẵn.</summary>
[RequirePerm(Perm.System)]
public class DemoDataController : AppController
{
    public DemoDataController(PetDbContext db) : base(db) { }

    public async Task<IActionResult> Index()
    {
        ViewBag.Counts = new (string Label, string Icon, int Count)[]
        {
            ("Khách hàng", "users", await Db.Owners.CountAsync()),
            ("Thú cưng", "paw", await Db.Pets.CountAsync()),
            ("Lịch hẹn", "calendar", await Db.Appointments.CountAsync()),
            ("Phiếu khám", "stethoscope", await Db.Visits.CountAsync()),
            ("Mũi tiêm", "syringe", await Db.Vaccinations.CountAsync()),
            ("Nội trú / lưu chuồng", "bed", await Db.Admissions.CountAsync()),
            ("Phiếu nhập kho", "box", await Db.StockReceipts.CountAsync()),
            ("Hóa đơn", "receipt", await Db.Invoices.CountAsync()),
            ("Lần thu tiền", "wallet", await Db.Payments.CountAsync()),
        };
        ViewBag.FirstVisit = await Db.Visits.MinAsync(v => (DateTime?)v.VisitAt);
        ViewBag.LastVisit = await Db.Visits.MaxAsync(v => (DateTime?)v.VisitAt);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(int days, int level, bool includeToday = true)
    {
        if (!FlowSimulator.DayOptions.Contains(days) || FlowSimulator.Levels.All(l => l.Code != level)) return BadRequest();
        FlowSimResult r;
        try
        {
            r = await FlowSimulator.RunAsync(Db, Svc<StockService>(), days, level, includeToday);
        }
        catch (InvalidOperationException ex)
        {
            return BackWithError(ex.Message, nameof(Index));
        }
        Db.AuditLogs.Add(new AuditLog
        {
            UserId = CurrentUserId, UserName = Me.FullName, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Entity = "DemoData", Action = "Tạo", Changes = $"Mô phỏng {r.Days} ngày: {r.Visits} phiếu khám, {r.Invoices} hóa đơn"
        });
        Db.SuppressAudit = true;
        await Db.SaveChangesAsync();
        return BackWithSuccess(
            $"Đã mô phỏng {r.Days} ngày: {r.NewOwners} khách hàng mới / {r.NewPets} thú cưng, {r.Appointments} lịch hẹn ({r.NoShows} không đến / hủy), " +
            $"{r.Visits} phiếu khám, {r.Vaccinations} mũi tiêm, {r.Admissions} lượt nội trú / lưu chuồng, {r.Receipts} phiếu nhập kho, " +
            $"{r.Invoices} hóa đơn ({r.Cancelled} hủy), {r.Payments} lần thu tiền – doanh thu {r.Revenue.ToMoney()}.", nameof(Index));
    }
}
