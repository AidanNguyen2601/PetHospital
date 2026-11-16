using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetHospital.Web.Models;

/// <summary>Dữ liệu thuộc về một chi nhánh (tự lọc theo chi nhánh đang làm việc, tự gán khi tạo mới)</summary>
public interface IBranchScoped
{
    int BranchId { get; set; }
}

/// <summary>Chi nhánh / cơ sở phòng khám trong chuỗi</summary>
public class Branch
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Nhập mã chi nhánh"), StringLength(6, ErrorMessage = "Mã tối đa 6 ký tự"),
     RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "Mã chỉ gồm chữ không dấu và số"), Display(Name = "Mã chi nhánh")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Nhập tên chi nhánh"), StringLength(150), Display(Name = "Tên chi nhánh")]
    public string Name { get; set; } = "";

    [StringLength(300), Display(Name = "Địa chỉ")]
    public string? Address { get; set; }

    [StringLength(30), Display(Name = "Điện thoại")]
    public string? Phone { get; set; }

    [StringLength(150), EmailAddress(ErrorMessage = "Email không hợp lệ"), Display(Name = "Email")]
    public string? Email { get; set; }

    [Display(Name = "Thứ tự")]
    public int SortOrder { get; set; }

    [Display(Name = "Đang hoạt động")]
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>Tồn kho một mặt hàng tại một chi nhánh</summary>
public class ProductStock : IBranchScoped
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Giá vốn bình quân tại chi nhánh</summary>
    public decimal CostPrice { get; set; }
}

/// <summary>Phiếu chuyển kho giữa hai chi nhánh (BranchId = chi nhánh xuất)</summary>
public class StockTransfer : IBranchScoped
{
    public int Id { get; set; }

    [Display(Name = "Chi nhánh xuất")]
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }

    [Display(Name = "Chi nhánh nhận")]
    public int ToBranchId { get; set; }
    public Branch? ToBranch { get; set; }

    [StringLength(20), Display(Name = "Số phiếu")]
    public string Code { get; set; } = "";

    [DataType(DataType.Date), Display(Name = "Ngày chuyển")]
    public DateTime Date { get; set; } = DateTime.Today;

    [StringLength(1000), Display(Name = "Ghi chú")]
    public string? Note { get; set; }

    public DocState State { get; set; }
    /// <summary>Tổng giá vốn hàng chuyển</summary>
    public decimal Total { get; set; }

    public int CreatedById { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? PostedAt { get; set; }

    public List<StockTransferLine> Lines { get; set; } = new();
}

public class StockTransferLine
{
    public int Id { get; set; }
    public int TransferId { get; set; }
    public StockTransfer? Transfer { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal Quantity { get; set; }
    public decimal CostAmount { get; set; }
}

/// <summary>Khách hàng – chủ nuôi</summary>
public class Owner : IBranchScoped
{
    public int Id { get; set; }
    public int BranchId { get; set; }

    [StringLength(20), Display(Name = "Mã khách hàng")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Nhập họ tên"), StringLength(150), Display(Name = "Họ tên")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Nhập số điện thoại"), StringLength(20), Display(Name = "Điện thoại")]
    public string Phone { get; set; } = "";

    [StringLength(150), EmailAddress(ErrorMessage = "Email không hợp lệ"), Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(300), Display(Name = "Địa chỉ")]
    public string? Address { get; set; }

    [StringLength(1000), Display(Name = "Ghi chú")]
    public string? Note { get; set; }

    [Display(Name = "Đang hoạt động")]
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<Pet> Pets { get; set; } = new();
}

/// <summary>Loài (chó, mèo, ...)</summary>
public class Species
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Nhập tên loài"), StringLength(100), Display(Name = "Loài")]
    public string Name { get; set; } = "";
    [Display(Name = "Thứ tự")] public int SortOrder { get; set; }
    public List<Breed> Breeds { get; set; } = new();
}

/// <summary>Giống theo loài</summary>
public class Breed
{
    public int Id { get; set; }
    public int SpeciesId { get; set; }
    public Species? Species { get; set; }
    [Required(ErrorMessage = "Nhập tên giống"), StringLength(100), Display(Name = "Giống")]
    public string Name { get; set; } = "";
}

