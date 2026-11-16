using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PetHospital.Web.Data;
using PetHospital.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace PetHospital.Web.Infrastructure;

/// <summary>Ngôn ngữ đang dùng cho yêu cầu hiện tại</summary>
public sealed class LangInfo
{
    public string Code { get; init; } = Localizer.Source;
    public string Name { get; init; } = "Tiếng Việt";
    public CultureInfo Culture { get; init; } = CultureInfo.GetCultureInfo("vi-VN");
    public string DateFormat { get; init; } = "dd/MM/yyyy";
    public string? Fallback { get; init; }
    public bool IsActive { get; init; } = true;
    public int SortOrder { get; init; }
    /// <summary>Mã ISO 2 ký tự cho thuộc tính lang của trang (vi, en, ko)</summary>
    public string HtmlLang => Code.Split('-')[0].ToLowerInvariant();
}

/// <summary>Ngôn ngữ của luồng xử lý hiện tại (đặt bởi LanguageMiddleware)</summary>
public static class LangScope
{
    public const string Cookie = "pet.lang";
    private static readonly AsyncLocal<LangInfo?> _current = new();
    public static LangInfo? Current { get => _current.Value; set => _current.Value = value; }
}

/// <summary>
/// Bộ dịch giao diện. Văn bản gốc (khóa) là tiếng Việt; bản dịch lưu ở bảng Translations, nạp vào bộ nhớ.
/// - T(key): dịch đúng khóa; khóa có {0}, {1}… là mẫu câu.
/// - Text(s): dịch câu bất kỳ – đúng khóa → khớp mẫu câu (thông báo có tham số) → tiền tố (phần còn lại là mã / tên).
/// - Html(literal): dịch các đoạn chữ và thuộc tính (title, placeholder…) trong HTML tĩnh của view.
/// Văn bản chưa có trong danh sách được tự ghi nhận vào bảng TranslationKeys để quản trị dịch bổ sung.
/// </summary>
public sealed class Localizer
{
    public const string Source = "vi";
    public static Localizer? Instance { get; private set; }

