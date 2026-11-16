# PetCare – Phần mềm quản lý phòng khám thú y

ASP.NET Core 8 MVC + SQL Server 2022 + EF Core 8, chạy bằng Docker (cùng mô hình với CRM, EAM… trong thư mục SourceCode).

## 1. Chạy nhanh

**Phương án A – SQL Server riêng** (cổng web 8160, SQL 1441)
```
copy .env.example .env      (đổi PET_SA_PASSWORD nếu cần)
docker compose up -d --build
```
hoặc nhấp đúp `run-pet-A.cmd`. Mở `http://localhost:8160`.

**Phương án B – dùng chung SQL Server đang chạy** (VD: `healthtracker-db`): nhấp đúp `run-pet-B.cmd`.
Script tự tìm container SQL Server, tạo login `pet_app` (chỉ quyền tạo database), ghi `.env`, build và chạy `pet-web`.
Kết quả ghi vào `deploy.log`.

Kiểm tra biên dịch nhanh trên máy có .NET 8 SDK: nhấp đúp `build-check.cmd` → xem `build.log` (`EXITCODE=0` là đạt).

Lần chạy đầu ứng dụng tự tạo database `PET_DB` và nạp dữ liệu mẫu (tắt bằng `PET_SEED_SAMPLE=false`).

**Mô phỏng luồng dữ liệu** (nạp thêm, không xóa dữ liệu có sẵn): *Quản trị › Dữ liệu mô phỏng* → chọn số ngày (7–90) và lượng khách.
Sinh theo từng ngày đúng luồng: đặt lịch → khách đến / không đến → phiếu khám (chẩn đoán, dịch vụ, đơn thuốc, tiêm) → tái khám / mũi nhắc lại
→ nằm viện, lưu chuồng → hóa đơn (trừ kho FEFO) → thu tiền ngay / trả sau / còn nợ → nhập kho bổ sung; hôm nay có hàng chờ, hóa đơn nháp, 7 ngày tới có lịch hẹn.
Chạy bằng dòng lệnh: `docker exec pet-web dotnet PetHospital.Web.dll --simulate 30 2` (số ngày, mức khách 1–3). Mã nguồn: `Data/FlowSimulator.cs`.

| Tài khoản | Mật khẩu | Vai trò |
|---|---|---|
| admin | Admin@123 | Quản trị hệ thống |
| quanly | Pet@123 | Quản lý phòng khám + bác sĩ |
| bs.an, bs.binh, bs.chau | Pet@123 | Bác sĩ thú y |
| letan | Pet@123 | Lễ tân / chăm sóc khách hàng |
| thungan | Pet@123 | Thu ngân |
| kho | Pet@123 | Kho / dược |

## 2. Chức năng