/// <summary>Thú cưng – bệnh nhân</summary>
public class Pet
{
    public int Id { get; set; }

    [StringLength(20), Display(Name = "Mã thú cưng")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Nhập tên thú cưng"), StringLength(100), Display(Name = "Tên")]
    public string Name { get; set; } = "";

    [Display(Name = "Chủ nuôi")]
    public int OwnerId { get; set; }
    public Owner? Owner { get; set; }

    [Required(ErrorMessage = "Chọn loài"), Display(Name = "Loài")]
    public int? SpeciesId { get; set; }
    public Species? Species { get; set; }

    [Display(Name = "Giống")]
    public int? BreedId { get; set; }
    public Breed? Breed { get; set; }

    [Display(Name = "Giới tính")]
    public PetGender Gender { get; set; }

    [DataType(DataType.Date), Display(Name = "Ngày sinh")]
    public DateTime? BirthDate { get; set; }

    [StringLength(100), Display(Name = "Màu lông / đặc điểm")]
    public string? Color { get; set; }

    [Range(0, 500, ErrorMessage = "Cân nặng không hợp lệ"), Display(Name = "Cân nặng (kg)")]
    public decimal? WeightKg { get; set; }

    [StringLength(50), Display(Name = "Số vi mạch (microchip)")]
    public string? Microchip { get; set; }

    [Display(Name = "Đã triệt sản")]
    public bool Neutered { get; set; }

    [StringLength(500), Display(Name = "Dị ứng / lưu ý y khoa")]
    public string? Allergies { get; set; }

    [StringLength(1000), Display(Name = "Ghi chú")]
    public string? Note { get; set; }

    [Display(Name = "Đã mất")]
    public bool IsDeceased { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string AgeText
    {
        get
        {
            if (!BirthDate.HasValue) return "";
            var today = DateTime.Today;
            var months = (today.Year - BirthDate.Value.Year) * 12 + today.Month - BirthDate.Value.Month - (today.Day < BirthDate.Value.Day ? 1 : 0);
            if (months < 0) return "";
            if (months < 12) return $"{months} tháng";
            return months % 12 == 0 ? $"{months / 12} tuổi" : $"{months / 12} tuổi {months % 12} tháng";
        }
    }
}

/// <summary>Dịch vụ & bảng giá</summary>
public class ServiceItem
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Nhập mã dịch vụ"), StringLength(30), Display(Name = "Mã dịch vụ")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Nhập tên dịch vụ"), StringLength(200), Display(Name = "Tên dịch vụ")]
    public string Name { get; set; } = "";

    [Display(Name = "Nhóm dịch vụ")]
    public ServiceKind Kind { get; set; }

    [StringLength(30), Display(Name = "Đơn vị tính")]
    public string Unit { get; set; } = "Lần";

    [Range(0, 1_000_000_000, ErrorMessage = "Giá không hợp lệ"), Display(Name = "Đơn giá")]
    public decimal Price { get; set; }

    [Range(0, 1440), Display(Name = "Thời lượng dự kiến (phút)")]
    public int DurationMinutes { get; set; } = 30;

    [StringLength(500), Display(Name = "Mô tả")]
    public string? Description { get; set; }

    [Display(Name = "Đang áp dụng")]
    public bool IsActive { get; set; } = true;
}

