# Hướng dẫn sử dụng nhanh

## Làm việc theo chi nhánh (chuỗi phòng khám)
- Góc trên màn hình hiện **chi nhánh đang làm việc**. Người được gán nhiều chi nhánh bấm vào để chuyển chi nhánh.
- Mọi thao tác (lịch hẹn, khám, tiêm, lưu chuồng, hóa đơn, nhập kho…) ghi vào chi nhánh đang chọn; danh sách chỉ hiện dữ liệu của chi nhánh đó.
- Số chứng từ có tiền tố mã chi nhánh, VD `Q7-HD2611-0001`, `Q7-PK2611-0012`.
- **Dùng chung toàn chuỗi**: bảng giá dịch vụ, danh mục thuốc – hàng hóa, loài – giống.
- **Khách hàng & hồ sơ thú cưng**: dùng chung hoặc riêng từng chi nhánh tùy cài đặt (*Quản trị › Thông tin chuỗi & tham số*).
  Khi dùng chung, hồ sơ thú cưng hiện lượt khám, mũi tiêm ở **mọi chi nhánh** (gắn nhãn tên chi nhánh); phiếu khám của chi nhánh khác **chỉ xem**.

## Ngôn ngữ giao diện (Tiếng Việt · English · 한국어)
- **Chọn ngôn ngữ**: trên trang đăng nhập (nút ở góc trên phải) hoặc menu tài khoản › *Ngôn ngữ*. Lựa chọn được lưu theo trình duyệt và hồ sơ người dùng.
- Số tiền, ngày tháng hiển thị theo định dạng của ngôn ngữ (VD tiếng Hàn: `2026-11-16`).
- Dữ liệu do người dùng nhập (tên khách, thú cưng, dịch vụ, thuốc, chẩn đoán…) giữ nguyên, không dịch.
- **Chỉnh sửa bản dịch** (*Quản trị › Ngôn ngữ & bản dịch*, quyền *Thông tin phòng khám, nhật ký hệ thống*):
  - **Bản dịch** → tìm câu, sửa trực tiếp, **Lưu trang này**. Tab *Chưa dịch* liệt kê câu còn thiếu (giao diện hiển thị câu mới sẽ tự bổ sung vào danh sách).
  - Câu có `{0}`, `{1}` là mẫu câu – giữ nguyên các chỗ này (là tên, mã, số được điền vào).
  - Để trống = dùng bản kế thừa / văn bản gốc. Có thể sửa cả *Tiếng Việt* để đổi cách gọi (để trống = giữ nguyên).
  - **Xuất CSV / Nhập CSV** để đưa cho người dịch sửa bằng Excel (cột 1 văn bản gốc, cột 2 bản dịch, lưu UTF-8).
  - Câu đã sửa được giữ nguyên khi cập nhật phần mềm (bản dịch có sẵn chỉ bổ sung câu mới).
- **Theo vùng miền**: *Thêm ngôn ngữ / vùng miền* → mã (VD `vi-Nam`, `en-GB`), tên hiển thị, **Kế thừa từ** ngôn ngữ gốc → chỉ sửa những câu khác biệt; câu không sửa lấy từ ngôn ngữ gốc. Có thể đặt định dạng số / ngày riêng.
- **Đặt mặc định**: ngôn ngữ cho người dùng chưa chọn.

