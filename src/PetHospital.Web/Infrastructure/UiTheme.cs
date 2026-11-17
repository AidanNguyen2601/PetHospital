using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PetHospital.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Infrastructure;

/// <summary>
/// Cấu hình giao diện: phông chữ, cỡ chữ, màu chủ đạo, vị trí menu (trái / phải / trên / dưới), kiểu màu menu.
/// Mặc định toàn hệ thống lưu ở SystemSettings[Ui.Theme]; mỗi người dùng có thể chọn riêng (Users.UiTheme).
/// Áp dụng bằng biến CSS (--accent, --app-font…) và thuộc tính data-menu / data-menu-style trên thẻ &lt;html&gt;.
/// </summary>
public sealed class UiTheme
{
    public string Font { get; set; } = "system";
    public int FontSize { get; set; } = 14;
    public string Accent { get; set; } = DefaultAccent;
    /// <summary>left | right | top | bottom</summary>
    public string Menu { get; set; } = "left";
    /// <summary>dark | light | accent</summary>
    public string MenuStyle { get; set; } = "dark";

    public const string DefaultAccent = "#0f9d8a";
    private const string Fallback = "\"Malgun Gothic\", \"Apple SD Gothic Neo\", sans-serif";   // chữ Hàn khi phông chính thiếu

    /// <summary>Phông chữ: mã, tên, CSS font-family, tên họ phông trên Google Fonts (null = phông có sẵn trong máy)</summary>
    public static readonly (string Code, string Name, string Stack, string? Google)[] Fonts =
    {
        ("system", "Segoe UI (mặc định)", $"\"Segoe UI\", Roboto, Arial, {Fallback}", null),
        ("arial", "Arial", $"Arial, Helvetica, {Fallback}", null),
        ("tahoma", "Tahoma", $"Tahoma, Verdana, {Fallback}", null),
        ("roboto", "Roboto", $"Roboto, \"Segoe UI\", Arial, {Fallback}", "Roboto"),
        ("inter", "Inter", $"Inter, \"Segoe UI\", Arial, {Fallback}", "Inter"),
        ("bevietnam", "Be Vietnam Pro", $"\"Be Vietnam Pro\", \"Segoe UI\", Arial, {Fallback}", "Be Vietnam Pro"),
        ("nunito", "Nunito", $"Nunito, \"Segoe UI\", Arial, {Fallback}", "Nunito"),
        ("opensans", "Open Sans", $"\"Open Sans\", \"Segoe UI\", Arial, {Fallback}", "Open Sans"),
        ("montserrat", "Montserrat", $"Montserrat, \"Segoe UI\", Arial, {Fallback}", "Montserrat"),
        ("notosans", "Noto Sans", $"\"Noto Sans\", \"Segoe UI\", Arial, {Fallback}", "Noto Sans"),
        ("times", "Times New Roman", "\"Times New Roman\", Times, \"Batang\", serif", null),
    };

    public static readonly int[] FontSizes = { 13, 14, 15, 16 };

    public static readonly (string Hex, string Name)[] Accents =
    {
        (DefaultAccent, "Xanh ngọc"), ("#2563eb", "Xanh dương"), ("#4f46e5", "Chàm"), ("#7c3aed", "Tím"),
        ("#db2777", "Hồng"), ("#dc2626", "Đỏ"), ("#ea580c", "Cam"), ("#ca8a04", "Vàng đậm"),
        ("#16a34a", "Xanh lá"), ("#0891b2", "Xanh biển"), ("#475569", "Xám"),
    };

    public static readonly (string Code, string Name)[] MenuPositions =
        { ("left", "Bên trái"), ("right", "Bên phải"), ("top", "Phía trên"), ("bottom", "Phía dưới") };

    public static readonly (string Code, string Name)[] MenuStyles =
        { ("dark", "Tối"), ("light", "Sáng"), ("accent", "Theo màu chủ đạo") };

    private static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    /// <summary>Đưa giá trị lạ về mặc định (dữ liệu nhập tay / phiên bản cũ)</summary>
    public UiTheme Normalize()
    {
        if (Fonts.All(f => f.Code != Font)) Font = "system";
        if (!FontSizes.Contains(FontSize)) FontSize = 14;
        Accent = HexColor.IsMatch(Accent ?? "") ? Accent!.ToLowerInvariant() : DefaultAccent;
        if (MenuPositions.All(m => m.Code != Menu)) Menu = "left";
        if (MenuStyles.All(m => m.Code != MenuStyle)) MenuStyle = "dark";
        return this;
    }

    [JsonIgnore] public string FontStack => Fonts.First(f => f.Code == Font).Stack;

    /// <summary>Đường dẫn Google Fonts (cần Internet; mất mạng thì trình duyệt tự dùng phông dự phòng)</summary>
    [JsonIgnore] public string? GoogleFontUrl => GoogleUrl(Fonts.First(f => f.Code == Font).Google);

    public static string? GoogleUrl(params string?[] families)
    {
        var fams = families.Where(f => !string.IsNullOrEmpty(f)).Select(f => "family=" + f!.Replace(' ', '+') + ":wght@300;400;500;600;700").ToList();
        return fams.Count == 0 ? null : "https://fonts.googleapis.com/css2?" + string.Join("&", fams) + "&display=swap";
    }

    public static string Rgb(string hex) =>
        string.Join(",", Enumerable.Range(0, 3).Select(i => int.Parse(hex.AsSpan(1 + i * 2, 2), NumberStyles.HexNumber)));

    /// <summary>Biến CSS đặt trên thẻ &lt;html style="…"&gt;</summary>
    [JsonIgnore] public string CssVars => $"--accent:{Accent};--accent-rgb:{Rgb(Accent)};--app-font:{FontStack};--app-font-size:{FontSize}px";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static UiTheme? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<UiTheme>(json, Json)?.Normalize(); }
        catch (JsonException) { return null; }
    }

    public UiTheme Clone() => (UiTheme)MemberwiseClone();
}

/// <summary>Đọc / lưu cấu hình giao diện (mặc định hệ thống + riêng từng người dùng)</summary>
public class ThemeService
{
    public const string SettingKey = "Ui.Theme";

    private readonly PetDbContext _db;
    private readonly CurrentUser _me;
    private UiTheme? _system, _current;

    public ThemeService(PetDbContext db, CurrentUser me) { _db = db; _me = me; }

    /// <summary>Giao diện mặc định toàn hệ thống</summary>
    public async Task<UiTheme> SystemAsync() => _system ??=
        UiTheme.Parse(await _db.SystemSettings.AsNoTracking().Where(s => s.Key == SettingKey).Select(s => s.Value).FirstOrDefaultAsync())
        ?? new UiTheme();

    /// <summary>Giao diện riêng của người dùng (null = dùng mặc định hệ thống)</summary>
    public async Task<UiTheme?> UserAsync(int userId) =>
        userId <= 0 ? null : UiTheme.Parse(await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.UiTheme).FirstOrDefaultAsync());

    /// <summary>Giao diện đang áp dụng cho người dùng hiện tại</summary>
    public async Task<UiTheme> CurrentAsync() => _current ??=
        (_me.IsAuthenticated ? await UserAsync(_me.Id) : null) ?? await SystemAsync();
}