    private static readonly Regex VnChar = new("[À-ỹđĐ]", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);
    private static readonly Regex TranslatableAttr = new(
        @"(\s(?:title|placeholder|aria-label|data-confirm|alt|label|data-label-collapse|data-label-expand)="")([^""<>@]*)("")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<Localizer> _log;
    private volatile Snapshot _snap = new();
    private readonly ConcurrentDictionary<string, byte> _pending = new();

    public Localizer(IServiceScopeFactory scopes, ILogger<Localizer> log)
    {
        _scopes = scopes; _log = log;
        Instance = this;
    }

    // ===================== Dữ liệu =====================

    private sealed class Compiled
    {
        public Dictionary<string, string> Map = new(StringComparer.Ordinal);
        public List<(Regex Re, string Target, string Key)> Templates = new();
        public ConcurrentDictionary<string, string> HtmlCache = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, string> TextCache = new(StringComparer.Ordinal);
    }

    private sealed class Snapshot
    {
        public Dictionary<string, LangInfo> Langs = new(StringComparer.OrdinalIgnoreCase) { [Source] = new LangInfo() };
        public Dictionary<string, Compiled> ByLang = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, byte> Known = new(StringComparer.Ordinal);
        public string DefaultCode = Source;
    }

    public IReadOnlyCollection<LangInfo> Languages => _snap.Langs.Values.OrderBy(l => l.SortOrder).ThenBy(l => l.Name).ToList();
    public IEnumerable<LangInfo> ActiveLanguages => Languages.Where(l => l.IsActive);
    public string DefaultCode => _snap.DefaultCode;
    public LangInfo? Find(string? code) => code != null && _snap.Langs.TryGetValue(code, out var l) ? l : null;
    public LangInfo Default => Find(_snap.DefaultCode) ?? _snap.Langs[Source];

    /// <summary>Nạp lại ngôn ngữ và bản dịch từ database (sau khi chỉnh sửa)</summary>
    public async Task ReloadAsync()
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetDbContext>();
        var langs = await db.Languages.AsNoTracking().ToListAsync();
        var rows = await db.Translations.AsNoTracking().Where(t => t.Value != null && t.Value != "").Select(t => new { t.LanguageCode, t.Key, t.Value }).ToListAsync();
        var keys = await db.TranslationKeys.AsNoTracking().Select(k => k.Key).ToListAsync();
        var def = await db.SystemSettings.AsNoTracking().Where(s => s.Key == SettingsService.DefaultLanguage).Select(s => s.Value).FirstOrDefaultAsync();

        var snap = new Snapshot();
        foreach (var l in langs)
        {
            CultureInfo culture;
            try { culture = CultureInfo.GetCultureInfo(l.Culture); } catch (CultureNotFoundException) { culture = CultureInfo.GetCultureInfo("vi-VN"); }
            snap.Langs[l.Code] = new LangInfo
            {
                Code = l.Code, Name = l.Name, Culture = culture, DateFormat = string.IsNullOrWhiteSpace(l.DateFormat) ? "dd/MM/yyyy" : l.DateFormat,
                Fallback = string.IsNullOrWhiteSpace(l.FallbackCode) ? null : l.FallbackCode, IsActive = l.IsActive, SortOrder = l.SortOrder
            };
        }
        if (!snap.Langs.ContainsKey(Source)) snap.Langs[Source] = new LangInfo();
        var raw = rows.GroupBy(r => r.LanguageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.First().Value!, StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);
        foreach (var code in snap.Langs.Keys)
        {
            // Chuỗi kế thừa: ngôn ngữ gốc trước, ngôn ngữ vùng miền ghi đè sau
            var chain = new List<string>();
            for (var c = code; c != null && !chain.Contains(c, StringComparer.OrdinalIgnoreCase); c = snap.Langs.TryGetValue(c, out var li) ? li.Fallback : null)
                chain.Insert(0, c);
            var comp = new Compiled();
            foreach (var c in chain)
                if (raw.TryGetValue(c, out var dict))
                    foreach (var (k, v) in dict) comp.Map[k] = v;
            foreach (var (k, v) in comp.Map)
            {
                if (!k.Contains('{') || !Placeholder.IsMatch(k)) continue;
                var pattern = "^" + Placeholder.Replace(Regex.Escape(k).Replace(@"\{", "{"), m => $"(?<g{m.Groups[1].Value}>.+?)") + "$";
                try { comp.Templates.Add((new Regex(pattern, RegexOptions.Singleline), v, k)); } catch (ArgumentException) { }
            }
            // Mẫu dài (cụ thể) thử trước
            comp.Templates.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            snap.ByLang[code] = comp;
        }
        foreach (var k in keys) snap.Known.TryAdd(k, 0);
        foreach (var dict in raw.Values) foreach (var k in dict.Keys) snap.Known.TryAdd(k, 0);
        snap.DefaultCode = def != null && snap.Langs.ContainsKey(def) ? def : Source;
        _snap = snap;
    }

    // ===================== Dịch =====================

    private Compiled? Current(out LangInfo lang)
    {
        lang = LangScope.Current ?? Default;
        var s = _snap;
        if (!s.ByLang.TryGetValue(lang.Code, out var comp)) return null;
        // Tiếng Việt gốc không có câu sửa đổi: không cần xử lý
        return comp.Map.Count == 0 && lang.Code == Source ? null : comp;
    }

    /// <summary>Đang hiển thị ngôn ngữ cần dịch (khác tiếng Việt gốc chưa sửa đổi)</summary>
    public bool IsTranslating => Current(out _) != null;

    /// <summary>Dịch đúng khóa (khóa là văn bản tiếng Việt, mẫu câu dùng {0}, {1}…)</summary>
    public string T(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        var comp = Current(out var lang);
        if (comp == null) return key;
        if (comp.Map.TryGetValue(key, out var v)) return v;
        Collect(key, lang);
        return key;
    }

