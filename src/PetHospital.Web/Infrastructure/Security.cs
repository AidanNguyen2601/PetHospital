namespace PetHospital.Web.Infrastructure;

/// <summary>Vai trò người dùng của phòng khám</summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Vet = "Vet";
    public const string Receptionist = "Receptionist";
    public const string Cashier = "Cashier";
    public const string Warehouse = "Warehouse";

    public static readonly (string Code, string Name)[] All =
    {
        (Admin, "Quản trị hệ thống"),
        (Manager, "Quản lý phòng khám"),
        (Vet, "Bác sĩ thú y"),
        (Receptionist, "Lễ tân / Chăm sóc khách hàng"),
        (Cashier, "Thu ngân"),
        (Warehouse, "Kho / Dược")
    };

    public static string DisplayName(string? code) =>
        L.T(All.FirstOrDefault(r => r.Code == code).Name ?? code ?? "");
}

/// <summary>Quyền chức năng. Ma trận vai trò – quyền cấu hình tại Quản trị › Vai trò &amp; quyền.</summary>
public static class Perm
{
    public const string CustomerView = "customer.view";
    public const string CustomerEdit = "customer.edit";
    public const string Appointment = "appt.manage";
    public const string VisitView = "visit.view";
    public const string VisitExam = "visit.exam";
    public const string Vaccine = "vaccine.manage";
    public const string Admission = "admission.manage";
    public const string InvoiceView = "invoice.view";
    public const string InvoiceManage = "invoice.manage";
    public const string InvoiceCancel = "invoice.cancel";
    public const string StockView = "stock.view";
    public const string StockManage = "stock.manage";
    public const string StockCost = "stock.cost";
    public const string Catalog = "catalog.manage";
    public const string Reports = "report.view";
    public const string Users = "admin.users";
    public const string System = "admin.system";

    public static readonly (string Code, string Group, string Name)[] All =
    {
        (CustomerView, "Khách hàng & thú cưng", "Xem khách hàng, hồ sơ thú cưng"),
        (CustomerEdit, "Khách hàng & thú cưng", "Thêm / sửa khách hàng, thú cưng"),
        (Appointment, "Tiếp nhận", "Đặt lịch hẹn, tiếp nhận khách đến khám"),
        (VisitView, "Khám chữa bệnh", "Xem bệnh án, phiếu khám"),
        (VisitExam, "Khám chữa bệnh", "Khám bệnh, chẩn đoán, kê đơn, chỉ định dịch vụ"),
        (Vaccine, "Khám chữa bệnh", "Ghi nhận tiêm phòng, nhắc lịch tiêm"),
        (Admission, "Khám chữa bệnh", "Nội trú / lưu chuồng, theo dõi hằng ngày"),
        (InvoiceView, "Thu ngân", "Xem hóa đơn"),
        (InvoiceManage, "Thu ngân", "Lập hóa đơn, bán hàng, thu tiền"),
        (InvoiceCancel, "Thu ngân", "Hủy hóa đơn đã phát hành"),
        (StockView, "Kho thuốc", "Xem tồn kho, thẻ kho"),
        (StockManage, "Kho thuốc", "Nhập kho, điều chỉnh tồn, quản lý danh mục thuốc"),
        (StockCost, "Kho thuốc", "Xem giá vốn, lãi gộp"),
        (Catalog, "Danh mục", "Dịch vụ & bảng giá, loài – giống, chuồng"),
        (Reports, "Báo cáo", "Xem và xuất báo cáo doanh thu, hoạt động"),
        (Users, "Quản trị", "Quản lý người dùng, vai trò, phân quyền"),
        (System, "Quản trị", "Thông tin phòng khám, nhật ký hệ thống")
    };

    /// <summary>Phân quyền mặc định khi khởi tạo hệ thống</summary>
    public static readonly Dictionary<string, string[]> Defaults = new()
    {
        [AppRoles.Admin] = All.Select(p => p.Code).ToArray(),
        [AppRoles.Manager] = All.Select(p => p.Code).Where(c => c != Users && c != System).ToArray(),
        [AppRoles.Vet] = new[] { CustomerView, CustomerEdit, Appointment, VisitView, VisitExam, Vaccine, Admission, InvoiceView, StockView },
        [AppRoles.Receptionist] = new[] { CustomerView, CustomerEdit, Appointment, VisitView, Vaccine, Admission, InvoiceView },
        [AppRoles.Cashier] = new[] { CustomerView, CustomerEdit, VisitView, InvoiceView, InvoiceManage, StockView },
        [AppRoles.Warehouse] = new[] { StockView, StockManage, StockCost, Catalog }
    };
}
