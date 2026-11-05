# Kiến trúc HIS (Hệ thống hỗ trợ đặt lịch khám và quản lý hồ sơ bệnh án)

Tài liệu mô tả **code đang có trong repo**, không mô tả ước nguyện. Mọi chỗ lệch `init.md` mục 1 đều ghi rõ ở cuối và được giải thích trong [docs/DECISIONS.md](DECISIONS.md).

Nguồn sự thật về cấu trúc dữ liệu: [docs/database/schema-reference.sql](database/schema-reference.sql) (DDL tham chiếu) và [docs/database/erd.md](database/erd.md) (ERD sinh từ DB thật `his_ci`).

## 1. Sơ đồ project

```
HIS.sln
├─ src/HIS.Domain      (net10.0)   entity + enum, không tham chiếu gì
├─ src/HIS.DAL         (net10.0)   → Domain
├─ src/HIS.BLL         (net10.0)   → DAL, Domain
├─ src/HIS.Web         (net10.0)   → BLL                 (portal bệnh nhân, MVC)
├─ src/HIS.WebApi      (net10.0)   → BLL, DAL            (API nội bộ)
├─ src/HIS.WinForms    (net10.0-windows) → BLL           (quầy tiếp nhận, admin)
└─ tests/HIS.Tests     (net10.0)   → BLL, DAL, Domain
```

Sinh ra từ file `.csproj` thật, kiểm tra bằng:

```powershell
Select-String -Path src/*/*.csproj,tests/*/*.csproj -Pattern "ProjectReference"
```

## 2. Quy tắc BẤT BIẾN (vi phạm là yêu cầu đổi, không phải gợi ý)

Trích `init.md:33-34`, áp dụng cho toàn bộ code trong repo:

1. `Domain` / `DAL` / `BLL` **không** được tham chiếu ngược lên `Web`, `WebApi`, `WinForms`.
2. `BLL` **không** dùng `HttpContext` hay bất kỳ kiểu nào của `Microsoft.AspNetCore.*` — lý do: cùng service phải gắn được cho WinForms (xem `src/HIS.WinForms/Program.cs:34-56`).
3. Project UI chỉ tham chiếu `DAL` ở composition root để đăng ký `AddDbContext`.

## 3. Từng tầng một

### HIS.Domain — 35 file, 18 enum

- `Enums.cs`: 18 enum (`RoleId`, `AppointmentStatusId`, `Gender`, `ExamShift`, `SlotStatus`, `PatientRelationship`, `LinkStatus`, `PaymentMethod`, …).
- `Entities/<Mục>/<Entity>.cs`: mỗi entity 1 file, tên class PascalCase tiếng Anh, property 1–1 với cột DDL.
- `Entities/DomainExtensions.cs`: hàm thuần không phụ thuộc EF (`ToPatientCode()`, `ToVietnameseLabel()`, sinh mã hẹn `HIS-{yyyyMMdd}-{6 base32}`).
- Không có annotation, không có attribute của EF/ASP.NET.

### HIS.DAL — DbContext + 33 IEntityTypeConfiguration + 4 migration

- `AppDbContext.cs:8-10` nhận `TimeProvider` trong constructor; `SaveChanges`/`SaveChangesAsync` gọi `TouchUpdatedAt()` (`:98-116`) để tự ghi `updated_at` cho `Account`, `Appointment`, `MedicalRecord` — vì vậy **code BLL không bao giờ tự gán** `updated_at`.
- `AppDbContext.cs:80` `UseIdentityAlwaysColumns()`; 3 bảng lookup (`roles`, `service_types`, `appointment_statuses`) giữ `ValueGeneratedNever` để seed id cố định.
- `AppDbContext.cs:87` vòng lặp `model.GetEntityTypes()` đặt `DeleteBehavior.Restrict` cho **mọi** FK. Ngoại lệ có chủ đích: 10 config giữ `Cascade` cho các bảng vệ tinh phụ thuộc một lịch hẹn/hóa đơn (`VitalSignsConfiguration.cs:21`, `InvoiceConfiguration.cs:28`, `PaymentConfiguration.cs:29`, `LabOrder/LabResult`, `Prescription/PrescriptionItem`, `AppointmentStatusHistory:36`, `MedicalRecord:25`).
- `Configurations/<Mục>/*.cs`: đặt `ToTable`, CHECK constraints, index, `HasData` seed. Tên ràng buộc theo mẫu `ck_<bảng>_<cột>` / `uq_<nội dung>`.
- `UpperSnakeCaseEnumConverter.cs`: chuyển enum PascalCase ↔ chuỗi `UPPER_SNAKE` trong cột CHECK, dùng cho mọi cột enum kiểu chuỗi.
- Naming convention: `UseNpgsql(...).UseSnakeCaseNamingConvention()` — class `PatientAccount` → bảng `patient_account`, không đặt tên thủ công.

### HIS.BLL — 13 interface + 15 service + DTO