    public string T(string key, params object?[] args)
    {
        var f = T(key);
        try { return string.Format(f, args.Select(a => a is string s ? Text(s) : a).ToArray()); }
        catch (FormatException) { return string.Format(key, args); }
    }

    /// <summary>Tra bản dịch không ghi nhận khóa (khóa theo ngữ cảnh)</summary>
    public bool TryGet(string key, out string value)
    {
        value = key;
        var comp = Current(out _);
        if (comp == null || !comp.Map.TryGetValue(key, out var v)) return false;
        value = v;
        return true;
    }

    private static readonly char[] Punct = { ' ', '·', '–', '-', '(', ')', '[', ']', ':', ';', ',', '.', '…', '"' };

    /// <summary>
    /// Chỉ dịch khi văn bản (bỏ dấu câu bao quanh) khớp đúng một câu đã có bản dịch; không ghi nhận câu mới.
    /// Dùng cho giá trị in ra từ biểu thức (có thể là dữ liệu người dùng).
    /// </summary>
    public string Known(string s)
    {
        // Không yêu cầu có dấu: đơn vị tính không dấu (Bao, Lon, Chai) vẫn dịch được
        if (s.Length == 0 || !s.Any(char.IsLetter)) return s;
        var comp = Current(out _);
        if (comp == null) return s;
        var t = s.Trim();
        if (comp.Map.TryGetValue(t, out var v)) return s.Replace(t, v);
        var core = t.Trim(Punct);
        if (core.Length > 0 && core != t && comp.Map.TryGetValue(core, out var cv)) return s.Replace(core, cv);
        return s;
    }

    /// <summary>Dịch câu có thể chứa tham số (thông báo, tiêu đề): đúng khóa → mẫu câu → tiền tố / ghép bằng " · "</summary>
    public string Text(string? s) => Text(s, true);