/// <summary>Thuốc, vắc-xin, vật tư, hàng hóa bán lẻ</summary>
public class Product
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Nhập mã hàng"), StringLength(30), Display(Name = "Mã hàng")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Nhập tên hàng"), StringLength(200), Display(Name = "Tên thuốc / hàng hóa")]
    public string Name { get; set; } = "";

    [Display(Name = "Loại")]
    public ProductKind Kind { get; set; }

    [Required(ErrorMessage = "Nhập đơn vị tính"), StringLength(30), Display(Name = "Đơn vị tính")]
    public string Unit { get; set; } = "Viên";

    [StringLength(200), Display(Name = "Hoạt chất / quy cách")]
    public string? Ingredient { get; set; }

    [StringLength(500), Display(Name = "Cách dùng mặc định")]
    public string? DefaultUsage { get; set; }

    [Range(0, 1_000_000_000), Display(Name = "Giá bán")]
    public decimal SalePrice { get; set; }

    /// <summary>Giá vốn tham chiếu toàn chuỗi (giá vốn bình quân thực tế lưu theo từng chi nhánh ở ProductStock)</summary>
    [Range(0, 1_000_000_000), Display(Name = "Giá vốn bình quân")]
    public decimal CostPrice { get; set; }

    [Range(0, 1_000_000), Display(Name = "Tồn tối thiểu")]
    public decimal MinStock { get; set; }

    /// <summary>Tồn kho theo chi nhánh (tự lọc theo chi nhánh đang làm việc; báo cáo toàn chuỗi = tổng các chi nhánh)</summary>
    public List<ProductStock> Stocks { get; set; } = new();

    /// <summary>Tồn kho của chi nhánh đang xem (cần Include(p =&gt; p.Stocks))</summary>
    [NotMapped, Display(Name = "Tồn kho")]
    public decimal Stock => Stocks.Sum(s => s.Quantity);

    /// <summary>Giá vốn bình quân tại chi nhánh đang xem (chưa có tồn thì lấy giá tham chiếu)</summary>
    [NotMapped]
    public decimal AvgCost
    {
        get
        {
            var qty = Stocks.Where(s => s.Quantity > 0).Sum(s => s.Quantity);
            if (qty > 0) return Math.Round(Stocks.Where(s => s.Quantity > 0).Sum(s => s.Quantity * s.CostPrice) / qty, 2);
            return Stocks.Count == 1 && Stocks[0].CostPrice > 0 ? Stocks[0].CostPrice : CostPrice;
        }
    }

    /// <summary>Giá trị tồn theo giá vốn</summary>
    [NotMapped]
    public decimal StockValue => Stocks.Sum(s => Math.Max(0, s.Quantity) * s.CostPrice);

    [Display(Name = "Theo dõi lô / hạn dùng")]
    public bool TrackExpiry { get; set; } = true;

    /// <summary>Vắc-xin: số ngày tới mũi nhắc lại mặc định</summary>
    [Range(0, 3650), Display(Name = "Nhắc lại sau (ngày)")]
    public int? BoosterDays { get; set; }

    [Display(Name = "Đang kinh doanh")]
    public bool IsActive { get; set; } = true;

    public List<ProductBatch> Batches { get; set; } = new();
}

/// <summary>Lô hàng (số lô, hạn dùng, số lượng còn) – xuất kho theo hạn gần nhất trước (FEFO)</summary>
public class ProductBatch : IBranchScoped
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    [StringLength(50)] public string? LotNo { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.Now;
}

/// <summary>Phiếu nhập kho</summary>
public class StockReceipt : IBranchScoped
{
    public int Id { get; set; }
    public int BranchId { get; set; }

    [StringLength(20), Display(Name = "Số phiếu")]
    public string Code { get; set; } = "";

    [DataType(DataType.Date), Display(Name = "Ngày nhập")]
    public DateTime Date { get; set; } = DateTime.Today;

    [StringLength(200), Display(Name = "Nhà cung cấp")]
    public string? Supplier { get; set; }

    [StringLength(50), Display(Name = "Số hóa đơn NCC")]
    public string? SupplierInvoiceNo { get; set; }

    [StringLength(1000), Display(Name = "Ghi chú")]
    public string? Note { get; set; }

    public DocState State { get; set; }
    public decimal Total { get; set; }

