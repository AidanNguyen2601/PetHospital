using System.ComponentModel.DataAnnotations;
using PetHospital.Web.Models;

namespace PetHospital.Web.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Nhập tên đăng nhập"), Display(Name = "Tên đăng nhập")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Nhập mật khẩu"), DataType(DataType.Password), Display(Name = "Mật khẩu")]
    public string Password { get; set; } = "";

    [Display(Name = "Ghi nhớ đăng nhập")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Nhập mật khẩu hiện tại"), DataType(DataType.Password), Display(Name = "Mật khẩu hiện tại")]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "Nhập mật khẩu mới"), StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu tối thiểu 6 ký tự"), DataType(DataType.Password), Display(Name = "Mật khẩu mới")]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Nhập lại mật khẩu mới"), Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu nhập lại không khớp"), DataType(DataType.Password), Display(Name = "Nhập lại mật khẩu mới")]
    public string ConfirmPassword { get; set; } = "";
}

public class UserEditViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Nhập tên đăng nhập"), StringLength(50), RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Chỉ dùng chữ không dấu, số, dấu chấm, gạch"), Display(Name = "Tên đăng nhập")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Nhập họ tên"), StringLength(150), Display(Name = "Họ tên")]
    public string FullName { get; set; } = "";

    [StringLength(100), Display(Name = "Chức danh (BS., KTV...)")]
    public string? Title { get; set; }

    [StringLength(150), EmailAddress(ErrorMessage = "Email không hợp lệ"), Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(30), Display(Name = "Điện thoại")]
    public string? Phone { get; set; }

    [Display(Name = "Vai trò")]
    public List<string> Roles { get; set; } = new();

    [Display(Name = "Là bác sĩ thú y")]
    public bool IsVet { get; set; }

    [Display(Name = "Đang hoạt động")]
    public bool IsActive { get; set; } = true;

    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu tối thiểu 6 ký tự"), DataType(DataType.Password), Display(Name = "Mật khẩu")]
    public string? Password { get; set; }

    [Display(Name = "Làm việc ở mọi chi nhánh (quản lý chuỗi)")]
    public bool AllBranches { get; set; }

    [Display(Name = "Chi nhánh làm việc")]
    public List<int> BranchIds { get; set; } = new();
}

/// <summary>Phiếu chuyển kho giữa chi nhánh</summary>
public class TransferFormModel
{
    public int Id { get; set; }

    [DataType(DataType.Date), Display(Name = "Ngày chuyển")]
    public DateTime Date { get; set; } = DateTime.Today;

    [Display(Name = "Chuyển đến chi nhánh")]
    public int ToBranchId { get; set; }

    [StringLength(1000), Display(Name = "Ghi chú")]
    public string? Note { get; set; }

    public List<TransferLineInput> Lines { get; set; } = new();
}

public class TransferLineInput
{
    public int? ProductId { get; set; }
    public decimal Quantity { get; set; } = 1;
}

/// <summary>Tồn kho một mặt hàng tại chi nhánh khác</summary>
public record BranchStockRow(string Branch, decimal Quantity);

public class DashboardViewModel
{
    public int AppointmentsToday { get; set; }
    public int Waiting { get; set; }
    public int InProgress { get; set; }
    public int CompletedToday { get; set; }
    public int Admitted { get; set; }
    public decimal RevenueToday { get; set; }
    public decimal RevenueMonth { get; set; }
    public decimal Receivable { get; set; }
    public int UnpaidInvoices { get; set; }
    public List<Appointment> Upcoming { get; set; } = new();
    public List<Visit> Queue { get; set; } = new();
    public List<Vaccination> VaccineDue { get; set; } = new();
    public List<Product> LowStock { get; set; } = new();
    public List<ProductBatch> Expiring { get; set; } = new();
    public List<(string Label, decimal Value)> Revenue30 { get; set; } = new();
    public List<(string Label, decimal Value)> RevenueByKind { get; set; } = new();
}

/// <summary>Dòng chọn dịch vụ / thuốc từ form (bảng nhiều dòng)</summary>
public class LineInput
{
    public int? ItemId { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal? UnitPrice { get; set; }
    public string? Note { get; set; }
    public int? Days { get; set; }
}

public class VisitFormModel
{
    public Visit Visit { get; set; } = new();
    public List<LineInput> Services { get; set; } = new();
    public List<LineInput> Medicines { get; set; } = new();
}

public class InvoiceLineInput
{
    /// <summary>"S:{id}" dịch vụ, "P:{id}" thuốc / hàng hóa, rỗng = dòng khác (nhập tay)</summary>
    public string? Item { get; set; }
    public string? Description { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public string? Unit { get; set; }
}

public class InvoiceFormModel
{
    public int Id { get; set; }
    public int? OwnerId { get; set; }
    public int? PetId { get; set; }
    [StringLength(150)] public string? CustomerName { get; set; }
    [Range(0, 1_000_000_000)] public decimal Discount { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
    public List<InvoiceLineInput> Lines { get; set; } = new();
}

public class ReceiptLineInput
{
    public int? ProductId { get; set; }
    public string? LotNo { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
}

public class ReceiptFormModel
{
    public StockReceipt Receipt { get; set; } = new();
    public List<ReceiptLineInput> Lines { get; set; } = new();
}

/// <summary>Báo cáo dạng bảng chung (hiển thị + xuất Excel)</summary>
public class ReportTable
{
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public List<string> Headers { get; set; } = new();
    /// <summary>Chỉ số cột canh phải (số tiền / số lượng)</summary>
    public HashSet<int> NumericColumns { get; set; } = new();
    public List<List<object?>> Rows { get; set; } = new();
    public List<object?>? Footer { get; set; }
    public List<(string Label, decimal Value)>? Chart { get; set; }
}

/// <summary>Ô chọn khách hàng (gõ tên / SĐT) – hidden input do view tự đặt, đây chỉ là ô tìm</summary>
public record OwnerPickerModel(string HiddenSelector, string? PetSelectSelector, string? Text, string? WeightSelector = null);

/// <summary>Biểu đồ cột đơn giản</summary>
public record BarChartModel(List<(string Label, decimal Value)> Data, bool Money = true, int Height = 160);