## Giao diện (phông chữ, màu chủ đạo, vị trí menu)
- **Giao diện của tôi**: menu tài khoản › *Giao diện*. Tắt *Dùng giao diện mặc định của hệ thống* rồi chọn:
  - **Màu chủ đạo**: 11 màu có sẵn hoặc màu tùy chọn (ô chọn màu). Nút, liên kết, biểu tượng, biểu đồ, mục menu đang chọn đổi theo; màu sáng / đậm phụ trợ tự tính.
  - **Phông chữ** (Segoe UI, Arial, Tahoma, Roboto, Inter, Be Vietnam Pro, Nunito, Open Sans, Montserrat, Noto Sans, Times New Roman) và **cỡ chữ** 13–16px. Phông nhãn *web* tải từ Google Fonts – máy không có Internet sẽ dùng phông có sẵn.
  - **Vị trí menu**: trái, phải, trên, dưới. Menu trên / dưới hiển thị theo nhóm, rê chuột hoặc bấm vào nhóm để mở. Điện thoại / màn hình hẹp: menu luôn trượt ra bằng nút ☰.
  - **Màu menu**: tối, sáng hoặc theo màu chủ đạo.
  - Thay đổi được xem trước ngay; bấm **Lưu** để áp dụng. Giao diện lưu theo tài khoản (dùng trên mọi máy). Bật lại *Dùng giao diện mặc định…* + **Lưu** để quay về mặc định.
- **Mặc định toàn hệ thống** (*Quản trị › Giao diện*, quyền *Thông tin phòng khám, nhật ký hệ thống*): áp dụng cho trang đăng nhập và mọi người dùng chưa chọn giao diện riêng.

## Lễ tân
1. **Khách gọi đặt lịch**: *Lịch hẹn › Đặt lịch hẹn* → gõ tên / SĐT khách (chưa có thì *+ Khách hàng mới*) → chọn thú cưng, dịch vụ, bác sĩ, giờ. Hệ thống cảnh báo nếu bác sĩ đã có lịch trùng giờ.
2. **Khách đến theo lịch**: *Lịch hẹn* → **Khách đến** → phiếu khám vào hàng chờ.
3. **Khách vãng lai**: *Tạo nhanh › Tiếp nhận khám* → chọn khách, thú cưng, dịch vụ ban đầu.
4. **Nhắc tiêm**: *Tiêm phòng & nhắc lịch* → tab *Cần nhắc lịch* → gọi điện → **Đặt lịch** hoặc **Đã nhắc**. Có thể xuất Excel danh sách gọi điện.
5. **Lưu chuồng**: *Nội trú / lưu chuồng* → bấm chuồng trống → chọn khách, thú cưng, giá/ngày, ghi đồ gửi kèm.
6. **Thêm chuồng**: trên *Sơ đồ chuồng* bấm **Thêm chuồng** (hoặc *Danh mục › Chuồng lưu trú*) → nhập mã, tên / vị trí, loại, giá / ngày.
   Chuồng thuộc chi nhánh đang làm việc; mã chuồng chỉ cần không trùng trong chi nhánh (các chi nhánh có thể cùng dùng mã S01, M01…).
   Người có quyền *Nội trú / lưu chuồng* hoặc *Danh mục* đều thêm / sửa được chuồng.

## Bác sĩ
1. *Khám bệnh* (hàng chờ) → **Bắt đầu khám**.
2. Nhập sinh hiệu, triệu chứng, khám lâm sàng, **chẩn đoán**, hướng điều trị, lời dặn, hẹn tái khám.
3. Thêm **dịch vụ** (xét nghiệm, siêu âm…), **đơn thuốc** (chọn thuốc → tự điền giá, gợi ý cách dùng, hiện tồn kho).
4. **Tiêm phòng**: chọn vắc-xin → tự gợi ý ngày hẹn mũi tiếp → *Ghi nhận mũi tiêm*.
5. **Kết thúc khám & lập hóa đơn** → hóa đơn nháp chuyển thu ngân; nếu có ngày tái khám hệ thống tự tạo lịch hẹn.
6. Cần nằm viện: *Chuyển nằm viện* → chọn chuồng; ghi theo dõi hằng ngày trong phiếu nội trú.
7. In *phiếu khám & đơn thuốc* đưa khách.