- `Interfaces/` + `Services/`: service trả **tuple/DTO**, không có `Result<T>` (xem DECISIONS #3).
- `Services/AppointmentStateMachine.cs`: hàm thuần, không đụng DB — định nghĩa các bước chuyển trạng thái hợp lệ. Test unit trực tiếp, không cần Postgres.
- `Services/QueuePositionAllocator.cs`: cấp vị trí trong khung giờ, tính số thứ tự hiển thị `A##`/`T##`. Là scoped service, **phải đăng ký trong mọi composition root** (`Web/Program.cs:33`, `WebApi/Program.cs:36`, `WinForms/Program.cs:43`).
- `Services/QueueCallOrder.cs`: thứ tự gọi số, gồm ưu tiên/chen hàng.
- `Options/BookingOptions.cs`: các ngưỡng nghiệp vụ (`OnlineRatioPct=60`, `ReleaseCutoffMinutes=10`, `FreeCancelHours=24`, `MaxLateCancelsPerMonth=1`, `MaxNoShowsBeforeBlock=3`, `NoShowWindowDays=90`, `BookingBlockDays=45`, ngưỡng chen hàng/tuổi ưu tiên). Mặc định ghi thẳng trong code; muốn đổi thì thêm section `Booking` vào `appsettings.json` — `AddBookingOptions` bind và `ValidateOnStart()` sẽ chặn giá trị vô lý ngay lúc khởi động.
- `Security/RolePermissions.cs`: map role → trang được mở, dùng cho `MainForm` (WinForms) và guard của portal.
- `DTOs/`: record trả về cho UI. UI không bao giờ nhận entity EF trừ `HIS.WebApi` (controller trả entity thô — cẩn thận khi đổi enum, nó đổi luôn payload JSON).

### HIS.Web — portal bệnh nhân (MVC + Session)

- `Controllers/PatientPageController.cs`: base class, `AccountId` đọc từ phiên đăng nhập, `RequireAccount()` redirect sang trang login.
- `Controllers/AccountController.cs` + `Helpers/DemoSession.cs`: **trang login demo**, chủ đích dùng Session thay cookie (DECISIONS #8) vì module đăng nhập thật thuộc người khác.
- `Controllers/BookingController.cs`: wizard 5 bước `ChonKhoa → ChonNgayCa → ChonGio → ChonNguoiKham → XacNhan`, state giữ trong `Session`, mọi bước có `Guard(state, throughStep)`.
- `Controllers/LichKhamController.cs`: danh sách lịch theo tài khoản + hủy lịch.

### HIS.WebApi / HIS.WinForms

- `WebApi`: REST nội bộ, cùng chain DI với Web, có Swagger.
- `WinForms`: app quầy/admin; `Pages/` + `Controls/` + `Dialogs/`, `UserSession` giữ tài khoản đăng nhập, `Theme.cs` quy ước giao diện.

## 4. Database và migration

DB phát triển: `hospital_his` (PostgreSQL 18.6, localhost:5432). DB phụ: `his_rehearsal` (tập dượt ALTER), `his_ci` (CI local).

| Migration | Loại |
|---|---|
| `20261015010838_InitialCreate` | scaffold ban đầu, **không khớp model hiện tại**, giữ lại vì repo đồng nghiệp đã base vào nó |
| `20261103165420_AlignToGroupSpec` | patch **chỉ chạy trên DB prod đã có dữ liệu** (remap id `appointment_statuses`, thêm cột, 3 bảng mới, di trú `patients.account_id` → link) |
| `20261103173448_SanityCheck` | siết ràng buộc sau align |
| `20261104144910_QueuePositionNumbering` | đổi sang cơ chế số A/T theo vị trí: `capacity`/`online_quota`/`slot_position`, bỏ `queue_counters` |

Vì `AlignToGroupSpec` là patch một chiều, CI **không** chạy migration để dựng DB. CI dựng DB từ snapshot sinh từ model:

- `docs/database/ci/schema.sql` — DDL đầy đủ 32 bảng cho DB sạch
- `docs/database/ci/demo_master.sql`, `demo_day.sql` — dữ liệu mồi
- `docs/database/ci/schema_checks.sql` — 16 kiểm tra ràng buộc, phải trả 16/16 TRUE

## 5. Chỗ trống và số thứ tự (bất biến nghiệp vụ quan trọng nhất)

- `capacity = max(1, slot_duration / avg_consultation_minutes)`; `online_quota = round(capacity × OnlineRatioPct / 100)`.
- Vị trí `1..online_quota` = ghế web (tiền tố `A`); vị trí còn lại = tại chỗ (tiền tố `T`).
- Số hiển thị = (tổng `capacity` của các khung giờ đi trước trong cùng phòng + ca + ngày) + `slot_position`.
- Trừ ghế phải **nguyên tử**: `UPDATE appointment_slots SET booked_count = booked_count + 1, online_booked = online_booked + 1 WHERE id = @id AND booked_count < capacity AND version = @version`, kiểm tra số dòng bị ảnh hưởng; hủy/chuyển walk-in phải nhả lại ghế trong cùng transaction.
- `uq_patient_slot_active` (partial unique index) chặn một hồ sơ đặt trùng chính khung giờ đó khi lịch còn sống.

## 6. Test

`tests/HIS.Tests`, 11 file:

- Unit (không trait): `AppointmentStateMachineTests`, `QueueCallOrderTests`, `QueuePositionsTests`, `SchedulingServiceTests`, `BookingServiceTests` — dùng `FixedTimeProvider.cs`, không cần DB.
- Integration (`[Trait("Category","Integration")]`): `SlotConstraintTests`, `BookingPolicyPathTests`, `PatientProfileFlowTests`, `ReceptionCheckInPathTests`, `RehearsalReadPathsTests` — chạy trên Postgres thật qua biến môi trường `HIS_REHEARSAL_CONNECTION`, gộp chung `[Collection("RehearsalPostgres")]` để không phá nhau.
- Ràng buộc CHECK/UNIQUE **không test được bằng InMemory**, nên các test ràng buộc bắt buộc đi qua Postgres.

## 7. CI (`.github/workflows/ci.yml`)

- job `build` (windows-latest): `dotnet build` + `dotnet test --filter "Category!=Integration"`.
- job `integration` (ubuntu-latest, service `postgres:16`): dựng `schema.sql` → seed → `schema_checks.sql` → `dotnet test --filter "Category=Integration"` → `dotnet ef migrations has-pending-model-changes` (chặn model đổi mà quên migration).
- Chạy y hệt bằng tay: `bash backup/ci_local.sh` (không commit `backup/`).