    public int CreatedById { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? PostedAt { get; set; }

    public List<StockReceiptLine> Lines { get; set; } = new();
}

public class StockReceiptLine
{
    public int Id { get; set; }
    public int ReceiptId { get; set; }
    public StockReceipt? Receipt { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    [StringLength(50)] public string? LotNo { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>Sổ kho: mọi biến động nhập / xuất / điều chỉnh</summary>
public class StockMove : IBranchScoped
{
    public long Id { get; set; }
    public int BranchId { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int? BatchId { get; set; }
    public ProductBatch? Batch { get; set; }
    public StockMoveType Type { get; set; }
    /// <summary>Số lượng có dấu: + nhập, − xuất</summary>
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    /// <summary>Tồn sau biến động</summary>
    public decimal Balance { get; set; }
    [StringLength(30)] public string? RefType { get; set; }
    public int? RefId { get; set; }
    [StringLength(30)] public string? RefCode { get; set; }
    [StringLength(500)] public string? Note { get; set; }
    public int? UserId { get; set; }
    public AppUser? User { get; set; }
}

/// <summary>Lịch hẹn</summary>
public class Appointment : IBranchScoped
{
    public int Id { get; set; }
    public int BranchId { get; set; }

    [StringLength(20), Display(Name = "Mã lịch hẹn")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Chọn khách hàng"), Display(Name = "Khách hàng")]
    public int? OwnerId { get; set; }
    public Owner? Owner { get; set; }

    [Display(Name = "Thú cưng")]
    public int? PetId { get; set; }
    public Pet? Pet { get; set; }

    [Display(Name = "Bác sĩ")]
    public int? VetId { get; set; }
    public AppUser? Vet { get; set; }

    [Display(Name = "Dịch vụ")]
    public int? ServiceItemId { get; set; }
    public ServiceItem? ServiceItem { get; set; }

    [Required(ErrorMessage = "Chọn thời gian"), Display(Name = "Thời gian hẹn")]
    public DateTime StartAt { get; set; } = DateTime.Today.AddHours(9);

    [Range(5, 600), Display(Name = "Thời lượng (phút)")]
    public int DurationMinutes { get; set; } = 30;

    [StringLength(500), Display(Name = "Lý do / yêu cầu")]
    public string? Reason { get; set; }

    [Display(Name = "Trạng thái")]
    public AppointmentStatus Status { get; set; }

    [StringLength(500), Display(Name = "Ghi chú")]
    public string? Note { get; set; }

    public int? VisitId { get; set; }

    public int CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime EndAt => StartAt.AddMinutes(DurationMinutes);
}

/// <summary>Lượt khám / bệnh án</summary>
public class Visit : IBranchScoped
{
    public int Id { get; set; }
    public int BranchId { get; set; }

    [StringLength(20), Display(Name = "Mã phiếu khám")]
    public string Code { get; set; } = "";

    [Display(Name = "Thú cưng")]
    public int PetId { get; set; }
    public Pet? Pet { get; set; }

    public int OwnerId { get; set; }
    public Owner? Owner { get; set; }

    [Display(Name = "Bác sĩ")]
    public int? VetId { get; set; }
    public AppUser? Vet { get; set; }

    public int? AppointmentId { get; set; }

    [Display(Name = "Thời gian tiếp nhận")]
    public DateTime VisitAt { get; set; } = DateTime.Now;

    [Display(Name = "Trạng thái")]
    public VisitStatus Status { get; set; }

    [StringLength(500), Display(Name = "Lý do đến khám")]
    public string? Reason { get; set; }

    [StringLength(2000), Display(Name = "Triệu chứng / bệnh sử")]
    public string? Symptoms { get; set; }

    [Range(0, 500), Display(Name = "Cân nặng (kg)")]
    public decimal? WeightKg { get; set; }

    [Range(25, 45, ErrorMessage = "Nhiệt độ 25–45 °C"), Display(Name = "Nhiệt độ (°C)")]
    public decimal? Temperature { get; set; }

    [Range(0, 400), Display(Name = "Nhịp tim (lần/phút)")]
    public int? HeartRate { get; set; }

    [Range(0, 200), Display(Name = "Nhịp thở (lần/phút)")]
    public int? RespiratoryRate { get; set; }

    [StringLength(2000), Display(Name = "Khám lâm sàng")]
    public string? Examination { get; set; }

    [StringLength(1000), Display(Name = "Chẩn đoán")]
    public string? Diagnosis { get; set; }

    [StringLength(2000), Display(Name = "Hướng điều trị")]
    public string? Treatment { get; set; }

    [StringLength(1000), Display(Name = "Lời dặn")]
    public string? Advice { get; set; }

    [DataType(DataType.Date), Display(Name = "Hẹn tái khám")]
    public DateTime? FollowUpDate { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public int? InvoiceId { get; set; }

    public int CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<VisitService> Services { get; set; } = new();
    public List<VisitMedicine> Medicines { get; set; } = new();
}

/// <summary>Dịch vụ thực hiện trong lượt khám</summary>
public class VisitService
{
    public int Id { get; set; }
    public int VisitId { get; set; }
    public Visit? Visit { get; set; }
    public int ServiceItemId { get; set; }
    public ServiceItem? ServiceItem { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    [StringLength(500)] public string? Note { get; set; }
}

/// <summary>Thuốc kê đơn / dùng trong lượt khám</summary>
public class VisitMedicine
{
    public int Id { get; set; }
    public int VisitId { get; set; }
    public Visit? Visit { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    /// <summary>Liều dùng, cách dùng</summary>
    [StringLength(500)] public string? Dosage { get; set; }
    public int? Days { get; set; }
}

/// <summary>Sổ tiêm phòng</summary>
public class Vaccination : IBranchScoped
{
    public int Id { get; set; }
    public int BranchId { get; set; }

    [Display(Name = "Thú cưng")]
    public int PetId { get; set; }
    public Pet? Pet { get; set; }

    [Display(Name = "Vắc-xin (trong kho)")]
    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    [Required(ErrorMessage = "Nhập tên vắc-xin"), StringLength(200), Display(Name = "Tên vắc-xin / bệnh phòng")]
    public string VaccineName { get; set; } = "";

    [Range(1, 50), Display(Name = "Mũi số")]
    public int DoseNumber { get; set; } = 1;

    [Required, DataType(DataType.Date), Display(Name = "Ngày tiêm")]
    public DateTime Date { get; set; } = DateTime.Today;

    [DataType(DataType.Date), Display(Name = "Hẹn mũi tiếp theo")]
    public DateTime? NextDueDate { get; set; }

    [StringLength(50), Display(Name = "Số lô")]
    public string? LotNo { get; set; }

    [Display(Name = "Bác sĩ")]
    public int? VetId { get; set; }
    public AppUser? Vet { get; set; }

    public int? VisitId { get; set; }
    public Visit? Visit { get; set; }

    [StringLength(500), Display(Name = "Ghi chú")]
    public string? Note { get; set; }

    /// <summary>Đã nhắc / đã tiêm mũi tiếp theo (không hiện trong danh sách nhắc nữa)</summary>
    public bool ReminderDone { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>Chuồng / phòng lưu trú</summary>
public class Cage : IBranchScoped
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    [Required(ErrorMessage = "Nhập mã chuồng"), StringLength(20), Display(Name = "Mã chuồng")]
    public string Code { get; set; } = "";
    [Required(ErrorMessage = "Nhập tên"), StringLength(100), Display(Name = "Tên / vị trí")]
    public string Name { get; set; } = "";
    [StringLength(50), Display(Name = "Loại (nhỏ / vừa / lớn / cách ly)")]
    public string? Size { get; set; }
    [Display(Name = "Khu cách ly")]
    public bool IsIsolation { get; set; }
    [Range(0, 100_000_000), Display(Name = "Giá / ngày")]
    public decimal DailyRate { get; set; }
    [Display(Name = "Đang sử dụng")]
    public bool IsActive { get; set; } = true;
}

/// <summary>Nội trú / lưu chuồng</summary>
public class Admission : IBranchScoped
{
    public int Id { get; set; }
    public int BranchId { get; set; }

    [StringLength(20), Display(Name = "Mã phiếu")]
    public string Code { get; set; } = "";

    [Display(Name = "Hình thức")]
    public AdmissionKind Kind { get; set; }

    [Display(Name = "Thú cưng")]
    public int PetId { get; set; }
    public Pet? Pet { get; set; }

    public int OwnerId { get; set; }
    public Owner? Owner { get; set; }

    [Required(ErrorMessage = "Chọn chuồng"), Display(Name = "Chuồng")]
    public int? CageId { get; set; }
    public Cage? Cage { get; set; }

    [Display(Name = "Bác sĩ phụ trách")]
    public int? VetId { get; set; }
    public AppUser? Vet { get; set; }

    public int? VisitId { get; set; }

    [Display(Name = "Thời gian nhận")]
    public DateTime AdmitAt { get; set; } = DateTime.Now;

    [DataType(DataType.Date), Display(Name = "Dự kiến xuất")]
    public DateTime? ExpectedDischarge { get; set; }

    [Display(Name = "Thời gian xuất")]
    public DateTime? DischargeAt { get; set; }

    [StringLength(1000), Display(Name = "Lý do / chẩn đoán")]
    public string? Reason { get; set; }

    [StringLength(1000), Display(Name = "Đồ gửi kèm / lưu ý chăm sóc")]
    public string? CareNote { get; set; }

    [Range(0, 100_000_000), Display(Name = "Giá / ngày")]
    public decimal DailyRate { get; set; }

    [Display(Name = "Trạng thái")]
    public AdmissionStatus Status { get; set; }

    [StringLength(1000), Display(Name = "Tình trạng khi xuất")]
    public string? DischargeNote { get; set; }

    public int? InvoiceId { get; set; }
    public int CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<AdmissionNote> Notes { get; set; } = new();

    /// <summary>Số ngày tính tiền (tối thiểu 1, phần lẻ tính tròn lên)</summary>
    public int BillableDays(DateTime? until = null)
    {
        var end = DischargeAt ?? until ?? DateTime.Now;
        var days = (int)Math.Ceiling((end - AdmitAt).TotalHours / 24.0);
        return Math.Max(1, days);
    }
}

/// <summary>Theo dõi hằng ngày khi nội trú</summary>
public class AdmissionNote
{
    public int Id { get; set; }
    public int AdmissionId { get; set; }
    public Admission? Admission { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
    public decimal? Temperature { get; set; }
    public decimal? WeightKg { get; set; }
    [StringLength(100)] public string? Appetite { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
    [StringLength(1000)] public string? Treatment { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
}

/// <summary>Hóa đơn bán hàng / dịch vụ</summary>
public class Invoice : IBranchScoped
{
    public int Id { get; set; }
    public int BranchId { get; set; }

    [StringLength(20), Display(Name = "Số hóa đơn")]
    public string Code { get; set; } = "";

    [Display(Name = "Ngày")]
    public DateTime Date { get; set; } = DateTime.Now;

    [Display(Name = "Khách hàng")]
    public int? OwnerId { get; set; }
    public Owner? Owner { get; set; }

    [StringLength(150), Display(Name = "Tên khách (khách lẻ)")]
    public string? CustomerName { get; set; }

    [Display(Name = "Thú cưng")]
    public int? PetId { get; set; }
    public Pet? Pet { get; set; }

    public int? VisitId { get; set; }
    public int? AdmissionId { get; set; }

    [Display(Name = "Trạng thái")]
    public InvoiceStatus Status { get; set; }

    public decimal SubTotal { get; set; }

    [Range(0, 1_000_000_000), Display(Name = "Giảm giá")]
    public decimal Discount { get; set; }

    public decimal Total { get; set; }
    public decimal Paid { get; set; }

    [StringLength(1000), Display(Name = "Ghi chú")]
    public string? Note { get; set; }

    public DateTime? IssuedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    [StringLength(500)] public string? CancelReason { get; set; }

    public int CreatedById { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<InvoiceLine> Lines { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();

    public decimal Balance => Total - Paid;

    public void Recalc()
    {
        foreach (var l in Lines) l.Amount = Math.Round(l.Quantity * l.UnitPrice, 0);
        SubTotal = Lines.Sum(l => l.Amount);
        if (Discount > SubTotal) Discount = SubTotal;
        Total = SubTotal - Discount;
    }
}

public class InvoiceLine
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public InvoiceLineKind Kind { get; set; }
    public int? ServiceItemId { get; set; }
    public ServiceItem? ServiceItem { get; set; }
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    [Required, StringLength(300)] public string Description { get; set; } = "";
    [StringLength(30)] public string? Unit { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Giá vốn xuất kho (tính khi phát hành hóa đơn) – phục vụ báo cáo lãi gộp</summary>
    public decimal CostAmount { get; set; }
    /// <summary>Bác sĩ thực hiện (báo cáo doanh thu theo bác sĩ)</summary>
    public int? VetId { get; set; }
    public AppUser? Vet { get; set; }
}

/// <summary>Phiếu thu</summary>
public class Payment
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    [StringLength(300)] public string? Note { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
}
