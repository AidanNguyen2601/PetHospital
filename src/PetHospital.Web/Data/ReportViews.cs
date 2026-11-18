using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Data;

/// <summary>
/// Bộ View dựng sẵn cho báo cáo động (rpt_*): tên cột tiếng Việt, trạng thái đã đổi thành chữ, có BranchId để lọc chi nhánh.
/// Tạo lại (CREATE OR ALTER) mỗi lần khởi động – không chứa dữ liệu, an toàn khi chạy lại.
/// </summary>
public static class ReportViews
{
    private const string Branch = "JOIN Branches b ON b.Id = {0}.BranchId";

    private static string ServiceKindText(string col) =>
        $"CASE {col} WHEN 0 THEN N'Khám bệnh' WHEN 1 THEN N'Tiêm phòng' WHEN 2 THEN N'Xét nghiệm' WHEN 3 THEN N'Chẩn đoán hình ảnh' " +
        $"WHEN 4 THEN N'Phẫu thuật / thủ thuật' WHEN 5 THEN N'Điều trị' WHEN 6 THEN N'Spa / Grooming' WHEN 7 THEN N'Lưu chuồng / nội trú' ELSE N'Khác' END";

    private static string ProductKindText(string col) =>
        $"CASE {col} WHEN 0 THEN N'Thuốc' WHEN 1 THEN N'Vắc-xin' WHEN 2 THEN N'Vật tư y tế' WHEN 3 THEN N'Thức ăn / dinh dưỡng' WHEN 4 THEN N'Phụ kiện' ELSE N'Khác' END";

