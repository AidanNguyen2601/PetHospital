using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace PetHospital.Web.Controllers;

/// <summary>
/// Cấu hình giao diện: phông chữ, cỡ chữ, màu chủ đạo, vị trí & màu menu.
/// Mọi người dùng chỉnh giao diện riêng của mình; quản trị hệ thống chỉnh giao diện mặc định cho toàn chuỗi.
/// </summary>
public class AppearanceController : AppController
{
    public AppearanceController(PetDbContext db) : base(db) { }

    public async Task<IActionResult> Index(string? scope)
    {
        var theme = Svc<ThemeService>();
        var system = scope == "system" && Me.Has(Perm.System);
        var mine = await theme.UserAsync(CurrentUserId);
        ViewBag.Scope = system ? "system" : "me";
        ViewBag.CanSystem = Me.Has(Perm.System);
        ViewBag.UseDefault = !system && mine == null;
        ViewBag.SystemTheme = await theme.SystemAsync();
        return View(system ? await theme.SystemAsync() : mine ?? (await theme.SystemAsync()).Clone());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string scope, UiTheme m, string? accentCustom, bool useDefault)
    {
        if (m.Accent == "custom") m.Accent = accentCustom ?? "";
        m.Normalize();
        if (scope == "system")
        {
            if (!Me.Has(Perm.System)) return Forbid();
            await Svc<SettingsService>().SetAsync(ThemeService.SettingKey, m.ToJson());
            await Db.SaveChangesAsync();
            return BackWithSuccess("Đã lưu giao diện mặc định của hệ thống. Người dùng chưa chọn giao diện riêng sẽ dùng giao diện này.", nameof(Index), new { scope });
        }
        var u = await Db.Users.FindAsync(CurrentUserId);
        if (u == null) return NotFound();
        u.UiTheme = useDefault ? null : m.ToJson();
        await Db.SaveChangesAsync();
        return BackWithSuccess(useDefault ? "Đã chuyển về giao diện mặc định của hệ thống." : "Đã lưu giao diện của bạn.", nameof(Index));
    }
}
