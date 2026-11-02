# Quyết định kiến trúc & Thiết kế hệ thống (ADR)

1. **Nguồn sự thật của Schema cơ sở dữ liệu**:
   - EF Core Migration là nguồn sự thật duy nhất của schema. Tệp `docs/database/schema-reference.sql` chỉ dùng để tham chiếu khi dựng entity/cấu hình ban đầu.
2. **Ca khám bệnh và Ca trực**:
   - Ca khám bệnh chỉ có ca Sáng (`MORNING`) và Chiều (`AFTERNOON`) để sinh slot khám.
   - Ca tối thuộc về ca trực của nhân viên (`duty_shifts`), không sinh slot khám đặt trước.
3. **Quản lý hủy ca và Slot**:
   - Khi bác sĩ hủy ca, lịch hẹn được chuyển sang bác sĩ thay thế hoặc dời lịch; sau đó xóa trực tiếp slot rỗng thay vì thêm các trạng thái phức tạp.
4. **Quota đặt lịch**:
   - Quota khám online (`online_slots`) và tại quầy (`max_patients - online_slots`) được quản lý theo cấu hình lịch trực `doctor_schedules`.
5. **BHYT (Bảo hiểm y tế)**:
   - Thẻ BHYT thuộc về bệnh nhân (`patients`), không thuộc tài khoản.
   - Nghiệp vụ kiểm tra và giảm trừ BHYT chỉ diễn ra ở bước thanh toán viện phí (`IBillingService`), kiểm tra thời hạn thẻ theo **ngày khám** thay vì ngày thanh toán.
6. **Mô hình tài khoản - bệnh nhân (N - N)**:
   - Tách rời tài khoản đặt lịch (`accounts`) và hồ sơ người khám (`patients`) thông qua bảng trung gian `account_patient_profiles`.
   - Phân định rõ quyền đặt lịch (`CanBook`: link `ACTIVE` hoặc `PENDING`) và quyền xem hồ sơ bệnh án (`CanViewRecords`: bắt buộc link `ACTIVE` và `can_view_records = true`).
7. **Lịch hẹn & Người đặt**:
   - `appointments.patient_id` lưu ID người được khám; `booked_by` lưu ID tài khoản đặt lịch.
8. **Quyền riêng tư hồ sơ y tế**:
   - Link `PENDING` được phép đặt lịch hộ nhưng không xem được bệnh án.
   - Xác minh liên kết diễn ra khi bệnh nhân check-in tại quầy hoặc được tài khoản người lớn xác nhận.
   - Người từ 18 tuổi trở lên bắt buộc phải có sự đồng ý trực tiếp mới được bật quyền xem bệnh án.
9. **Định danh Seed Lookup**:
   - ID của bảng `appointment_statuses` (1 đến 10) là cố định (`ValueGeneratedNever`) vì các index một phần và logic nghiệp vụ phụ thuộc chặt chẽ vào các ID này.
10. **Tài khoản và Email**:
    - `accounts.email` (unique không phân biệt hoa thường, nullable) và `payments.receipt_email` (nullable) chỉ là kênh liên lạc chưa xác minh;
    - Quên mật khẩu qua email (cần bảng token + SMTP) để sprint sau, hiện Admin đặt lại mật khẩu tại quầy.