## Thu ngân
1. *Hóa đơn* (tab *Chờ thu tiền*, số hóa đơn nháp hiện trên menu) → mở hóa đơn → kiểm tra / sửa dòng, giảm giá.
2. **Phát hành hóa đơn** (nhập số tiền thu ngay, hình thức) → kho tự trừ theo lô hạn gần nhất.
3. Khách trả thiếu → hóa đơn *Thanh toán một phần*, thu tiếp bằng **Thu tiền**. Công nợ xem tại hồ sơ khách và báo cáo *Công nợ*.
4. Bán lẻ (thức ăn, phụ kiện): *Bán hàng / lập hóa đơn* → *Lưu & phát hành*.
5. In **A5** hoặc **bill 80 mm**. Hủy hóa đơn (cần quyền) → hoàn nhập kho, ghi hoàn tiền.

## Kho / dược
- *Phiếu nhập kho* → chọn mặt hàng, số lô, hạn dùng, số lượng, giá nhập → **Lưu & ghi sổ** (giá vốn bình quân tự tính).
- *Thuốc & hàng hóa*: lọc *Hết / sắp hết hàng*, *Lô sắp hết hạn*; mở mặt hàng xem **thẻ kho**, **điều chỉnh tồn** theo kiểm kê, **xuất hủy** lô hết hạn.
- Mỗi chi nhánh có **kho riêng**: tồn kho, lô / hạn dùng, giá vốn bình quân, thẻ kho tính theo chi nhánh đang làm việc. Trang chi tiết mặt hàng hiện thêm tồn ở các chi nhánh khác.
- **Chuyển kho giữa chi nhánh**: *Kho thuốc › Chuyển kho chi nhánh* → **Lập phiếu chuyển kho** → chọn chi nhánh nhận, mặt hàng, số lượng → **Lưu & ghi sổ**.
  Ghi sổ trừ tồn chi nhánh xuất (theo lô hạn gần nhất trước) và cộng ngay vào chi nhánh nhận, giữ nguyên số lô, hạn dùng, giá vốn. Phiếu chỉ sửa / ghi sổ / xóa ở chi nhánh xuất; chi nhánh nhận xem ở tab *Nhận về*.

## Quản lý / Quản trị
- *Báo cáo*: doanh thu, lãi gộp, hiệu suất bác sĩ, lượt khám, công nợ, nhập – xuất – tồn, tiêm phòng (Excel, PDF).
  Ô chọn phạm vi: một chi nhánh hoặc **Toàn chuỗi** (người dùng toàn chuỗi). Báo cáo **So sánh chi nhánh**: lượt khám, nội trú, khách mới, doanh thu, lãi gộp, thực thu, công nợ, giá trị tồn kho.
- *Dịch vụ & bảng giá*, *Loài & giống*, *Chuồng lưu trú*.
- **Chi nhánh** (*Quản trị › Chi nhánh*): thêm chi nhánh (mã ≤ 6 ký tự – tiền tố số chứng từ, tên, địa chỉ, điện thoại in trên chứng từ). Chi nhánh đã có dữ liệu chỉ ngừng hoạt động, không xóa.
  Sau khi thêm: gán nhân viên → chuyển sang chi nhánh mới → khai báo chuồng → nhập kho (hoặc chuyển kho từ chi nhánh khác).
- **Người dùng**: chọn **chi nhánh làm việc** (một hoặc nhiều) hoặc *Làm việc ở mọi chi nhánh* (quản lý chuỗi). Quản trị hệ thống luôn làm được ở mọi chi nhánh.
  Quản lý một chi nhánh chỉ gán / bỏ được chi nhánh của mình. Thay đổi chi nhánh có hiệu lực từ lần đăng nhập sau. Danh sách chọn bác sĩ chỉ hiện bác sĩ của chi nhánh.
- *Vai trò & quyền*, *Thông tin chuỗi & tham số* (tên công ty, mã số thuế, lời cảm ơn trên hóa đơn, số ngày nhắc tiêm, cảnh báo hạn dùng, dùng chung / riêng khách hàng), *Nhật ký hệ thống*.