    /// <param name="collect">false: không ghi nhận câu chưa dịch (giá trị có thể là dữ liệu người dùng)</param>
    public string Text(string? s, bool collect)
    {
        if (string.IsNullOrWhiteSpace(s)) return s ?? "";
        var comp = Current(out var lang);
        if (comp == null) return s;
        if (comp.TextCache.TryGetValue(s, out var cached)) return cached;
        var r = TextCore(comp, s.Trim(), lang, collect ? 0 : 1);
        if (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1])) r = s[..(s.Length - s.TrimStart().Length)] + r + s[s.TrimEnd().Length..];
        if (comp.TextCache.Count < 20000) comp.TextCache[s] = r;
        return r;
    }

    private string TextCore(Compiled comp, string s, LangInfo lang, int depth)
    {
        if (comp.Map.TryGetValue(s, out var v)) return v;
        if (!VnChar.IsMatch(s) || depth > 3) return s;
        foreach (var (re, target, _) in comp.Templates)
        {
            var m = re.Match(s);
            if (!m.Success) continue;
            // Tham số là đoạn văn dài tiếng Việt chưa dịch: mẫu câu khớp nhầm (VD "Sửa {0}" với một câu dài) → bỏ qua
            var groups = m.Groups.Cast<Group>().Skip(1).Where(g => g.Success)
                .Select(g => (g.Name, Value: g.Value, Tr: TextCore(comp, g.Value, lang, depth + 1))).ToList();
            if (groups.Any(g => g.Value.Length > 40 && g.Tr == g.Value && VnChar.IsMatch(g.Value))) continue;
            return Placeholder.Replace(target, x =>
                groups.FirstOrDefault(g => g.Name == "g" + x.Groups[1].Value) is { Name: not null } g ? g.Tr : x.Value);
        }
        // Nhiều câu nối tiếp (thông báo ghép): dịch từng câu
        var sentences = Regex.Split(s, @"(?<=[.!?])\s+(?=\p{Lu})");
        if (sentences.Length > 1)
        {
            var joined = string.Join(" ", sentences.Select(p => TextCore(comp, p, lang, depth + 1)));
            if (joined != s) return joined;
        }
        // Ghép nhiều phần bằng " · " / " – "
        foreach (var sep in new[] { " · ", " – " })
            if (s.Contains(sep))
            {
                var joined = string.Join(sep, s.Split(sep).Select(p => TextCore(comp, p.Trim(), lang, depth + 1)));
                if (joined != s) return joined;
            }
        // Tiền tố đã dịch + phần còn lại không phải tiếng Việt (mã chứng từ, số, tên riêng không dấu)
        var words = s.Split(' ');
        for (var n = words.Length - 1; n >= 1; n--)
        {
            var head = string.Join(' ', words.Take(n));
            var tail = string.Join(' ', words.Skip(n));
            if (comp.Map.TryGetValue(head, out var hv) && !VnChar.IsMatch(tail)) return hv + " " + tail;
            if (comp.Map.TryGetValue(head.TrimEnd(':'), out var hv2) && head.EndsWith(':') && !VnChar.IsMatch(tail)) return hv2 + ": " + tail;
        }
        if (depth == 0) Collect(s, lang);
        return s;
    }

    /// <summary>Dịch đoạn HTML tĩnh: chữ giữa các thẻ + thuộc tính hiển thị. inScript: đang ở trong &lt;script&gt; / &lt;style&gt;.</summary>
    public string Html(string literal, ref bool inScript, bool decodeEntities = false)
    {
        var comp = Current(out var lang);
        if (comp == null || string.IsNullOrEmpty(literal)) return literal;
        var hasScriptTag = literal.Contains("<script", StringComparison.OrdinalIgnoreCase) || literal.Contains("</script", StringComparison.OrdinalIgnoreCase)
            || literal.Contains("<style", StringComparison.OrdinalIgnoreCase) || literal.Contains("</style", StringComparison.OrdinalIgnoreCase);
        if (inScript && !hasScriptTag) return literal;
        if (!hasScriptTag && !decodeEntities && comp.HtmlCache.TryGetValue(literal, out var hit)) return hit;

        var sb = new StringBuilder(literal.Length + 16);
        var i = 0;
        // Đoạn đầu nằm giữa một thẻ (sau biểu thức @ trong thuộc tính): chép tới dấu '>' đầu tiên
        var firstLt = literal.IndexOf('<'); var firstGt = literal.IndexOf('>');
        if (firstGt >= 0 && (firstLt < 0 || firstGt < firstLt) && !inScript)
        {
            sb.Append(TranslateAttrs(comp, literal[..(firstGt + 1)], lang));
            i = firstGt + 1;
        }
        while (i < literal.Length)
        {
            var lt = literal.IndexOf('<', i);
            var text = lt < 0 ? literal[i..] : literal[i..lt];
            if (text.Length > 0) sb.Append(inScript ? text : TranslateText(comp, text, lang, decodeEntities));
            if (lt < 0) break;
            var gt = literal.IndexOf('>', lt);
            if (gt < 0) { sb.Append(inScript ? literal[lt..] : TranslateAttrs(comp, literal[lt..], lang)); break; }
            var tag = literal[lt..(gt + 1)];
            if (Regex.IsMatch(tag, @"^<(script|style)\b", RegexOptions.IgnoreCase)) inScript = true;
            else if (Regex.IsMatch(tag, @"^</(script|style)\s*>", RegexOptions.IgnoreCase)) inScript = false;
            sb.Append(inScript ? tag : TranslateAttrs(comp, tag, lang));
            i = gt + 1;
        }
        var result = sb.ToString();
        if (!hasScriptTag && !decodeEntities && comp.HtmlCache.Count < 50000) comp.HtmlCache[literal] = result;
        return result;
    }

    private string TranslateText(Compiled comp, string text, LangInfo lang, bool decode)
    {
        var t = text.Trim();
        if (t.Length == 0 || !t.Any(char.IsLetter)) return text;
        // Khóa dịch không phụ thuộc xuống dòng / thụt lề trong file view
        var plain = Regex.Replace(decode ? WebUtility.HtmlDecode(t) : t, @"\s+", " ");
        var tr = TextCore(comp, plain, lang, 0);
        if (ReferenceEquals(tr, plain) || tr == plain) return text;
        // Bản dịch do người dùng nhập: mã hóa HTML (giữ nguyên ý nghĩa các entity có sẵn như &amp;amp;, &amp;laquo;)
        tr = WebUtility.HtmlEncode(WebUtility.HtmlDecode(tr));
        var start = text.IndexOf(t, StringComparison.Ordinal);
        return text[..start] + tr + text[(start + t.Length)..];
    }

    private string TranslateAttrs(Compiled comp, string tag, LangInfo lang) =>
        !tag.Contains('=') ? tag : TranslatableAttr.Replace(tag, m =>
        {
            var v = m.Groups[2].Value;
            if (v.Length == 0 || !v.Any(char.IsLetter)) return m.Value;
            var tr = TextCore(comp, WebUtility.HtmlDecode(v), lang, 0);
            return m.Groups[1].Value + WebUtility.HtmlEncode(tr) + m.Groups[3].Value;
        });

    // ===================== Ghi nhận văn bản chưa dịch =====================

    private void Collect(string key, LangInfo lang)
    {
        if (lang.Code == Source || key.Length > 800 || !VnChar.IsMatch(key)) return;
        if (_snap.Known.ContainsKey(key)) return;
        if (_pending.Count < 5000) _pending.TryAdd(key, 0);
    }

    /// <summary>Lưu các văn bản mới gặp vào bảng TranslationKeys (gọi định kỳ)</summary>
    public async Task FlushPendingAsync()
    {
        if (_pending.IsEmpty) return;
        var batch = _pending.Keys.Take(500).ToList();
        foreach (var k in batch) _pending.TryRemove(k, out _);
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PetDbContext>();
            var exists = await db.TranslationKeys.Where(k => batch.Contains(k.Key)).Select(k => k.Key).ToListAsync();
            var add = batch.Except(exists).ToList();
            if (add.Count > 0)
            {
                db.TranslationKeys.AddRange(add.Select(k => new TranslationKey { Key = k }));
                await db.SaveChangesAsync();
            }
            foreach (var k in batch) _snap.Known.TryAdd(k, 0);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Không lưu được văn bản cần dịch"); }
    }

    // ===================== Bản dịch có sẵn (i18n/*.json) =====================

    /// <summary>Ngôn ngữ cài sẵn khi khởi tạo</summary>
    public static readonly (string Code, string Name, string Culture, string DateFormat, int Sort)[] BuiltIn =
    {
        ("vi", "Tiếng Việt", "vi-VN", "dd/MM/yyyy", 1),
        ("en", "English", "en-US", "dd/MM/yyyy", 2),
        ("ko", "한국어", "ko-KR", "yyyy-MM-dd", 3)
    };

    /// <summary>
    /// Tạo ngôn ngữ cài sẵn (nếu chưa có) và nạp bản dịch từ i18n/{mã}.json:
    /// thêm câu còn thiếu, cập nhật câu chưa bị người dùng sửa; câu đã sửa (IsCustom) giữ nguyên.
    /// </summary>
    public static async Task SeedAsync(PetDbContext db, string contentRoot, ILogger? log = null)
    {
        var existing = await db.Languages.Select(l => l.Code).ToListAsync();
        foreach (var b in BuiltIn.Where(b => !existing.Contains(b.Code, StringComparer.OrdinalIgnoreCase)))
            db.Languages.Add(new Language { Code = b.Code, Name = b.Name, Culture = b.Culture, DateFormat = b.DateFormat, SortOrder = b.Sort });
        await db.SaveChangesAsync();

        var dir = Path.Combine(contentRoot, "i18n");
        if (!Directory.Exists(dir)) return;
        var allKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            // en.json hoặc chia nhiều phần en.1.json, en.2.json…
            var code = Path.GetFileNameWithoutExtension(file).Split('.')[0];
            if (!await db.Languages.AnyAsync(l => l.Code == code)) continue;
            Dictionary<string, string>? dict;
            try { dict = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(file)); }
            catch (JsonException ex) { log?.LogWarning(ex, "Lỗi đọc {File}", file); continue; }
            if (dict == null) continue;
            var rows = await db.Translations.Where(t => t.LanguageCode == code).ToDictionaryAsync(t => t.Key, StringComparer.Ordinal);
            int added = 0, updated = 0;
            // SQL Server bỏ qua khoảng trắng cuối khi so sánh: chuẩn hóa khóa, bỏ trùng
            foreach (var (k, v) in dict.Select(x => (x.Key.Trim(), x.Value)).GroupBy(x => x.Item1).Select(g => g.Last()))
            {
                if (string.IsNullOrWhiteSpace(k) || k.Length > 800 || string.IsNullOrEmpty(v)) continue;
                allKeys.Add(k);
                if (!rows.TryGetValue(k, out var row))
                {
                    db.Translations.Add(new Translation { LanguageCode = code, Key = k, Value = v.Length > 2000 ? v[..2000] : v });
                    added++;
                }
                else if (!row.IsCustom && row.Value != v) { row.Value = v; row.UpdatedAt = DateTime.Now; updated++; }
            }
            await db.SaveChangesAsync();
            if (added + updated > 0) log?.LogInformation("Bản dịch {Code}: thêm {Added}, cập nhật {Updated}", code, added, updated);
        }
        var known = new HashSet<string>(await db.TranslationKeys.Select(k => k.Key).ToListAsync(), StringComparer.Ordinal);
        var newKeys = allKeys.Where(k => !known.Contains(k)).ToList();
        if (newKeys.Count > 0)
        {
            db.TranslationKeys.AddRange(newKeys.Select(k => new TranslationKey { Key = k }));
            await db.SaveChangesAsync();
        }
    }
}

