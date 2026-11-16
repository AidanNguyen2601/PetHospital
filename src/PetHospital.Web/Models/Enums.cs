using System.ComponentModel.DataAnnotations;

namespace PetHospital.Web.Models;

public enum PetGender
{
    [Display(Name = "Không rõ")] Unknown = 0,
    [Display(Name = "Đực")] Male = 1,
    [Display(Name = "Cái")] Female = 2
}

public enum AppointmentStatus
{
    [Display(Name = "Đã đặt")] Booked = 0,
    [Display(Name = "Đã xác nhận")] Confirmed = 1,
    [Display(Name = "Đã đến (chờ khám)")] CheckedIn = 2,
    [Display(Name = "Hoàn thành")] Completed = 3,
    [Display(Name = "Đã hủy")] Cancelled = 4,
    [Display(Name = "Không đến")] NoShow = 5
}

public enum VisitStatus
{
    [Display(Name = "Chờ khám")] Waiting = 0,
    [Display(Name = "Đang khám")] InProgress = 1,
    [Display(Name = "Đã khám xong")] Completed = 2,
    [Display(Name = "Đã hủy")] Cancelled = 3
}

public enum ServiceKind
{
    [Display(Name = "Khám bệnh")] Exam = 0,
    [Display(Name = "Tiêm phòng")] Vaccination = 1,
    [Display(Name = "Xét nghiệm")] Lab = 2,
    [Display(Name = "Chẩn đoán hình ảnh")] Imaging = 3,
    [Display(Name = "Phẫu thuật / thủ thuật")] Surgery = 4,
    [Display(Name = "Điều trị")] Treatment = 5,
    [Display(Name = "Spa / Grooming")] Grooming = 6,
    [Display(Name = "Lưu chuồng / nội trú")] Boarding = 7,
    [Display(Name = "Khác")] Other = 9
}

public enum ProductKind
{
    [Display(Name = "Thuốc")] Medicine = 0,
    [Display(Name = "Vắc-xin")] Vaccine = 1,
    [Display(Name = "Vật tư y tế")] Supply = 2,
    [Display(Name = "Thức ăn / dinh dưỡng")] Food = 3,
    [Display(Name = "Phụ kiện")] Accessory = 4,
    [Display(Name = "Khác")] Other = 9
}

public enum StockMoveType
{
    [Display(Name = "Nhập kho")] Receipt = 0,
    [Display(Name = "Xuất bán / kê đơn")] Sale = 1,
    [Display(Name = "Hoàn nhập (hủy hóa đơn)")] Return = 2,
    [Display(Name = "Điều chỉnh tăng")] AdjustIn = 3,
    [Display(Name = "Điều chỉnh giảm / hủy hàng")] AdjustOut = 4,
    [Display(Name = "Chuyển kho đi")] TransferOut = 5,
    [Display(Name = "Nhận chuyển kho")] TransferIn = 6
}

public enum DocState
{
    [Display(Name = "Nháp")] Draft = 0,
    [Display(Name = "Đã ghi sổ")] Posted = 1,
    [Display(Name = "Đã hủy")] Cancelled = 2
}

public enum InvoiceStatus
{
    [Display(Name = "Nháp")] Draft = 0,
    [Display(Name = "Chưa thanh toán")] Unpaid = 1,
    [Display(Name = "Thanh toán một phần")] Partial = 2,
    [Display(Name = "Đã thanh toán")] Paid = 3,
    [Display(Name = "Đã hủy")] Cancelled = 4
}

public enum InvoiceLineKind
{
    [Display(Name = "Dịch vụ")] Service = 0,
    [Display(Name = "Thuốc / hàng hóa")] Product = 1,
    [Display(Name = "Khác")] Other = 2
}

public enum PaymentMethod
{
    [Display(Name = "Tiền mặt")] Cash = 0,
    [Display(Name = "Chuyển khoản")] Transfer = 1,
    [Display(Name = "Thẻ")] Card = 2,
    [Display(Name = "Ví điện tử")] EWallet = 3
}

public enum AdmissionKind
{
    [Display(Name = "Nằm viện điều trị")] Hospitalization = 0,
    [Display(Name = "Lưu chuồng (gửi giữ)")] Boarding = 1
}

public enum AdmissionStatus
{
    [Display(Name = "Đang lưu chuồng")] Active = 0,
    [Display(Name = "Đã xuất")] Discharged = 1,
    [Display(Name = "Đã hủy")] Cancelled = 2
}
