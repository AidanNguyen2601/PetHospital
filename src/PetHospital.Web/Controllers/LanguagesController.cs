using System.Text;
using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using PetHospital.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Controllers;

/// <summary>Ngôn ngữ giao diện, bản dịch (chỉnh sửa theo vùng miền), chọn ngôn ngữ</summary>
public class LanguagesController : AppController
{
    public LanguagesController(PetDbContext db) : base(db) { }
    private Localizer Loc => Svc<Localizer>();

    /// <summary>Chọn ngôn ngữ giao diện (cả trang đăng nhập); lưu vào hồ sơ người dùng</summary>
    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Switch(string code, string? returnUrl)
    {
        var lang = Loc.Find(code);
        if (lang != null && lang.IsActive)
        {
            Response.Cookies.Append(LangScope.Cookie, lang.Code,
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax, HttpOnly = true });
            if (User.Identity?.IsAuthenticated == true && await Db.Users.FindAsync(CurrentUserId) is { } u)
            {
                u.Language = lang.Code;
                await Db.SaveChangesAsync();
            }
        }
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction("Index", "Home");
    }

    // ===================== Quản lý ngôn ngữ =====================

    [RequirePerm(Perm.System)]
    public async Task<IActionResult> Index()
    {
        var langs = await Db.Languages.AsNoTracking().OrderBy(l => l.SortOrder).ThenBy(l => l.Name).ToListAsync();
        ViewBag.TotalKeys = await AllKeysQuery().CountAsync();
        ViewBag.Own = await Db.Translations.Where(t => t.Value != null && t.Value != "").GroupBy(t => t.LanguageCode)
            .Select(g => new { g.Key, C = g.Count(), Custom = g.Count(x => x.IsCustom) }).ToDictionaryAsync(x => x.Key, x => (x.C, x.Custom));
        ViewBag.Default = Loc.DefaultCode;
        return View(langs);
    }

    [RequirePerm(Perm.System)]
    public async Task<IActionResult> Create()
    {
        await FormData();
        return View("Form", new Language { Culture = "vi-VN", DateFormat = "dd/MM/yyyy", SortOrder = (await Db.Languages.MaxAsync(l => (int?)l.SortOrder) ?? 0) + 1 });
    }

    [RequirePerm(Perm.System)]
    public async Task<IActionResult> Edit(int id)
    {
        var l = await Db.Languages.FindAsync(id);
        if (l == null) return NotFound();
        await FormData();
        return View("Form", l);
    }

    private async Task FormData() =>
        ViewBag.Languages = await Db.Languages.AsNoTracking().OrderBy(l => l.SortOrder).ToListAsync();

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.System)]
    public async Task<IActionResult> Save(Language m, string? copyFrom)
    {
        m.Code = (m.Code ?? "").Trim();
        if (await Db.Languages.AnyAsync(l => l.Code == m.Code && l.Id != m.Id)) ModelState.AddModelError(nameof(m.Code), "Mã ngôn ngữ đã tồn tại.");
        try { System.Globalization.CultureInfo.GetCultureInfo(m.Culture); }
        catch (System.Globalization.CultureNotFoundException) { ModelState.AddModelError(nameof(m.Culture), "Định dạng số không hợp lệ (VD: vi-VN, en-US, ko-KR)."); }
        try { _ = DateTime.Today.ToString(m.DateFormat); } catch (FormatException) { ModelState.AddModelError(nameof(m.DateFormat), "Định dạng ngày không hợp lệ (VD: dd/MM/yyyy)."); }
        if (!string.IsNullOrEmpty(m.FallbackCode) && (m.FallbackCode == m.Code || !await Db.Languages.AnyAsync(l => l.Code == m.FallbackCode)))
            ModelState.AddModelError(nameof(m.FallbackCode), "Ngôn ngữ kế thừa không hợp lệ.");
        Language? l = null;
        if (m.Id != 0)
        {
            l = await Db.Languages.FirstOrDefaultAsync(x => x.Id == m.Id);
            if (l == null) return NotFound();
            if (l.Code != m.Code && (l.Code == Localizer.Source || await Db.Translations.AnyAsync(t => t.LanguageCode == l.Code)))
                ModelState.AddModelError(nameof(m.Code), "Không đổi mã của ngôn ngữ gốc / ngôn ngữ đã có bản dịch.");
            if (!m.IsActive && l.Code == Loc.DefaultCode) ModelState.AddModelError(nameof(m.IsActive), "Không ngừng ngôn ngữ mặc định.");
        }
        if (!ModelState.IsValid) { await FormData(); return View("Form", m); }

        if (l == null) { l = new Language(); Db.Languages.Add(l); }
        l.Code = m.Code; l.Name = m.Name.Trim(); l.Culture = m.Culture.Trim(); l.DateFormat = m.DateFormat.Trim();
        l.FallbackCode = string.IsNullOrWhiteSpace(m.FallbackCode) ? null : m.FallbackCode; l.SortOrder = m.SortOrder; l.IsActive = m.IsActive;
        await Db.SaveChangesAsync();

        // Ngôn ngữ mới: sao chép toàn bộ bản dịch từ ngôn ngữ có sẵn để chỉnh sửa
        var copied = 0;
        if (m.Id == 0 && !string.IsNullOrEmpty(copyFrom) && copyFrom != l.Code)
        {
            var src = await Db.Translations.AsNoTracking().Where(t => t.LanguageCode == copyFrom && t.Value != null && t.Value != "").ToListAsync();
            Db.Translations.AddRange(src.Select(t => new Translation { LanguageCode = l.Code, Key = t.Key, Value = t.Value, IsCustom = true }));
            Db.SuppressAudit = true;
            await Db.SaveChangesAsync();
            copied = src.Count;
        }
        await Loc.ReloadAsync();
        return BackWithSuccess(copied > 0 ? L.T("Đã lưu ngôn ngữ {0} – sao chép {1} câu dịch.", l.Name, copied) : L.T("Đã lưu ngôn ngữ {0}.", l.Name), nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.System)]
    public async Task<IActionResult> SetDefault(string code)
    {
        var l = await Db.Languages.FirstOrDefaultAsync(x => x.Code == code && x.IsActive);
        if (l == null) return NotFound();
        await Svc<SettingsService>().SetAsync(SettingsService.DefaultLanguage, l.Code);
        await Db.SaveChangesAsync();
        await Loc.ReloadAsync();
        return BackWithSuccess(L.T("Ngôn ngữ mặc định: {0}.", l.Name), nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.System)]
    public async Task<IActionResult> Delete(int id)
    {
        var l = await Db.Languages.FindAsync(id);
        if (l == null) return NotFound();
        if (l.Code == Localizer.Source || l.Code == Loc.DefaultCode) return BackWithError("Không xóa ngôn ngữ gốc / ngôn ngữ mặc định.", nameof(Index));
        if (await Db.Languages.AnyAsync(x => x.FallbackCode == l.Code)) return BackWithError("Ngôn ngữ đang được ngôn ngữ khác kế thừa.", nameof(Index));
        await Db.Translations.Where(t => t.LanguageCode == l.Code).ExecuteDeleteAsync();
        foreach (var u in await Db.Users.Where(u => u.Language == l.Code).ToListAsync()) u.Language = null;
        Db.Languages.Remove(l);
        await Db.SaveChangesAsync();
        await Loc.ReloadAsync();
        return BackWithSuccess(L.T("Đã xóa ngôn ngữ {0}.", l.Name), nameof(Index));
    }

    // ===================== Bản dịch =====================

    private IQueryable<string> AllKeysQuery() =>
        Db.TranslationKeys.Select(k => k.Key).Union(Db.Translations.Select(t => t.Key));

    /// <summary>Bảng dịch của một ngôn ngữ: tìm kiếm, lọc chưa dịch / đã sửa, phân trang</summary>
    [RequirePerm(Perm.System)]
    public async Task<IActionResult> Translations(string code, ListFilter f, string filter = "all")
    {
        var lang = await Db.Languages.AsNoTracking().FirstOrDefaultAsync(l => l.Code == code);
        if (lang == null) return NotFound();
        var rows = await BuildRows(lang);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var k = f.Q.Trim();
            rows = rows.Where(r => r.Key.Contains(k, StringComparison.OrdinalIgnoreCase) || (r.Value?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.Fallback?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }
        rows = filter switch
        {
            "missing" => rows.Where(r => string.IsNullOrEmpty(r.Value) && string.IsNullOrEmpty(r.Fallback)).ToList(),
            "custom" => rows.Where(r => r.IsCustom).ToList(),
            "own" => rows.Where(r => !string.IsNullOrEmpty(r.Value)).ToList(),
            _ => rows
        };
        if (f.PageSize < 50) f.PageSize = 50;
        var page = rows.Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToList();
        ViewBag.Lang = lang; ViewBag.Filter = f; ViewBag.Mode = filter;
        return View(new PagedList<TranslationRow>(page, rows.Count, f.Page, f.PageSize));
    }

    private async Task<List<TranslationRow>> BuildRows(Language lang)
    {
        var keys = await AllKeysQuery().ToListAsync();
        var own = await Db.Translations.AsNoTracking().Where(t => t.LanguageCode == lang.Code)
            .ToDictionaryAsync(t => t.Key, t => t, StringComparer.Ordinal);
        // Giá trị kế thừa (ngôn ngữ vùng miền → ngôn ngữ gốc)
        var fallback = new Dictionary<string, string>(StringComparer.Ordinal);
        var langs = await Db.Languages.AsNoTracking().ToDictionaryAsync(l => l.Code, l => l.FallbackCode);
        var chain = new List<string>();
        for (var c = lang.FallbackCode; c != null && !chain.Contains(c) && c != lang.Code; c = langs.GetValueOrDefault(c)) chain.Add(c);
        foreach (var c in Enumerable.Reverse(chain))
            foreach (var t in await Db.Translations.AsNoTracking().Where(t => t.LanguageCode == c && t.Value != null && t.Value != "").Select(t => new { t.Key, t.Value }).ToListAsync())
                fallback[t.Key] = t.Value!;
        return keys.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.CurrentCulture).Select(k =>
        {
            own.TryGetValue(k, out var t);
            return new TranslationRow { Key = k, Value = t?.Value, IsCustom = t?.IsCustom ?? false, Fallback = fallback.GetValueOrDefault(k) };
        }).ToList();
    }

    /// <summary>Lưu các câu đã sửa trên trang (để trống = dùng bản kế thừa / văn bản gốc)</summary>
    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.System)]
    public async Task<IActionResult> SaveTranslations(string code, List<TranslationRow> rows, string? q, string filter = "all", int page = 1)
    {
        if (!await Db.Languages.AnyAsync(l => l.Code == code)) return NotFound();
        var changed = await Upsert(code, rows.Where(r => !string.IsNullOrEmpty(r.Key) && (r.Value ?? "") != (r.Original ?? "")).Select(r => (r.Key, r.Value)));
        await Loc.ReloadAsync();
        TempData["Success"] = L.T("Đã lưu {0} câu dịch.", changed);
        return RedirectToAction(nameof(Translations), new { code, q, filter, page });
    }

    private async Task<int> Upsert(string code, IEnumerable<(string Key, string? Value)> items)
    {
        var list = items.Select(i => (Key: i.Key.Trim(), i.Value)).Where(i => i.Key.Length is > 0 and <= 800).GroupBy(i => i.Key).Select(g => g.Last()).ToList();
        if (list.Count == 0) return 0;
        var keys = list.Select(i => i.Key).ToList();
        var existing = (await Db.Translations.Where(t => t.LanguageCode == code).ToListAsync())
            .Where(t => keys.Contains(t.Key)).ToDictionary(t => t.Key, StringComparer.Ordinal);
        foreach (var (key, value) in list)
        {
            var v = value?.Trim();
            if (v?.Length > 2000) v = v[..2000];
            existing.TryGetValue(key, out var row);
            if (string.IsNullOrEmpty(v)) { if (row != null) Db.Translations.Remove(row); continue; }
            if (row == null) Db.Translations.Add(new Translation { LanguageCode = code, Key = key, Value = v, IsCustom = true });
            else { row.Value = v; row.IsCustom = true; row.UpdatedAt = DateTime.Now; }
        }
        var known = await Db.TranslationKeys.Where(k => keys.Contains(k.Key)).Select(k => k.Key).ToListAsync();
        Db.TranslationKeys.AddRange(keys.Except(known).Distinct().Select(k => new TranslationKey { Key = k }));
        await Db.SaveChangesAsync();
        return list.Count;
    }

    /// <summary>Xuất bảng dịch ra CSV (UTF-8) để dịch / hiệu chỉnh bên ngoài</summary>
    [RequirePerm(Perm.System)]
    public async Task<IActionResult> Export(string code)
    {
        var lang = await Db.Languages.AsNoTracking().FirstOrDefaultAsync(l => l.Code == code);
        if (lang == null) return NotFound();
        var rows = await BuildRows(lang);
        var csv = Spreadsheet.ToCsv(new[] { "Key (vi)", "Value", "Inherited" }, rows.Select(r => (IReadOnlyList<object?>)new object?[] { r.Key, r.Value, r.Fallback }));
        return File(csv, "text/csv", $"translations_{lang.Code}_{DateTime.Now:yyyyMMdd}.csv");
    }

    /// <summary>Nhập CSV (cột 1: văn bản gốc, cột 2: bản dịch) – ghi đè các câu có trong file</summary>
    [HttpPost, ValidateAntiForgeryToken, RequirePerm(Perm.System)]
    public async Task<IActionResult> Import(string code, IFormFile? file)
    {
        if (!await Db.Languages.AnyAsync(l => l.Code == code)) return NotFound();
        if (file == null || file.Length == 0) return BackWithError("Chọn file CSV.", nameof(Translations), new { code });
        string text;
        using (var r = new StreamReader(file.OpenReadStream(), Encoding.UTF8, true)) text = await r.ReadToEndAsync();
        var records = ParseCsv(text).Skip(1).Where(c => c.Count >= 2 && !string.IsNullOrWhiteSpace(c[0]) && !string.IsNullOrWhiteSpace(c[1]))
            .Select(c => (Unguard(c[0]), (string?)Unguard(c[1]))).ToList();
        var n = await Upsert(code, records);
        await Loc.ReloadAsync();
        return BackWithSuccess(L.T("Đã nhập {0} câu dịch.", n), nameof(Translations), new { code });
    }

    /// <summary>Bỏ dấu ' do bước xuất CSV thêm trước ô bắt đầu bằng = + - @ (chống chèn công thức)</summary>
    private static string Unguard(string s) => s.Length > 1 && s[0] == '\'' && "=+-@".Contains(s[1]) ? s[1..] : s;

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>(); var cell = new StringBuilder(); var q = false;
        var sep = text.Split('\n').FirstOrDefault()?.Count(c => c == ';') > text.Split('\n').FirstOrDefault()?.Count(c => c == ',') ? ';' : ',';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (q)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') q = false;
                else cell.Append(c);
            }
            else if (c == '"') q = true;
            else if (c == sep) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n') { row.Add(cell.ToString().TrimEnd('\r')); cell.Clear(); rows.Add(row); row = new(); }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString().TrimEnd('\r')); rows.Add(row); }
        return rows;
    }
}
