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

### HIS.DAL — DbContext + 33 IEntityTypeConfiguration + 6 migration

- `AppDbContext.cs:8-10` nhận `TimeProvider` trong constructor; `SaveChanges`/`SaveChangesAsync` gọi `TouchUpdatedAt()` (`:98-116`) để tự ghi `updated_at` cho `Account`, `Appointment`, `MedicalRecord` — vì vậy **code BLL không bao giờ tự gán** `updated_at`.
- `AppDbContext.cs:80` `UseIdentityAlwaysColumns()`; 3 bảng lookup (`roles`, `service_types`, `appointment_statuses`) giữ `ValueGeneratedNever` để seed id cố định.
- `AppDbContext.cs:87` vòng lặp `model.GetEntityTypes()` đặt `DeleteBehavior.Restrict` cho **mọi** FK. Ngoại lệ có chủ đích: 10 config giữ `Cascade` cho các bảng vệ tinh phụ thuộc một lịch hẹn/hóa đơn (`VitalSignsConfiguration.cs:21`, `InvoiceConfiguration.cs:28`, `PaymentConfiguration.cs:29`, `LabOrder/LabResult`, `Prescription/PrescriptionItem`, `AppointmentStatusHistory:36`, `MedicalRecord:25`).
- `Configurations/<Mục>/*.cs`: đặt `ToTable`, CHECK constraints, index, `HasData` seed. Tên ràng buộc theo mẫu `ck_<bảng>_<cột>` / `uq_<nội dung>`.
- `UpperSnakeCaseEnumConverter.cs`: chuyển enum PascalCase ↔ chuỗi `UPPER_SNAKE` trong cột CHECK, dùng cho mọi cột enum kiểu chuỗi.
- Naming convention: `UseNpgsql(...).UseSnakeCaseNamingConvention()` — class `PatientAccount` → bảng `patient_account`, không đặt tên thủ công.

### HIS.BLL — 15 interface + 18 service + DTO

