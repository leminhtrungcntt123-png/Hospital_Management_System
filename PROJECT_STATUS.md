# BẢN ĐẶC TẢ HIỆN TRẠNG DỰ ÁN & NGUỒN SỰ THẬT TỐI CAO (PROJECT STATUS & GROUND TRUTH)

* **Dự án:** Hệ thống Quản lý Bệnh viện & Đặt lịch khám (HIS - Hospital Information System)
* **Nền tảng kỹ thuật:** .NET 10, ASP.NET Core MVC / Web API, Entity Framework Core 10, PostgreSQL 16
* **Thời điểm cập nhật:** Tháng 11/2026
* **MỤC ĐÍCH FILE NÀY:** Đây là tài liệu phản ánh **100% HIỆN TRẠNG MÃ NGUỒN THỰC TẾ (GROUND TRUTH)**. Mọi Agent / AI (Gemini Web, Codex, ChatGPT...) khi tham gia phân tích hoặc viết mã cho dự án **BẮT BUỘC ĐỌC FILE NÀY ĐẦU TIÊN** và tuân thủ các quy tắc trong đây, **GHI ĐÈ HOÀN TOÀN** lên các tài liệu scaffold ban đầu như `init.md` hay `README.md`.

---

## 1. CẤU TRÚC SOLUTION & RÀNG BUỘC KIẾN TRÚC BẤT BIẾN

Solution chỉ bao gồm đúng **4 dự án chính + 1 dự án test**:
```text
Hospital_Management_System/
├── src/
│   ├── HIS.Domain/   (Class Library)  — 33 Entities, Enums. KHÔNG tham chiếu thư viện ngoài.
│   ├── HIS.DAL/      (Class Library)  — AppDbContext, EF Configurations (snake_case), Seed data, Migrations.
│   ├── HIS.BLL/      (Class Library)  — Services, DTOs, Validators (FluentValidation), State Machine, Options.
│   │                                    KHÔNG dùng Microsoft.AspNetCore.* để giữ khả năng gắn WinForms.
│   └── HIS.Web/      (ASP.NET Core)   — MVC Controllers, Razor Views, Web API, Cookie Auth, SPA MediCare UI.
└── tests/
    └── HIS.Tests/    (xUnit)          — Unit tests & Integration tests (Testcontainers PostgreSQL).
```

> [!CAUTION]
> **QUY TẮC BẤT BIẾN DÀNH CHO AI:**
> 1. **TUYỆT ĐỐI KHÔNG** tự ý bịa đặt hay tạo các project ảo giác như `HIS.Application`, `HIS.Infrastructure`. Toàn bộ DTOs và Services nằm tại `HIS.BLL`.
> 2. **TUYỆT ĐỐI KHÔNG** tự ý thêm bảng hoặc thêm cột mới vào cơ sở dữ liệu nếu chưa có sự đồng ý của nhóm.
> 3. **TUYỆT ĐỐI KHÔNG** đụng chạm hay sửa đổi phân hệ `/Staff/Schedule` (module phân ca của Trưởng khoa đang do thành viên khác phụ trách).

---

## 2. VÒNG ĐỜI LỊCH KHÁM & MÁY TRẠNG THÁI (APPOINTMENT STATE MACHINE)