| Nhóm | Chức năng |
|---|---|
| Bảng điều khiển | Lịch hẹn hôm nay, hàng chờ khám, đang nội trú, tiền thu hôm nay / tháng, công nợ, biểu đồ 30 ngày, doanh thu theo nhóm, nhắc tiêm, cảnh báo hết hàng / sắp hết hạn |
| Khách hàng & thú cưng | Hồ sơ chủ nuôi (SĐT không trùng, công nợ, tổng chi tiêu), hồ sơ thú cưng (loài – giống, giới tính, tuổi, cân nặng, vi mạch, triệt sản, dị ứng – cảnh báo đỏ khi khám), lịch sử khám, sổ tiêm, biểu đồ cân nặng, in bệnh án; tìm nhanh theo SĐT / mã / vi mạch |
| Lịch hẹn | Xem theo ngày / tuần, lọc bác sĩ, cảnh báo trùng lịch bác sĩ, xác nhận / không đến / hủy, "Khách đến" tạo phiếu khám vào hàng chờ |
| Khám bệnh | Hàng chờ, tiếp nhận khách vãng lai, phiếu khám: sinh hiệu, triệu chứng, khám lâm sàng, chẩn đoán, điều trị, lời dặn, hẹn tái khám (tự tạo lịch hẹn); chỉ định dịch vụ, kê đơn thuốc (kiểm tra tồn kho, gợi ý liều dùng), ghi nhận tiêm phòng; kết thúc khám tự lập hóa đơn chuyển thu ngân; in phiếu khám & đơn thuốc; mở lại phiếu khi hóa đơn còn nháp |
| Tiêm phòng | Sổ tiêm từng thú cưng, hẹn mũi nhắc lại theo vắc-xin, danh sách cần nhắc (quá hạn / đến hạn N ngày) xuất Excel để gọi điện, đặt lịch từ danh sách nhắc |
| Nội trú / lưu chuồng | Sơ đồ chuồng (trống / đang có thú / cách ly), nhận lưu chuồng hoặc nằm viện từ phiếu khám, theo dõi hằng ngày (nhiệt độ, cân nặng, ăn uống, xử trí), xuất chuồng tự tính số ngày × giá và lập hóa đơn |
| Thu ngân | Hóa đơn từ phiếu khám / lưu chuồng / bán lẻ (POS), giảm giá, phát hành (trừ kho FEFO, ghi giá vốn), thu tiền nhiều lần, nhiều hình thức (tiền mặt, chuyển khoản, thẻ, ví), hủy hóa đơn (hoàn nhập kho, ghi hoàn tiền), in A5 hoặc bill 80 mm |
| Kho thuốc | Danh mục thuốc / vắc-xin / vật tư / thức ăn / phụ kiện, phiếu nhập theo lô – hạn dùng, giá vốn bình quân, xuất theo hạn gần nhất trước (FEFO), thẻ kho, điều chỉnh tồn kiểm kê, xuất hủy lô hết hạn, cảnh báo tồn tối thiểu và hạn dùng |
| Báo cáo | Doanh thu theo ngày & hình thức thanh toán; doanh thu – giá vốn – lãi gộp theo dịch vụ / hàng hóa; hiệu suất bác sĩ; lượt khám & chẩn đoán thường gặp; công nợ; nhập – xuất – tồn; thống kê tiêm phòng. Xuất Excel, in / PDF |
| Danh mục | Dịch vụ & bảng giá (khám, tiêm, xét nghiệm, chẩn đoán hình ảnh, phẫu thuật, điều trị, spa, lưu chuồng), loài & giống, chuồng lưu trú |
| Quản trị | Người dùng (nhiều vai trò, cờ bác sĩ), ma trận vai trò × quyền chức năng (ẩn giá vốn theo quyền), thông tin phòng khám in trên chứng từ, nhật ký hệ thống tự động (trước → sau, IP) |

## 3. Cấu trúc

```
PetHospital/
├─ src/PetHospital.Web/
│  ├─ Models/          Thực thể: khách hàng, thú cưng, lịch hẹn, phiếu khám, tiêm phòng, nội trú, kho, hóa đơn
│  ├─ Data/            PetDbContext (quan hệ khai báo tường minh, nhật ký tự động), DbSeeder, SampleData
│  ├─ Infrastructure/  Phân quyền, đánh số chứng từ, StockService (FEFO), InvoiceService, Lookups, Excel
│  ├─ Controllers/     MVC controllers
│  ├─ Views/           Razor views (Bootstrap 5, responsive)
│  └─ wwwroot/         css, js, lib (bootstrap, jquery)
├─ Dockerfile, docker-compose.yml (A), docker-compose.shared-db.yml (B)
├─ run-pet-A.cmd, run-pet-B.cmd, deploy-pet-shared-db.ps1, build-check.cmd
└─ docs/HUONG-DAN-SU-DUNG.md
```

## 4. Ghi chú kỹ thuật
- Database tạo bằng `EnsureCreated` (giống CRM / EAM). Khóa ngoại mặc định `NO ACTION`; chỉ dòng chi tiết (dòng hóa đơn, dòng phiếu nhập, dịch vụ / thuốc của phiếu khám, ghi chép nội trú) xóa theo chứng từ cha.
- Tiền tệ `decimal(18,2)`, số lượng / cân nặng `decimal(18,3)`.
- Kho trừ khi **phát hành hóa đơn** (không trừ khi kê đơn) để tránh lệch tồn khi khách không lấy thuốc; hủy hóa đơn hoàn nhập đúng lô.
- Sao lưu: dùng `docker exec pet-sqlserver /opt/mssql-tools18/bin/sqlcmd ... BACKUP DATABASE PET_DB ...` hoặc công cụ sao lưu SQL Server sẵn có.
