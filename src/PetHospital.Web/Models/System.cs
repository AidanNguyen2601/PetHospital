using System.ComponentModel.DataAnnotations;

namespace PetHospital.Web.Models;

/// <summary>Tài khoản đăng nhập (bác sĩ, lễ tân, thu ngân, kho, quản lý)</summary>
public class AppUser
{
    public int Id { get; set; }

    [Required, StringLength(50)]
    public string Username { get; set; } = "";

    [Required, StringLength(150)]
    public string FullName { get; set; } = "";

    /// <summary>Học hàm / chức danh hiển thị (BS. Thú y, KTV...)</summary>
    [StringLength(100)]
    public string? Title { get; set; }

    [StringLength(150)]
    public string? Email { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    [Required]
    public string PasswordHash { get; set; } = "";

    /// <summary>Danh sách mã vai trò, phân cách bởi dấu phẩy</summary>
    [Required, StringLength(300)]
    public string Roles { get; set; } = "";

    /// <summary>Là bác sĩ thú y (xuất hiện trong danh sách chọn bác sĩ)</summary>
    public bool IsVet { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Ngôn ngữ giao diện của người dùng (null = mặc định hệ thống)</summary>
    [StringLength(20)]
    public string? Language { get; set; }

    /// <summary>Làm việc ở mọi chi nhánh (quản lý chuỗi). Quản trị hệ thống luôn được xem mọi chi nhánh.</summary>
    public bool AllBranches { get; set; }

    /// <summary>Các chi nhánh được làm việc (khi không phải toàn chuỗi)</summary>
    public List<UserBranch> Branches { get; set; } = new();

    public IEnumerable<string> RoleList() =>
        Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? FullName : $"{Title} {FullName}";
}

/// <summary>
/// Ngôn ngữ giao diện. Văn bản gốc của phần mềm là tiếng Việt (khóa dịch); mỗi ngôn ngữ có bản dịch riêng sửa được,
/// ngôn ngữ vùng miền (VD: en-GB, vi-Nam) kế thừa ngôn ngữ gốc (FallbackCode) và chỉ ghi đè những câu khác.
/// </summary>
public class Language
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Nhập mã ngôn ngữ"), StringLength(20), RegularExpression("^[a-zA-Z]{2,3}(-[a-zA-Z0-9]{2,8})*$", ErrorMessage = "Mã dạng vi, en, ko, en-GB, vi-Nam…"),
     Display(Name = "Mã ngôn ngữ")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Nhập tên ngôn ngữ"), StringLength(100), Display(Name = "Tên hiển thị")]
    public string Name { get; set; } = "";

    /// <summary>Văn hóa định dạng số / tiền (vi-VN, en-US, ko-KR…)</summary>
    [Required, StringLength(20), Display(Name = "Định dạng số (culture)")]
    public string Culture { get; set; } = "vi-VN";

    [Required, StringLength(20), Display(Name = "Định dạng ngày")]
    public string DateFormat { get; set; } = "dd/MM/yyyy";

    /// <summary>Ngôn ngữ dự phòng khi chưa có bản dịch (ngôn ngữ vùng miền kế thừa ngôn ngữ gốc)</summary>
    [StringLength(20), Display(Name = "Kế thừa từ ngôn ngữ")]
    public string? FallbackCode { get; set; }

    [Display(Name = "Thứ tự")]
    public int SortOrder { get; set; }

    [Display(Name = "Đang sử dụng")]
    public bool IsActive { get; set; } = true;
}

/// <summary>Văn bản cần dịch (văn bản gốc tiếng Việt), tự ghi nhận khi giao diện hiển thị</summary>
public class TranslationKey
{
    public int Id { get; set; }
    [Required, StringLength(800)] public string Key { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>Bản dịch của một văn bản cho một ngôn ngữ</summary>
public class Translation
{
    public int Id { get; set; }
    [Required, StringLength(20)] public string LanguageCode { get; set; } = "";
    [Required, StringLength(800)] public string Key { get; set; } = "";
    [StringLength(2000)] public string? Value { get; set; }
    /// <summary>Đã được người dùng chỉnh sửa (không bị ghi đè khi cập nhật bản dịch có sẵn)</summary>
    public bool IsCustom { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>Người dùng – chi nhánh được làm việc</summary>
public class UserBranch
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
}

/// <summary>Quyền chức năng gắn với vai trò (ma trận phân quyền cấu hình được)</summary>
public class RolePermission
{
    public int Id { get; set; }
    [Required, StringLength(50)] public string Role { get; set; } = "";
    [Required, StringLength(50)] public string Permission { get; set; } = "";
}

/// <summary>Nhật ký hệ thống: tạo / sửa / xóa dữ liệu</summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
    public int? UserId { get; set; }
    [StringLength(150)] public string? UserName { get; set; }
    [Required, StringLength(80)] public string Entity { get; set; } = "";
    [StringLength(50)] public string? EntityId { get; set; }
    [Required, StringLength(30)] public string Action { get; set; } = "";
    public string? Changes { get; set; }
    [StringLength(60)] public string? IpAddress { get; set; }
}

/// <summary>Tham số hệ thống dạng khóa – giá trị (thông tin phòng khám, ngưỡng nhắc...)</summary>
public class SystemSetting
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string Key { get; set; } = "";
    [StringLength(1000)] public string? Value { get; set; }
}
