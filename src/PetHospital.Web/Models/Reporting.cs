using System.ComponentModel.DataAnnotations;

namespace PetHospital.Web.Models;

public enum ReportSource
{
    [Display(Name = "Câu lệnh SQL")] Sql = 0,
    [Display(Name = "View dữ liệu")] View = 1
}

/// <summary>
/// Báo cáo động do quản trị tự thiết kế: truy xuất bằng câu lệnh SELECT hoặc từ một View của database.
/// Chạy ở chế độ chỉ đọc (kiểm tra câu lệnh + giao dịch luôn hoàn tác + giới hạn thời gian / số dòng).
/// </summary>
public class DynamicReport
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Nhập tên báo cáo"), StringLength(150), Display(Name = "Tên báo cáo")]
    public string Name { get; set; } = "";

    [StringLength(100), Display(Name = "Nhóm")]
    public string? GroupName { get; set; }

    [StringLength(500), Display(Name = "Mô tả")]
    public string? Description { get; set; }

    [Display(Name = "Nguồn dữ liệu")]
    public ReportSource Source { get; set; }

    /// <summary>Câu lệnh SELECT / WITH … SELECT (nguồn SQL)</summary>
    [Display(Name = "Câu lệnh SQL")]
    public string? SqlText { get; set; }

    /// <summary>Tên view (nguồn View), dạng schema.ten hoặc ten</summary>
    [StringLength(260), Display(Name = "View")]
    public string? ViewName { get; set; }

    /// <summary>Cột ngày của view dùng lọc theo kỳ (Từ ngày – Đến ngày)</summary>
    [StringLength(128), Display(Name = "Cột lọc theo ngày")]
    public string? DateColumn { get; set; }

    /// <summary>Cột mã chi nhánh (BranchId) của view dùng lọc theo chi nhánh</summary>
    [StringLength(128), Display(Name = "Cột chi nhánh (BranchId)")]
    public string? BranchColumn { get; set; }

    /// <summary>Chọn cột hiển thị (phân cách dấu phẩy, trống = tất cả)</summary>
    [StringLength(2000), Display(Name = "Cột hiển thị")]
    public string? Columns { get; set; }

    [StringLength(1000), Display(Name = "Điều kiện lọc (WHERE)")]
    public string? WhereClause { get; set; }

    [StringLength(300), Display(Name = "Sắp xếp (ORDER BY)")]
    public string? OrderBy { get; set; }

    /// <summary>Định nghĩa tham số (JSON) – xem ReportParam</summary>
    public string? ParametersJson { get; set; }

    [StringLength(128), Display(Name = "Biểu đồ – cột nhãn")]
    public string? ChartLabel { get; set; }

    [StringLength(128), Display(Name = "Biểu đồ – cột giá trị")]
    public string? ChartValue { get; set; }

    [Display(Name = "Dòng tổng cộng các cột số")]
    public bool ShowTotals { get; set; } = true;

    /// <summary>Vai trò được xem (mã, phân cách dấu phẩy); trống = mọi người có quyền xem báo cáo</summary>
    [StringLength(300), Display(Name = "Vai trò được xem")]
    public string? AllowedRoles { get; set; }

    [Display(Name = "Thứ tự")]
    public int SortOrder { get; set; }

    [Display(Name = "Đang sử dụng")]
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
    [StringLength(150)] public string? UpdatedBy { get; set; }

    public IEnumerable<string> RoleList() =>
        (AllowedRoles ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Tham số của báo cáo động (lưu JSON trong DynamicReport.ParametersJson)</summary>
public class ReportParam
{
    /// <summary>Tên tham số trong câu lệnh, không có @</summary>
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>text | int | decimal | date | select</summary>
    public string Type { get; set; } = "text";
    /// <summary>Giá trị mặc định; ngày có thể dùng today, monthstart, yearstart, -30 (số ngày so với hôm nay)</summary>
    public string? Default { get; set; }
    /// <summary>Danh sách chọn (select): mỗi dòng "giá trị" hoặc "giá trị=nhãn"</summary>
    public string? Options { get; set; }
}