- `Interfaces/` + `Services/`: service trả **tuple/DTO**, không có `Result<T>` (xem DECISIONS #3).
- `Services/AppointmentStateMachine.cs`: hàm thuần, không đụng DB — định nghĩa các bước chuyển trạng thái hợp lệ. Test unit trực tiếp, không cần Postgres.
- `Services/QueuePositionAllocator.cs`: cấp vị trí trong khung giờ, tính số thứ tự hiển thị `A##`/`T##`. Là scoped service, **phải đăng ký trong mọi composition root** (`Web/Program.cs:33`, `WebApi/Program.cs:36`, `WinForms/Program.cs:43`).
- `Services/QueueCallOrder.cs`: thứ tự gọi số, gồm ưu tiên/chen hàng.
- `Services/OtpService.cs` + `Interfaces/IOtpService.cs`: cấp/xác thực mã OTP khi thêm hồ sơ (mục 5.4). BLL **chỉ** chọn email nhận theo `ProfileOtpSource`, sinh mã, ghi dấu vết `notifications` rồi gọi `IOtpDelivery` — không biết gì về SMTP/ASP.NET, nên WebApi và test dùng chung được. `OtpCodes.Matches` so mã trong Session, chịu khoảng trắng nhưng chặn sai độ dài.
- `Services/ProfileEmail.cs`: chuẩn hóa email về chữ thường + `IsUsable`/`NormalizeUsable` (chuỗi rỗng/khoảng trắng/email vô nghĩa → `null`) để mọi chỗ cần email dùng chung một cách từ chối.
- `Options/BookingOptions.cs`: các ngưỡng nghiệp vụ (`OnlineRatioPct=60`, `ReleaseCutoffMinutes=10`, ngưỡng chen hàng/tuổi ưu tiên, `MaxAdvanceDays=30`, `BookingCutoffHour=15`, `MaxProfilesPerAccount=5`, `MaxProfilesPerOwnerPhone=5`, `MaxRelativesPerDay=3`, `MaxBookingsPerDay=6`). Mặc định ghi thẳng trong code; muốn đổi thì thêm section `Booking` vào `appsettings.json` — `AddBookingOptions` bind và `ValidateOnStart()` sẽ chặn giá trị vô lý ngay lúc khởi động. **Đã bỏ** `FreeCancelHours`, `MaxLateCancelsPerMonth`, `BookingBlockDays`, `MaxNoShowsBeforeBlock`, `NoShowWindowDays`: không còn phạt hủy trễ và không còn chặn tài khoản vì vắng mặt (đến trễ thì bỏ lượt, quầy bắt số lại). Cột `accounts.booking_blocked_until`, `accounts.no_show_reset_at`, `appointment_status_history.is_late_cancel` vẫn còn trong DB cho khỏi ALTER nhưng code không đọc/ghi nữa.
- `Security/RolePermissions.cs`: map role → trang được mở, dùng cho `MainForm` (WinForms) và guard của portal.
- `DTOs/`: record trả về cho UI. UI không bao giờ nhận entity EF trừ `HIS.WebApi` (controller trả entity thô — cẩn thận khi đổi enum, nó đổi luôn payload JSON).

### HIS.Web — portal bệnh nhân (MVC + Session)

- `Controllers/PatientPageController.cs`: base class, `AccountId` đọc từ phiên đăng nhập, `RequireAccount()` redirect sang trang login.
- `Controllers/AccountController.cs` + `Helpers/DemoSession.cs`: **trang login demo**, chủ đích dùng Session thay cookie (DECISIONS #8) vì module đăng nhập thật thuộc người khác.
- `Controllers/BookingController.cs`: wizard **ba lối vào** hội tụ về một `slotId` — `khoa` = `ChonKhoa → ChonBacsi → ChonNgayGio → ChonNguoiKham → XacNhan`, `ngay` = `ChonNgay → ChonKhoa → ChonBacsi (Bác sĩ & khung giờ) → ChonNguoiKham → XacNhan`, `bacsi` = `ChonBacsi → ChonNgayGio → ChonNguoiKham → XacNhan`. Mode lưu trong `BookingState` (Session); `Wizard` + `ModeHasStep` sinh thứ tự bước cho stepper động và nút Back (`ViewData["Steps"]/["Step"]/["StepLabel"]/["BackUrl"]`), `Guard(state, action)` chặn đi tắt, `SlotStepOf(state)` đưa về đúng màn khung giờ của mode đang đi. Ca khám không còn là một bước: `ExamShifts` chỉ còn là nhãn nhóm chip "Ca sáng / Ca chiều" trong `_SlotChips`. Màn `ChonNgayGio` tải lại khung giờ bằng fetch `GET /Booking/KhungGio?date=` (partial `_SlotList`). Wizard **chỉ chọn** hồ sơ đã liên kết, không tạo hồ sơ ở đây. Hai khai báo bảo hiểm nằm ở bước `XacNhan` (mục 5.3).
- `Controllers/HoSoController.cs`: màn Hồ sơ riêng — danh sách hồ sơ của tài khoản, một màn *Tạo hồ sơ* hai nhánh "đã từng khám / chưa từng khám" (mục 5.2), màn nhập mã xác thực `XacOtp` + `GuiLaiOtp` (mục 5.4), xem lịch sử khám theo từng hồ sơ.
- `Services/OtpDeliveries.cs`: hai kênh gửi mã — `SmtpOtpDelivery` (mail thật) và `DemoOtpDelivery` (in mã ra log). `Program.cs:56-64` chỉ đăng ký SMTP khi `Otp:Smtp` có User/Password không còn placeholder; `Options/OtpSmtpOptions.cs` giữ cấu hình. Credential Gmail đặt ở `src/HIS.Web/appsettings.Otp.local.json` — **đã `.gitignore` qua mẫu `*.local.json`**, không bao giờ bỏ vào `appsettings.json` đang tracked.
- `Controllers/LichKhamController.cs`: danh sách lịch theo tài khoản + hủy lịch.
- `Realtime/QueueChangeNotifier.cs`: broadcaster WebSocket một chiều (server → client) + middleware `UseQueueChangeSocket()`; xem mục 6.

### HIS.WebApi / HIS.WinForms

- `WebApi`: REST nội bộ, cùng chain DI với Web, có Swagger.
- `WinForms`: app quầy/admin; `Pages/` + `Controls/` + `Dialogs/`, `UserSession` giữ tài khoản đăng nhập, `Theme.cs` quy ước giao diện.
- `WinForms/Services/QueueChangeClient.cs`: client WebSocket (`ClientWebSocket` trong BCL, không thêm package) nối vào `/ws/quay` của HIS.Web, tự kết nối lại sau 3s, báo `Received` cho `ReceptionPage` để nó tự reload lưới — xem mục 6.

## 4. Database và migration

DB phát triển: `hospital_his` (PostgreSQL 18.6, localhost:5432). DB phụ: `his_rehearsal` (tập dượt ALTER), `his_ci` (CI local).

| Migration | Loại |
|---|---|
| `20261015010838_InitialCreate` | scaffold ban đầu, **không khớp model hiện tại**, giữ lại vì repo đồng nghiệp đã base vào nó |
| `20261103165420_AlignToGroupSpec` | patch **chỉ chạy trên DB prod đã có dữ liệu** (remap id `appointment_statuses`, thêm cột, 3 bảng mới, di trú `patients.account_id` → link) |
| `20261103173448_SanityCheck` | siết ràng buộc sau align |
| `20261104144910_QueuePositionNumbering` | đổi sang cơ chế số A/T theo vị trí: `capacity`/`online_quota`/`slot_position`, bỏ `queue_counters` |
| `20261105131631_AppointmentInsuranceDeclaration` | thêm 2 cột khai báo bảo hiểm trên `appointments`: `has_health_insurance`, `has_private_insurance` (BOOLEAN NOT NULL DEFAULT false, chỉ để quầy phân luồng) |
| `20261105141229_PatientEmail` | thêm `patients.email character varying(200) NULL` — hộp thư của hồ sơ, dùng làm nơi nhận mã OTP (mục 5.4) |

Vì `AlignToGroupSpec` là patch một chiều, CI **không** chạy migration để dựng DB. CI dựng DB từ snapshot sinh từ model:

- `docs/database/ci/schema.sql` — DDL đầy đủ 32 bảng cho DB sạch
- `docs/database/ci/demo_master.sql`, `demo_day.sql` — dữ liệu mồi
- `docs/database/ci/schema_checks.sql` — 18 kiểm tra ràng buộc, phải trả 18/18 TRUE

## 5. Chỗ trống và số thứ tự (bất biến nghiệp vụ quan trọng nhất)

- `capacity = max(1, slot_duration / avg_consultation_minutes)`; `online_quota = round(capacity × OnlineRatioPct / 100)`.
- Vị trí `1..online_quota` = ghế web (tiền tố `A`); vị trí còn lại = tại chỗ (tiền tố `T`).
- Số hiển thị = (tổng `capacity` của các khung giờ đi trước trong cùng phòng + ca + ngày) + `slot_position`.
- Trừ ghế phải **nguyên tử**: `UPDATE appointment_slots SET booked_count = booked_count + 1, online_booked = online_booked + 1 WHERE id = @id AND booked_count < capacity AND version = @version`, kiểm tra số dòng bị ảnh hưởng; hủy/chuyển walk-in phải nhả lại ghế trong cùng transaction.
- `uq_patient_slot_active` (partial unique index) chặn một hồ sơ đặt trùng chính khung giờ đó khi lịch còn sống.

### 5.1 Cửa sổ đặt lịch và hạn mức một ngày của portal

Toàn bộ quy tắc nằm trong `BookingService` (BLL), UI chỉ hỏi `FirstBookableDate` / `LastBookableDate` / `BookableDayCount` / `IsBookableDate(date)` nên WinForms dùng lại được:

- Ngày khám D chỉ **đặt** được trước `BookingCutoffHour` (15:00) của ngày D−1 ⇒ sau 15:00 hôm nay thì ngày mai tự đóng, `FirstBookableDate` nhảy sang ngày kia.
- Chỉ đặt tối đa `MaxAdvanceDays` (30) ngày kể từ hôm nay; lịch web render đúng cửa sổ đó, ngày ngoài hạn/đã cutoff trả `state="closed"` (nút disabled, nhãn "Ngưng nhận").
- Chỉ **hủy** được đến 23:59 ngày D−1 (`IsCancelAllowed`); qua mốc thì phải ra quầy.
- Hạn mức theo `booked_by`, đếm trong 00:00–23:59 của ngày đặt và **chỉ tính lượt còn hiệu lực** (trạng thái ≠ CANCELLED): tối đa `MaxBookingsPerDay` (6) lượt và `MaxRelativesPerDay` (3) hồ sơ khác nhau ⇒ hủy một lượt là hoàn chỉ tiêu ngay trong ngày.
- Hồ sơ: `PatientProfileService.ValidateProfileQuotaAsync` chặn quá `MaxProfilesPerAccount` (5) hồ sơ cho một tài khoản và quá `MaxProfilesPerOwnerPhone` (5) hồ sơ theo **SĐT chủ tài khoản** (SĐT của hồ sơ `SELF`, vì `accounts` không có cột phone); SĐT đã là chủ ở tài khoản khác → từ chối "Số điện thoại đã được đăng ký".
- Không còn phạt: hủy trễ không bị tính hạn mức, `NO_SHOW/EXPIRED` không còn đặt `booking_blocked_until` (`ReceptionService` chỉ còn ghi trạng thái).

### 5.2 Màn "Tạo hồ sơ": một màn, hai nhánh

`HoSo/Them` là **màn hình duy nhất** cho cả hai tình trạng người bệnh (màn "Xin liên kết" riêng đã xóa), chọn nhánh bằng radio `Branch` = `seen` | `new`:

- **`seen` — đã từng khám**: chỉ được nhập **mã bệnh nhân** `BN######` in trên phiếu khám (`Keyword`, `hoso.js` chặn format, `FindByPatientCodeAsync` chỉ parse mỗi mã) + quan hệ → `RequestLinkAsync` → link `PENDING`, `can_view_records=false` → màn nhập mã OTP (mục 5.4); mã không về được (hồ sơ chưa có email) thì link vẫn `PENDING` để quầy đối chiếu giấy tờ khi bệnh nhân tới. WinForms vẫn duyệt được. Broadcast `profile.link_requested`.
- **`new` — chưa từng khám**: khai họ tên, ngày sinh, giới tính, quan hệ, SĐT, CCCD (hoặc CCCD người thân), địa chỉ, số thẻ BHYT → `CreateProfileAsync` → link `ACTIVE` + `can_view_records=true`, đặt lịch ngay. `InsuranceCardNumber` được lưu vào `insurance_cards` qua `TrackInsuranceCardAsync` (không cần ALTER).
- **Quy tắc giấy tờ** `PatientProfileService.ValidateIdentityDocumentsAsync` (chạy sau hạn mức hồ sơ): CCCD đã tồn tại trong `patients` → từ chối và bảo chuyển sang nhánh `seen`; đủ `OwnIdFromAge` (14) tuổi → **bắt buộc** CCCD của chính mình; dưới 14 mà chưa có CCCD → **bắt buộc** `GuardianNationalId` và số đó phải khớp `national_id` của một hồ sơ `ACTIVE` thuộc đúng tài khoản (`uq` không đổi, chỉ là kiểm tra BLL — không thêm cột nào).
- `ThemHoSoRequest` **không** dùng `[Required]`/`[RegularExpression]` vì hai nhánh chung một form (trường của nhánh kia luôn rỗng); rỗng/format kiểm tra trong controller, nhân thân kiểm tra trong BLL. `hoso.js` disable toàn bộ input của panel không chọn để trình duyệt không gửi lẫn không chặn submit.

### 5.3 Hai khai báo bảo hiểm trên lượt khám

`appointments.has_health_insurance` / `has_private_insurance` (BOOLEAN NOT NULL DEFAULT false) là **khai báo của người bệnh ở bước `XacNhan`** của wizard (trước đây nằm ở bước chọn người khám — xem `DECISIONS.md` mục 23), chỉ để quầy nhắc mang thẻ và làm thủ tục viện phí:

- Không validate, không chặn đặt lịch, không ảnh hưởng viện phí. Thẻ bảo hiểm thật vẫn nằm ở `insurance_cards` (mục 5.2) — hai chỗ này không tra gì nhau.
- Cờ đi qua `BookingState` (Session) nên bấm *Quay lại* rồi vào lại bước xác nhận vẫn còn khai báo; `CreateAppointmentAsync(..., hasHealthInsurance, hasPrivateInsurance)` ghi thẳng vào entity trong cùng lần ghi với cấp số A.
- Nơi đọc: bước 5 ("Bảo hiểm đã khai"), hai màn lịch sử (nhãn gộp trong `InsuranceDeclarations.Summary`), và lưới quầy tiếp nhận qua `ReceptionItemDto.InsuranceDeclaration` ("BHYT" / "Tư nhân" / "BHYT + tư nhân"; BLL sinh nhãn vì quầy hiển thị tiếng Việt).
- Quầy **chưa sửa** được khai báo khi bệnh nhân khai sai — chỉ bệnh nhân đổi được trên web trước cutoff.

### 5.4 Mã OTP khi thêm hồ sơ

Chỉ nghiệp vụ *thêm hồ sơ* cần xác thực (mục 5.2). Form **không thu thập email** — nguồn hộp thư do nhánh vừa chọn quyết định, lấy từ dữ liệu đã có trong DB:

| Nhánh | Hồ sơ | Email nhận mã | Kết quả sau khi nhập đúng mã |
|---|---|---|---|
| `new` (chưa từng khám) | vừa tạo, link `ACTIVE` ngay | `accounts.email` của tài khoản đăng nhập, và được **sao chép** sang `patients.email` của hồ sơ mới | mã chỉ để xác nhận đã đọc được hộp thư, hồ sơ vẫn dùng được |
| `seen` (đã từng khám) | đã tồn tại, tìm bằng `BN######` | `patients.email` **đã lưu trong hồ sơ** (không cho form ghi đè) | `VerifyLinkAsync` chuyển link `PENDING` → `ACTIVE`, `verified_by` = tài khoản đang đăng nhập |

- Trạng thái chờ nằm trong `Session` (`HoSoController` key `PendingProfileOtp`: patientId, email nhận, mã, hạn, số lần đã nhập, `Source`); **không** có cột/bảng mới chứa mã nên không cần thêm ALTER. Hết hạn 5 phút, 5 lần nhập, cách 60 giây mới được gửi lại, xác nhận xong là xóa mã.
- Thiếu email ở nguồn nào → báo lỗi tiếng Việt đúng nguyên nhân kèm hotline 0808 0808, **không** cấp mã, trả người dùng về đúng nhánh vừa khai (`?branch=seen|new`). Nhánh `seen` vẫn để lại link `PENDING` vì quầy còn đường duyệt giấy tờ.
- Mỗi lần cấp mã ghi một dòng `notifications` (`title = "Mã xác thực hồ sơ"`) làm dấu vết đối chiếu; đây là bảng có thật nhưng người dùng chưa có UI đọc nó (`docs/DECISIONS.md` #15).
- Chi tiết và lý do chốt: `docs/DECISIONS.md` #22, bằng chứng chạy thật: `Changed.md` §12.

## 6. Kênh realtime giữa portal bệnh nhân và quầy tiếp nhận

Bệnh nhân đặt/hủy trên web → quầy phải thấy ngay, không cần bấm tải lại.

- **Server** `src/HIS.Web/Realtime/QueueChangeNotifier.cs` (singleton): giữ danh sách socket đang mở; `Publish(kind)` gửi JSON `{ kind, at }` cho mọi client, fire-and-forget, client gửi lỗi thì gỡ khỏi danh sách. Middleware `UseQueueChangeSocket(path: "/ws/quay")` đăng ký trong `Program.cs` **trước** `UseHttpsRedirection` — quầy nối `ws://` trên cổng http, đặt sau thì request upgrade bị đá sang https và hỏng.
- **Loại sự kiện** (`QueueChangeKinds`): `appointment.created` (BookingController đặt thành công), `appointment.cancelled` (LichKhamController hủy thành công), `profile.link_requested` (nhánh "đã từng khám" của `HoSo/Them` gửi yêu cầu liên kết).
- **Client** `src/HIS.WinForms/Services/QueueChangeClient.cs`: `ReceptionPage` subscribe khi mở trang, unsubscribe khi đóng; callback tới từ thread nền nên bắt buộc marshalling về UI thread bằng `Control.BeginInvoke` (`OnRealtimeChanged`), rồi mới reload lưới.
- **Config**: `Realtime:WebSocketUrl` (mặc định `ws://localhost:5044/ws/quay`) và `Realtime:Enabled` trong `src/HIS.WinForms/appsettings.json` (file gitignored). `Enabled=false` → quầy hoạt động y như trước, không mở kết nối.
- Tin nhắn chỉ báo "có thay đổi", **không** mang dữ liệu; quầy vẫn đọc DB qua `IReceptionService`, nên tin nhắn có thất lạc cũng không làm hai UI lệch nhau.

## 7. Test

`tests/HIS.Tests`, 12 file:

- Unit (không trait): `AppointmentStateMachineTests`, `QueueCallOrderTests`, `QueuePositionsTests`, `SchedulingServiceTests`, `BookingServiceTests` — dùng `FixedTimeProvider.cs`, không cần DB.
- Integration (`[Trait("Category","Integration")]`): `SlotConstraintTests`, `BookingPolicyPathTests`, `BookingEntryPathTests`, `PatientProfileFlowTests`, `OtpProfileFlowTests`, `ReceptionCheckInPathTests`, `RehearsalReadPathsTests` — chạy trên Postgres thật qua biến môi trường `HIS_REHEARSAL_CONNECTION`, gộp chung `[Collection("RehearsalPostgres")]` để không phá nhau. `BookingEntryPathTests` không giả định lịch trực: cửa sổ không còn khung nào thì bỏ qua, nên `his_ci` (chỉ có fixture hôm nay/ngày mai) vẫn chạy xanh.
- Ràng buộc CHECK/UNIQUE **không test được bằng InMemory**, nên các test ràng buộc bắt buộc đi qua Postgres.

## 8. CI (`.github/workflows/ci.yml`)

- job `build` (windows-latest): `dotnet build` + `dotnet test --filter "Category!=Integration"`.
- job `integration` (ubuntu-latest, service `postgres:16`): dựng `schema.sql` → seed → `schema_checks.sql` → `dotnet test --filter "Category=Integration"` → `dotnet ef migrations has-pending-model-changes` (chặn model đổi mà quên migration).
- Chạy y hệt bằng tay: `bash backup/ci_local.sh` (không commit `backup/`).