    /// <summary>(Tên view, mô tả, câu lệnh SELECT)</summary>
    public static readonly (string Name, string Description, string Select)[] All =
    {
        ("rpt_HoaDon", "Hóa đơn: khách hàng, tổng tiền, đã thu, còn nợ, trạng thái", $@"
SELECT i.Id AS InvoiceId, i.BranchId, b.Name AS [Chi nhánh], i.Code AS [Mã hóa đơn],
    CAST(COALESCE(i.IssuedAt, i.Date) AS date) AS [Ngày], COALESCE(i.IssuedAt, i.Date) AS [Thời điểm],
    CASE i.Status WHEN 0 THEN N'Nháp' WHEN 1 THEN N'Chưa thanh toán' WHEN 2 THEN N'Thanh toán một phần' WHEN 3 THEN N'Đã thanh toán' ELSE N'Đã hủy' END AS [Trạng thái],
    i.Status AS StatusCode, o.Code AS [Mã khách], COALESCE(o.FullName, i.CustomerName, N'Khách lẻ') AS [Khách hàng], o.Phone AS [Điện thoại],
    p.Name AS [Thú cưng], i.SubTotal AS [Tiền hàng], i.Discount AS [Giảm giá], i.Total AS [Tổng tiền], i.Paid AS [Đã thu],
    i.Total - i.Paid AS [Còn nợ], u.FullName AS [Người lập]
FROM Invoices i {string.Format(Branch, "i")}
LEFT JOIN Owners o ON o.Id = i.OwnerId LEFT JOIN Pets p ON p.Id = i.PetId LEFT JOIN Users u ON u.Id = i.CreatedById"),

        ("rpt_DoanhThuChiTiet", "Từng dòng hàng của hóa đơn đã phát hành: dịch vụ / thuốc, nhóm, số lượng, doanh thu, giá vốn, lãi gộp, bác sĩ", $@"
SELECT l.Id AS LineId, i.Id AS InvoiceId, i.BranchId, b.Name AS [Chi nhánh], i.Code AS [Mã hóa đơn], CAST(i.IssuedAt AS date) AS [Ngày],
    COALESCE(o.FullName, i.CustomerName, N'Khách lẻ') AS [Khách hàng], sp.Name AS [Loài],
    CASE l.Kind WHEN 0 THEN N'Dịch vụ' WHEN 1 THEN N'Thuốc / hàng hóa' ELSE N'Khác' END AS [Loại],
    COALESCE(s.Code, pr.Code) AS [Mã hàng], l.Description AS [Tên dịch vụ / hàng hóa],
    CASE WHEN s.Id IS NOT NULL THEN {ServiceKindText("s.Kind")} WHEN pr.Id IS NOT NULL THEN {ProductKindText("pr.Kind")} ELSE N'Khác' END AS [Nhóm],
    l.Quantity AS [Số lượng], l.UnitPrice AS [Đơn giá], l.Amount AS [Thành tiền], l.CostAmount AS [Giá vốn], l.Amount - l.CostAmount AS [Lãi gộp],
    v.FullName AS [Bác sĩ]
FROM InvoiceLines l JOIN Invoices i ON i.Id = l.InvoiceId {string.Format(Branch, "i")}
LEFT JOIN Owners o ON o.Id = i.OwnerId LEFT JOIN Pets p ON p.Id = i.PetId LEFT JOIN Species sp ON sp.Id = p.SpeciesId
LEFT JOIN ServiceItems s ON s.Id = l.ServiceItemId LEFT JOIN Products pr ON pr.Id = l.ProductId LEFT JOIN Users v ON v.Id = l.VetId
WHERE i.Status IN (1, 2, 3) AND i.IssuedAt IS NOT NULL"),

        ("rpt_ThuTien", "Các lần thu tiền: ngày, hóa đơn, khách hàng, hình thức, số tiền, thu ngân", $@"
SELECT pm.Id AS PaymentId, i.BranchId, b.Name AS [Chi nhánh], CAST(pm.At AS date) AS [Ngày], pm.At AS [Thời điểm], i.Code AS [Mã hóa đơn],
    COALESCE(o.FullName, i.CustomerName, N'Khách lẻ') AS [Khách hàng],
    CASE pm.Method WHEN 0 THEN N'Tiền mặt' WHEN 1 THEN N'Chuyển khoản' WHEN 2 THEN N'Thẻ' ELSE N'Ví điện tử' END AS [Hình thức],
    pm.Amount AS [Số tiền], u.FullName AS [Thu ngân]
FROM Payments pm JOIN Invoices i ON i.Id = pm.InvoiceId {string.Format(Branch, "i")}
LEFT JOIN Owners o ON o.Id = i.OwnerId LEFT JOIN Users u ON u.Id = pm.UserId"),

        ("rpt_LuotKham", "Lượt khám: thú cưng, loài, chủ nuôi, bác sĩ, lý do, chẩn đoán, sinh hiệu", $@"
SELECT v.Id AS VisitId, v.BranchId, b.Name AS [Chi nhánh], v.Code AS [Mã phiếu khám], CAST(v.VisitAt AS date) AS [Ngày], v.VisitAt AS [Thời điểm],
    CASE v.Status WHEN 0 THEN N'Chờ khám' WHEN 1 THEN N'Đang khám' WHEN 2 THEN N'Đã khám xong' ELSE N'Đã hủy' END AS [Trạng thái],
    p.Code AS [Mã thú cưng], p.Name AS [Thú cưng], sp.Name AS [Loài], br.Name AS [Giống], o.FullName AS [Chủ nuôi], o.Phone AS [Điện thoại],
    u.FullName AS [Bác sĩ], v.Reason AS [Lý do khám], v.Diagnosis AS [Chẩn đoán], v.WeightKg AS [Cân nặng (kg)], v.Temperature AS [Nhiệt độ],
    v.FollowUpDate AS [Hẹn tái khám]
FROM Visits v {string.Format(Branch, "v")}
JOIN Pets p ON p.Id = v.PetId JOIN Owners o ON o.Id = v.OwnerId
LEFT JOIN Species sp ON sp.Id = p.SpeciesId LEFT JOIN Breeds br ON br.Id = p.BreedId LEFT JOIN Users u ON u.Id = v.VetId"),

        ("rpt_TiemPhong", "Mũi tiêm: vắc-xin, mũi số, thú cưng, chủ nuôi, ngày tiêm tiếp, trạng thái nhắc lịch", $@"
SELECT va.Id AS VaccinationId, va.BranchId, b.Name AS [Chi nhánh], va.Date AS [Ngày tiêm], va.VaccineName AS [Vắc-xin], va.DoseNumber AS [Mũi số],
    va.LotNo AS [Số lô], p.Code AS [Mã thú cưng], p.Name AS [Thú cưng], sp.Name AS [Loài], o.FullName AS [Chủ nuôi], o.Phone AS [Điện thoại],
    va.NextDueDate AS [Ngày tiêm tiếp], CASE WHEN va.ReminderDone = 1 THEN N'Đã nhắc' ELSE N'Chưa nhắc' END AS [Nhắc lịch], u.FullName AS [Bác sĩ]
FROM Vaccinations va {string.Format(Branch, "va")}
JOIN Pets p ON p.Id = va.PetId JOIN Owners o ON o.Id = p.OwnerId LEFT JOIN Species sp ON sp.Id = p.SpeciesId LEFT JOIN Users u ON u.Id = va.VetId"),

        ("rpt_LichHen", "Lịch hẹn: ngày giờ, trạng thái, khách hàng, thú cưng, dịch vụ, bác sĩ", $@"
SELECT a.Id AS AppointmentId, a.BranchId, b.Name AS [Chi nhánh], a.Code AS [Mã lịch hẹn], CAST(a.StartAt AS date) AS [Ngày], a.StartAt AS [Giờ hẹn],
    CASE a.Status WHEN 0 THEN N'Đã đặt' WHEN 1 THEN N'Đã xác nhận' WHEN 2 THEN N'Đã đến (chờ khám)' WHEN 3 THEN N'Hoàn thành' WHEN 4 THEN N'Đã hủy' ELSE N'Không đến' END AS [Trạng thái],
    o.FullName AS [Khách hàng], o.Phone AS [Điện thoại], p.Name AS [Thú cưng], s.Name AS [Dịch vụ], u.FullName AS [Bác sĩ], a.Reason AS [Lý do]
FROM Appointments a {string.Format(Branch, "a")}
LEFT JOIN Owners o ON o.Id = a.OwnerId LEFT JOIN Pets p ON p.Id = a.PetId LEFT JOIN ServiceItems s ON s.Id = a.ServiceItemId LEFT JOIN Users u ON u.Id = a.VetId"),

        ("rpt_NoiTru", "Nội trú / lưu chuồng: chuồng, thú cưng, ngày nhận – xuất, số ngày, giá / ngày", $@"
SELECT ad.Id AS AdmissionId, ad.BranchId, b.Name AS [Chi nhánh], ad.Code AS [Mã phiếu],
    CASE ad.Kind WHEN 0 THEN N'Nằm viện điều trị' ELSE N'Lưu chuồng (gửi giữ)' END AS [Hình thức],
    CASE ad.Status WHEN 0 THEN N'Đang lưu chuồng' WHEN 1 THEN N'Đã xuất' ELSE N'Đã hủy' END AS [Trạng thái],
    CAST(ad.AdmitAt AS date) AS [Ngày nhận], ad.DischargeAt AS [Ngày xuất], c.Code AS [Chuồng], p.Name AS [Thú cưng], o.FullName AS [Chủ nuôi],
    o.Phone AS [Điện thoại], u.FullName AS [Bác sĩ], DATEDIFF(day, ad.AdmitAt, COALESCE(ad.DischargeAt, GETDATE())) + 1 AS [Số ngày], ad.DailyRate AS [Giá / ngày]
FROM Admissions ad {string.Format(Branch, "ad")}
JOIN Pets p ON p.Id = ad.PetId JOIN Owners o ON o.Id = ad.OwnerId LEFT JOIN Cages c ON c.Id = ad.CageId LEFT JOIN Users u ON u.Id = ad.VetId"),

        ("rpt_KhachHang", "Khách hàng: số thú cưng, số lượt khám, lần khám gần nhất, tổng chi tiêu, còn nợ", $@"
SELECT o.Id AS OwnerId, o.BranchId, b.Name AS [Chi nhánh tạo], o.Code AS [Mã khách], o.FullName AS [Khách hàng], o.Phone AS [Điện thoại],
    o.Email AS [Email], o.Address AS [Địa chỉ], CAST(o.CreatedAt AS date) AS [Ngày tạo],
    (SELECT COUNT(*) FROM Pets p WHERE p.OwnerId = o.Id) AS [Số thú cưng],
    (SELECT COUNT(*) FROM Visits v WHERE v.OwnerId = o.Id AND v.Status <> 3) AS [Số lượt khám],
    (SELECT MAX(v.VisitAt) FROM Visits v WHERE v.OwnerId = o.Id AND v.Status <> 3) AS [Lần khám gần nhất],
    COALESCE((SELECT SUM(i.Total) FROM Invoices i WHERE i.OwnerId = o.Id AND i.Status IN (1, 2, 3)), 0) AS [Tổng chi tiêu],
    COALESCE((SELECT SUM(i.Total - i.Paid) FROM Invoices i WHERE i.OwnerId = o.Id AND i.Status IN (1, 2)), 0) AS [Còn nợ]
FROM Owners o {string.Format(Branch, "o")}
WHERE o.IsActive = 1"),

        ("rpt_TonKho", "Tồn kho theo chi nhánh: số lượng, tồn tối thiểu, giá vốn, giá trị tồn, hạn dùng gần nhất", $@"
SELECT ps.BranchId, b.Name AS [Chi nhánh], pr.Code AS [Mã hàng], pr.Name AS [Tên hàng], {ProductKindText("pr.Kind")} AS [Nhóm], pr.Unit AS [ĐVT],
    ps.Quantity AS [Tồn kho], pr.MinStock AS [Tồn tối thiểu], ps.CostPrice AS [Giá vốn], ps.Quantity * ps.CostPrice AS [Giá trị tồn], pr.SalePrice AS [Giá bán],
    (SELECT MIN(pb.ExpiryDate) FROM ProductBatches pb WHERE pb.BranchId = ps.BranchId AND pb.ProductId = ps.ProductId AND pb.Quantity > 0) AS [Hạn dùng gần nhất]
FROM ProductStocks ps JOIN Products pr ON pr.Id = ps.ProductId {string.Format(Branch, "ps")}
WHERE pr.IsActive = 1"),
    };

    public static async Task EnsureAsync(PetDbContext db, ILogger? log = null)
    {
        foreach (var (name, _, select) in All)
        {
            try { await db.Database.ExecuteSqlRawAsync($"CREATE OR ALTER VIEW [dbo].[{name}] AS {select}"); }
            catch (Exception ex) { log?.LogWarning(ex, "Không tạo được view báo cáo {View}", name); }
        }
    }

    private const string SeededKey = "DynamicReports.Seeded";

    /// <summary>Báo cáo động mẫu (chỉ tạo một lần – quản trị xóa đi sẽ không tạo lại)</summary>
    public static async Task SeedSamplesAsync(PetDbContext db)
    {
        if (await db.SystemSettings.AnyAsync(s => s.Key == SeededKey)) return;
        if (!await db.DynamicReports.AnyAsync())
        {
            db.DynamicReports.AddRange(
                new DynamicReport
                {
                    Name = "Danh sách hóa đơn", GroupName = "Thu ngân", Source = ReportSource.View, SortOrder = 1,
                    Description = "Mẫu báo cáo từ View: hóa đơn trong kỳ theo chi nhánh, lọc thêm theo trạng thái.",
                    ViewName = "dbo.rpt_HoaDon", DateColumn = "Ngày", BranchColumn = "BranchId",
                    Columns = "Mã hóa đơn, Ngày, Khách hàng, Điện thoại, Thú cưng, Trạng thái, Tổng tiền, Đã thu, Còn nợ, Người lập",
                    WhereClause = "StatusCode <> 0", OrderBy = "[Thời điểm] DESC", ShowTotals = true
                },
                new DynamicReport
                {
                    Name = "Doanh thu theo nhóm dịch vụ / hàng hóa", GroupName = "Doanh thu", Source = ReportSource.Sql, SortOrder = 2,
                    Description = "Mẫu câu lệnh SQL có tham số kỳ báo cáo và chi nhánh, kèm biểu đồ.",
                    SqlText = """
SELECT [Nhóm], COUNT(DISTINCT InvoiceId) AS [Số hóa đơn], SUM([Số lượng]) AS [Số lượng],
       SUM([Thành tiền]) AS [Doanh thu], SUM([Lãi gộp]) AS [Lãi gộp]
FROM rpt_DoanhThuChiTiet
WHERE [Ngày] >= @From AND [Ngày] < @ToEnd
  AND (@BranchId IS NULL OR BranchId = @BranchId)
GROUP BY [Nhóm]
ORDER BY [Doanh thu] DESC
""",
                    ChartLabel = "Nhóm", ChartValue = "Doanh thu", ShowTotals = true
                },
                new DynamicReport
                {
                    Name = "Khách hàng lâu chưa quay lại", GroupName = "Chăm sóc khách hàng", Source = ReportSource.Sql, SortOrder = 3,
                    Description = "Mẫu tham số tự khai báo: khách hàng không đến khám trong N tháng gần đây, sắp xếp theo chi tiêu.",
                    SqlText = """
SELECT [Mã khách], [Khách hàng], [Điện thoại], [Số thú cưng], [Số lượt khám], [Lần khám gần nhất], [Tổng chi tiêu]
FROM rpt_KhachHang
WHERE [Lần khám gần nhất] < DATEADD(month, -@SoThang, GETDATE())
  AND [Tổng chi tiêu] >= @ChiTieuToiThieu
  AND (@BranchId IS NULL OR BranchId = @BranchId)
ORDER BY [Tổng chi tiêu] DESC
""",
                    ParametersJson = System.Text.Json.JsonSerializer.Serialize(new[]
                    {
                        new ReportParam { Name = "SoThang", Label = "Không đến khám trong (tháng)", Type = "int", Default = "6" },
                        new ReportParam { Name = "ChiTieuToiThieu", Label = "Tổng chi tiêu tối thiểu", Type = "decimal", Default = "0" }
                    }),
                    ShowTotals = false
                },
                new DynamicReport
                {
                    Name = "Lượt khám theo bác sĩ và loài", GroupName = "Khám chữa bệnh", Source = ReportSource.Sql, SortOrder = 4,
                    Description = "Mẫu tham số danh sách chọn: lọc theo trạng thái lượt khám.",
                    SqlText = """
SELECT COALESCE([Bác sĩ], N'(Chưa phân công)') AS [Bác sĩ],
       SUM(CASE WHEN [Loài] = N'Chó' THEN 1 ELSE 0 END) AS [Chó],
       SUM(CASE WHEN [Loài] = N'Mèo' THEN 1 ELSE 0 END) AS [Mèo],
       SUM(CASE WHEN [Loài] NOT IN (N'Chó', N'Mèo') OR [Loài] IS NULL THEN 1 ELSE 0 END) AS [Loài khác],
       COUNT(*) AS [Tổng lượt khám]
FROM rpt_LuotKham
WHERE [Ngày] >= @From AND [Ngày] < @ToEnd
  AND (@BranchId IS NULL OR BranchId = @BranchId)
  AND (@TrangThai = N'' OR [Trạng thái] = @TrangThai)
GROUP BY [Bác sĩ]
ORDER BY [Tổng lượt khám] DESC
""",
                    ParametersJson = System.Text.Json.JsonSerializer.Serialize(new[]
                    {
                        new ReportParam { Name = "TrangThai", Label = "Trạng thái", Type = "select", Default = "Đã khám xong",
                            Options = "=Tất cả\nĐã khám xong\nĐang khám\nChờ khám\nĐã hủy" }
                    }),
                    ChartLabel = "Bác sĩ", ChartValue = "Tổng lượt khám", ShowTotals = true
                });
        }
        db.SystemSettings.Add(new SystemSetting { Key = SeededKey, Value = "1" });
        await db.SaveChangesAsync();
    }
}
