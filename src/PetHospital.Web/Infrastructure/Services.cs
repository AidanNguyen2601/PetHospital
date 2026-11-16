using PetHospital.Web.Data;
using PetHospital.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Infrastructure;

/// <summary>Đánh số chứng từ: TIỀN TỐ + yyMM + "-" + số thứ tự 4 chữ số (VD: HD2611-0007)</summary>
public static class Codes
{
    public static async Task<string> NextAsync(IQueryable<string> codes, string prefix, DateTime? date = null)
    {
        var head = $"{prefix}{(date ?? DateTime.Today):yyMM}-";
        var last = await codes.Where(c => c.StartsWith(head)).OrderByDescending(c => c).FirstOrDefaultAsync();
        var n = last != null && int.TryParse(last[head.Length..], out var v) ? v + 1 : 1;
        return head + n.ToString("0000");
    }

    /// <summary>Mã danh bạ không theo tháng: KH00001, TC00001</summary>
    public static async Task<string> NextPlainAsync(IQueryable<string> codes, string prefix, int digits = 5)
    {
        var list = await codes.Where(c => c.StartsWith(prefix)).ToListAsync();
        var max = list.Select(c => int.TryParse(c[prefix.Length..], out var v) ? v : 0).DefaultIfEmpty(0).Max();
        return prefix + (max + 1).ToString(new string('0', digits));
    }
}

/// <summary>Thông tin phòng khám và tham số (lưu bảng SystemSettings)</summary>
public class SettingsService
{
    public const string ClinicName = "Clinic.Name";
    public const string ClinicAddress = "Clinic.Address";
    public const string ClinicPhone = "Clinic.Phone";
    public const string ClinicEmail = "Clinic.Email";
    public const string ClinicTaxCode = "Clinic.TaxCode";
    public const string InvoiceFooter = "Invoice.Footer";
    public const string VaccineRemindDays = "Remind.VaccineDays";
    public const string ExpiryWarnDays = "Stock.ExpiryWarnDays";

    public static readonly (string Key, string Label, string Default)[] All =
    {
        (ClinicName, "Tên phòng khám", "Phòng khám Thú y PetCare"),
        (ClinicAddress, "Địa chỉ", "123 Nguyễn Văn Linh, Quận 7, TP. Hồ Chí Minh"),
        (ClinicPhone, "Điện thoại", "028 3888 9999"),
        (ClinicEmail, "Email", "lienhe@petcare.local"),
        (ClinicTaxCode, "Mã số thuế", ""),
        (InvoiceFooter, "Lời cảm ơn cuối hóa đơn", "Cảm ơn quý khách! Chúc bé luôn khỏe mạnh."),
        (VaccineRemindDays, "Nhắc lịch tiêm trước (ngày)", "7"),
        (ExpiryWarnDays, "Cảnh báo thuốc sắp hết hạn trước (ngày)", "60")
    };

    private readonly PetDbContext _db;
    private Dictionary<string, string?>? _cache;
    public SettingsService(PetDbContext db) => _db = db;

    public async Task<string> GetAsync(string key)
    {
        _cache ??= await _db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
        if (_cache.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
        return All.FirstOrDefault(a => a.Key == key).Default ?? "";
    }

    public async Task<int> GetIntAsync(string key, int fallback) =>
        int.TryParse(await GetAsync(key), out var v) ? v : fallback;

    public async Task SetAsync(string key, string? value)
    {
        var s = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == key);
        if (s == null) _db.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
        else s.Value = value;
        _cache = null;
    }
}

/// <summary>Danh sách chọn dùng chung cho form</summary>
public class Lookups
{
    private readonly PetDbContext _db;
    public Lookups(PetDbContext db) => _db = db;

    public async Task<List<SelectListItem>> VetsAsync(int? selected = null, string? blank = "-- Chọn bác sĩ --") =>
        WithBlank(await _db.Users.AsNoTracking().Where(u => u.IsVet && (u.IsActive || u.Id == selected)).OrderBy(u => u.FullName)
            .Select(u => new SelectListItem((u.Title != null ? u.Title + " " : "") + u.FullName, u.Id.ToString(), u.Id == selected)).ToListAsync(), blank);

    public async Task<List<SelectListItem>> SpeciesAsync(int? selected = null, string? blank = "-- Chọn loài --") =>
        WithBlank(await _db.Species.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s => new SelectListItem(s.Name, s.Id.ToString(), s.Id == selected)).ToListAsync(), blank);

    /// <summary>Giống: kèm data-species để lọc theo loài phía trình duyệt</summary>
    public async Task<List<(int Id, int SpeciesId, string Name)>> BreedsAsync() =>
        (await _db.Breeds.AsNoTracking().OrderBy(b => b.Name).Select(b => new { b.Id, b.SpeciesId, b.Name }).ToListAsync())
            .Select(b => (b.Id, b.SpeciesId, b.Name)).ToList();

    public async Task<List<ServiceItem>> ServicesAsync(ServiceKind? kind = null) =>
        await _db.ServiceItems.AsNoTracking().Where(s => s.IsActive && (kind == null || s.Kind == kind)).OrderBy(s => s.Kind).ThenBy(s => s.Name).ToListAsync();

    public async Task<List<Product>> ProductsAsync(ProductKind? kind = null) =>
        await _db.Products.AsNoTracking().Where(p => p.IsActive && (kind == null || p.Kind == kind)).OrderBy(p => p.Name).ToListAsync();

    public async Task<List<SelectListItem>> CagesAsync(int? selected = null, int? exceptAdmissionId = null)
    {
        var busy = await _db.Admissions.AsNoTracking().Where(a => a.Status == AdmissionStatus.Active && a.Id != exceptAdmissionId && a.CageId != null)
            .Select(a => a.CageId!.Value).ToListAsync();
        var cages = await _db.Cages.AsNoTracking().Where(c => c.IsActive || c.Id == selected).OrderBy(c => c.Code).ToListAsync();
        return cages.Select(c => new SelectListItem(c.Code + " – " + c.Name + (busy.Contains(c.Id) ? " (đang có thú)" : ""), c.Id.ToString(), c.Id == selected)
            { Disabled = busy.Contains(c.Id) && c.Id != selected }).ToList();
    }

    public async Task<List<SelectListItem>> PetsOfOwnerAsync(int? ownerId, int? selected = null)
    {
        if (ownerId == null) return new List<SelectListItem> { new("-- Chọn khách hàng trước --", "") };
        return WithBlank(await _db.Pets.AsNoTracking().Where(p => p.OwnerId == ownerId && !p.IsDeceased).OrderBy(p => p.Name)
            .Select(p => new SelectListItem(p.Name + (p.Species != null ? " (" + p.Species.Name + ")" : ""), p.Id.ToString(), p.Id == selected)).ToListAsync(), "-- Chọn thú cưng --");
    }

    private static List<SelectListItem> WithBlank(List<SelectListItem> list, string? blank)
    {
        if (blank != null) list.Insert(0, new SelectListItem(blank, ""));
        return list;
    }
}