/// <summary>Lưu định kỳ văn bản mới gặp cần dịch</summary>
public sealed class TranslationKeyCollector : BackgroundService
{
    private readonly Localizer _l;
    public TranslationKeyCollector(Localizer l) => _l = l;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch (TaskCanceledException) { break; }
            await _l.FlushPendingAsync();
        }
        await _l.FlushPendingAsync();
    }
}

/// <summary>Chọn ngôn ngữ cho mỗi yêu cầu: cookie → mặc định hệ thống</summary>
public sealed class LanguageMiddleware
{
    private readonly RequestDelegate _next;
    public LanguageMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx, Localizer l)
    {
        var lang = l.Find(ctx.Request.Cookies[LangScope.Cookie]);
        if (lang == null || !lang.IsActive) lang = l.Default;
        LangScope.Current = lang;
        CultureInfo.CurrentUICulture = lang.Culture;
        await _next(ctx);
    }
}

/// <summary>Dịch tên trường (Display) và thông báo kiểm tra dữ liệu (ErrorMessage) qua bộ dịch</summary>
public sealed class DbStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly DbStringLocalizer _instance = new();
    public IStringLocalizer Create(Type resourceSource) => _instance;
    public IStringLocalizer Create(string baseName, string location) => _instance;
}

public sealed class DbStringLocalizer : IStringLocalizer
{
    public LocalizedString this[string name] => new(name, Localizer.Instance?.T(name) ?? name, false);
    public LocalizedString this[string name, params object[] arguments] => new(name, Localizer.Instance?.T(name, arguments) ?? string.Format(name, arguments), false);
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
}

/// <summary>Truy cập nhanh bộ dịch từ mã C# (static)</summary>
public static class L
{
    public static string T(string key) => Localizer.Instance?.T(key) ?? key;
    public static string T(string key, params object?[] args) => Localizer.Instance?.T(key, args) ?? string.Format(key, args);
    public static string Text(string? s) => Localizer.Instance?.Text(s) ?? s ?? "";
}