Toàn bộ quy trình khám chữa bệnh được kiểm soát chặt chẽ bởi máy trạng thái tuần tự tại [`AppointmentStateMachine.cs`](file:///src/HIS.BLL/Common/AppointmentStateMachine.cs):

```text
       (Đặt Web)
           │
           ▼
[1. Pending] ──(Xác nhận OTP)──► [2. Confirmed]
                                      │
                                (Quầy tiếp nhận đối chiếu & cấp STT)
                                      │
                                      ▼
                                [3. CheckedIn]
                                      │
                                (Bàn điều dưỡng đo sinh hiệu)
                                      │
                                      ▼
                                [4. ReadyForDoctor]
                                      │
                                (Bác sĩ bấm "Bắt đầu khám")
                                      │
                                      ▼
                                [5. InProgress]
                                      │
                                (Bác sĩ lưu hồ sơ bệnh án)
                                      │
                                      ▼
                                [6. Completed]
```

### Quy tắc kiểm soát trạng thái & chống tranh chấp đồng thời:
1. **Kiểm tra tính hợp lệ:** Sử dụng hàm thuần `AppointmentStateMachine.CanTransition(current, next)`. (Hệ thống **KHÔNG CÓ** hàm `AppointmentStateMachine.Transition(...)`).
2. **Khóa chống tranh chấp (PostgreSQL Concurrency Lock):** Mọi giao dịch chuyển trạng thái hoặc ghi sinh hiệu phải chạy trong transaction có khóa phiên theo ID ca khám:
   ```csharp
   await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({appointmentId})", ct);
   ```
3. **Siết chặt máy chủ 2 đầu (Server-Side Enforcement):**
   - **Ghi sinh hiệu:** Chỉ cho phép khi ca khám đang ở `CheckedIn (3)` $\rightarrow$ lưu `vital_signs` $\rightarrow$ chuyển sang `ReadyForDoctor (4)` $\rightarrow$ ghi log `AppointmentStatusHistory`.
   - **Sửa sinh hiệu:** Chỉ cho phép khi ca khám vẫn đang ở `ReadyForDoctor (4)`. Nếu Bác sĩ đã bắt đầu khám (`StatusId >= 5`), server chặn sửa ngay lập tức.
   - **Bác sĩ bắt đầu khám:** Bắt buộc `appointment.StatusId == ReadyForDoctor` VÀ `appointment.VitalSigns != null` mới chuyển sang `InProgress (5)`. Nút bấm trên giao diện bị khóa nếu bệnh nhân chưa đo sinh hiệu.
   - **Bác sĩ lưu bệnh án:** Bắt buộc `appointment.StatusId == InProgress` mới cho chuyển sang `Completed (6)`.
4. **Phân định khóa ngoại:**
   - `vital_signs.recorded_by = nurse.Id` (FK trỏ đến `Nurse`).
   - `appointment_status_history.performed_by = accountId` (FK trỏ đến `Account`).

---

## 3. HIỆN TRẠNG CÁC PHÂN HỆ ĐÃ HOÀN THÀNH (COMPLETED MODULES)

### 3.1. Cổng người bệnh & Đặt lịch khám trực tuyến (Patient Portal)
* **Đặt lịch không cần đăng nhập (Guest Booking) + Xác thực OTP qua Email:** Bệnh nhân chọn chuyên khoa, bác sĩ, khung giờ khám $\rightarrow$ Hệ thống gửi mã OTP 6 số qua email $\rightarrow$ Xác thực thành công sinh mã lịch khám dạng `HIS-yyyyMMdd-XXXXXX`, trạng thái ban đầu là `Confirmed (2)`.
* **Quản lý hồ sơ gia đình:** Hỗ trợ tạo hồ sơ bản thân (`SELF`) và người thân (`SPOUSE`, `CHILD`, `PARENT`...).
* **Route:** `/Booking`, `/Patient/MyAppointments`, `/Patient/FamilyProfiles`.

### 3.2. Quầy tiếp nhận ngoại trú (`/Staff/Reception`)
* Do Điều dưỡng phụ trách (`[Authorize(Policy = "NurseOnly")]`).
* **Tab 1 — "Lịch đặt trên web":** Lọc các lịch khám hôm nay của chuyên khoa có trạng thái `Confirmed (2)`. Đối chiếu danh tính, bấm "Tiếp nhận & cấp số" $\rightarrow$ Hệ thống dùng khóa advisory lock sinh số thứ tự `O01, O02...` và chuyển trạng thái sang `CheckedIn (3)`.
* **Tab 2 — "Đăng ký khách vãng lai":** Tiếp nhận bệnh nhân đến trực tiếp bệnh viện $\rightarrow$ Cấp số thứ tự `W01, W02...` $\rightarrow$ Trạng thái `CheckedIn (3)`.
* **Tab 3 — "Hàng chờ đã tiếp nhận":** Danh sách các ca khám hôm nay có `StatusId IN (3, 4, 5)`.

### 3.3. Bàn đo Sinh hiệu Y tá (`/Workspace/staff/vitals`)
* **Nghiệp vụ:** Hiển thị hàng đợi các ca `CheckedIn (3)` (chưa đo) và `ReadyForDoctor (4)` (đã đo chờ khám) trong khoa.
* **Biểu mẫu đo sinh hiệu:**
  - 5 chỉ số bắt buộc: Mạch ($30 - 220\text{ l/p}$), Huyết áp tâm thu ($50 - 260\text{ mmHg}$), Huyết áp tâm trương ($30 - 160\text{ mmHg}$), Thân nhiệt ($34.0 - 43.0^\circ\text{C}$), Cân nặng ($1.0 - 300.0\text{ kg}$).
  - 3 chỉ số tùy chọn: Nhịp thở ($8 - 60\text{ l/p}$), $\text{SpO}_2$ ($50 - 100\%$), Chiều cao ($30 - 250\text{ cm}$).
  - Ràng buộc y khoa: $\text{SystolicBp} - \text{DiastolicBp} \ge 10\text{ mmHg}$.
  - Tính năng trên giao diện: Tự động tính chỉ số khối cơ thể $\text{BMI}$ thời gian thực, cảnh báo màu sắc bất thường.
* **REST API:** Cung cấp qua [`StaffWorkspaceApiController.cs`](file:///src/HIS.Web/Areas/Staff/Controllers/StaffWorkspaceApiController.cs) bảo vệ bằng `RequestVerificationToken` (Anti-CSRF).

### 3.4. Bàn làm việc Điều dưỡng & Hồ sơ (`/Workspace/staff/dashboard` & `/Workspace/staff/patients`)
* **4 thẻ KPI thời gian thực:**
  1. *Lịch khám hôm nay:* Tổng số ca trong ngày tại khoa (`StatusId IN (2, 3, 4, 5, 6)`).
  2. *Chờ xử lý:* Tổng ca chờ (`StatusId IN (3, 4)`) kèm chú thích số ca *Chờ đo* và số ca *Chờ khám*.
  3. *Đang khám:* Số ca Bác sĩ đang khám (`StatusId == 5`).
  4. *Hoàn thành:* Số ca đã xong (`StatusId == 6`).
* **Bảng công việc hôm nay:** Hiển thị các ca cần ưu tiên xử lý (nút tắt đo sinh hiệu hoặc tiếp nhận nhanh).
* **Tra cứu người khám:** Tìm kiếm tức thời hồ sơ bệnh nhân từng khám tại khoa theo tên, SĐT, CCCD.

### 3.5. Màn hình Khám & Kê đơn của Bác sĩ (`/Staff/Consultation`)
* Phân quyền: `[Authorize(Policy = "DoctorOnly")]`.
* Thanh `<div class="vitals-strip">` tự động nạp Mạch, Huyết áp, Thân nhiệt, $\text{SpO}_2$, Cân nặng từ bảng `vital_signs`.
* Khóa chặt nút "Bắt đầu khám":
  - Nếu ca khám ở `CheckedIn (3)` (chưa đo sinh hiệu): Nút bị `disabled` kèm cảnh báo màu cam.
  - Nếu ca khám ở `ReadyForDoctor (4)`: Nút kích hoạt $\rightarrow$ Bấm nút gọi `POST /Staff/Consultation/StartExam` chuyển sang `InProgress (5)` rồi mới mở khóa form khám bệnh.
* Nút "Hoàn tất khám": Tạo hồ sơ bệnh án [`MedicalRecord`](file:///src/HIS.Domain/MedicalRecord.cs) và chuyển trạng thái sang `Completed (6)`.

---

## 4. DANH SÁCH TÀI KHOẢN TEST SEED DỮ LIỆU SẴN (DEV DATA SEEDER)

* Mật khẩu chung cho tất cả tài khoản seed: **`Admin@123456`**

| Tài khoản (Username) | Họ và tên | Vai trò | Chuyên khoa / Vị trí | Đường dẫn truy cập chính |
| :--- | :--- | :--- | :--- | :--- |
| `admin` | Quản trị viên | ADMIN | Quản trị hệ thống | `/Admin/Management` |
| `bs_nam` | BS. Nguyễn Văn Nam | DOCTOR | Khoa Nội tổng quát (P.101) | `/Staff/Consultation` |
| `bs_huy` | BS. Lê Quang Huy | DOCTOR | Khoa Nội tổng quát (P.102) | `/Staff/Consultation` |
| `tk_thang` | BS. Trần Văn Thắng | DEPARTMENT_HEAD | Trưởng khoa Nội tổng quát | `/Staff/Consultation` (Tab quản lý khoa) |
| `yt_lan` | Điều dưỡng Hoàng Lan | NURSE | Khoa Nội tổng quát | `/Workspace/staff/dashboard`, `/Staff/Reception`, `/Workspace/staff/vitals` |
| `bn_an` | Nguyễn Văn An | PATIENT | Bệnh nhân mẫu | `/Booking`, `/Patient/MyAppointments` |

---

## 5. HƯỚNG DẪN KHI YÊU CẦU AI PHÂN TÍCH HOẶC VIẾT CODE

Khi gửi mã nguồn dự án cho **Gemini Web** hoặc **Codex**, hãy dùng câu lệnh sau:
> *"Tôi gửi bạn mã nguồn dự án HIS. Hãy đọc file `PROJECT_STATUS.md` trước tiên. Đây là bản đặc tả hiện trạng thực tế và nguồn sự thật duy nhất (Ground Truth) của dự án. Mọi phân tích, kiến trúc và code mới phải bám sát 100% vào các thực thể và quy tắc chuyển trạng thái đã mô tả trong file này, tuyệt đối không tạo thêm project ảo hay sửa đổi phân hệ /Staff/Schedule."*
