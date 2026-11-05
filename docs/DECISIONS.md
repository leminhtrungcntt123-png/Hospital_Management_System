# Quyết định kiến trúc (DECISIONS)

Mỗi mục: **Bối cảnh → Quyết định → Hệ quả**. Đánh dấu ⚠️ là chỗ **lệch `init.md` có chủ đích** — ai định "sửa cho đúng spec" phải đọc mục đó trước.

Trạng thái cập nhật: Mẻ 5 (05/11/2026).

---

## 1. ⚠️ Giữ `HIS.WebApi` và `HIS.WinForms`

**Bối cảnh**: `init.md:14-29` chỉ liệt kê 4 project (`Domain`, `DAL`, `BLL`, `Web`) + tests. Repo đồng nghiệp đã có sẵn `HIS.WebApi` (Swagger/API) và `HIS.WinForms` (app quầy tiếp nhận, admin) với code đang chạy.
**Quyết định**: giữ cả hai, coi WinForms là UI chính cho nhân viên, WebApi cho luồng nội bộ.
**Hệ quả**: thêm 2 composition root phải bảo trì (`src/HIS.WebApi/Program.cs`, `src/HIS.WinForms/Program.cs`) — mỗi service mới phải đăng ký ở **cả 3** chỗ. Chính vì giữ WinForms mà quy tắc "BLL không dùng kiểu ASP.NET" (`init.md:33-34`) càng quan trọng.

## 2. ⚠️ CSDL `hospital_his`, connection key `DefaultConnection`, Postgres 18.6 cục bộ

**Bối cảnh**: `init.md:538-539` yêu cầu extension `AddHisDataAccess` + connection string tên `HisDb`; `init.md:710` yêu cầu `docker-compose.yml` chạy postgres:16 database `his_db`. Thực tế nhóm đã có DB `hospital_his` trên Postgres 18.6 ở máy dev, đã có dữ liệu thật (tài khoản, lịch hẹn, hồ sơ).
**Quyết định**: giữ tên DB và key `DefaultConnection` như code hiện có; dựng DB mới bằng snapshot DDL (`docs/database/ci/schema.sql`) thay vì docker-compose.
**Hệ quả**: không có `docker-compose.yml`/`.env.example`. CI dùng service `postgres:16` (đủ cho DDL, không cần 18). Đổi DB thật = phải migrate dữ liệu, không phải đổi 1 dòng config.

## 3. ⚠️ BLL trả tuple/DTO, không dùng `Result<T>`

**Bối cảnh**: `init.md:544` chốt `Result`/`Result<T>` cho lỗi nghiệp vụ. 15 service đang tồn tại trong `src/HIS.BLL/Services/` (code đồng nghiệp) trả `Task<(bool Ok, string? Message)>`, DTO, hoặc ném exception cho lỗi bất thường.
**Quyết định**: giữ kiểu trả hiện tại, chỉ thêm DTO khi cần.
**Hệ quả**: `init.md` và code không khớp nhau ở hợp đồng service. Đổi sang `Result<T>` là rewrite toàn bộ controller + form WinForms, **không** làm trong đợt này. Ai làm phải thống nhất cả nhóm vì nó chạm module của 4 người.

## 4. ⚠️ 7 role, không phải 5

**Bối cảnh**: `init.md:521-523` seed 5 role, gộp "Y tá / Lễ tân" thành một. DB thật và `src/HIS.WinForms` tách `RECEPTIONIST` (quầy bốc số, duyệt liên kết) và `NURSE` (khám, sinh hiệu), thêm `CASHIER` (thu ngân).
**Quyết định**: `RoleId` theo DB thật — `Admin=1, Doctor=2, Nurse=3, Receptionist=4, Cashier=5, DepartmentHead=6, Patient=7` (`src/HIS.Domain/Enums.cs:8-17`), seed `HasData` khớp đúng id trong DB đích.
**Hệ quả**: `roles` trong DB **không** đúng từng dòng như `init.md`. Bảng tra `src/HIS.BLL/Security/RolePermissions.cs` là chỗ duy nhất định nghĩa role nào mở được trang nào.

## 5. ⚠️ `appointment_statuses` lấy id theo spec, DB cũ phải remap

**Bối cảnh**: 8 status trong DB `hospital_his` bị **hoán vị id** so với spec/enum C# (3↔5, 4↔6): cùng code `IN_PROGRESS` nhưng id khác nhau, nên mọi so sánh theo id trong WinForms sai âm thầm.
**Quyết định**: chuẩn hóa về 10 dòng id theo `init.md:526-531` (`Pending=1 … RescheduleRequired=10`). Remap chạy trong migration `AlignToGroupSpec` qua id tạm `1101..1108` để không va PK khi UPDATE hoán vị.
**Hệ quả**: một chiều. Muốn quay lại phải `pg_restore` backup (`backup/hospital_his_*.dump`), không có `Down()` tự động an toàn.

## 6. ⚠️ Bỏ `queue_counters`, đánh số theo vị trí khung giờ (A/T)

**Bối cảnh**: `init.md` ban đầu mô tả `queue_counters` (bộ đếm theo ngày+phòng+ca+kênh). Bộ đếm thuần không cho phép hoàn một suất giữa hàng, không phân biệt ghế bán online và ghế tại quầy, và hai quầy bấm cùng lúc vẫn có thể ghi trùng số.
**Quyết định**: mỗi lượt giữ `slot_position` trong khung giờ; `1..online_quota` = suất **A** (web), phần còn lại = suất **T** (tại quầy). Số hiển thị = tổng `capacity` các khung đi trước trong cùng phòng/ca/ngày + `slot_position`, nên `A06`/`T09` không bao giờ trùng. Chống trùng do index `uq_slot_position` (tạo trong migration `QueuePositionNumbering:106`) + `version` (optimistic concurrency). Migration: `20261104144910_QueuePositionNumbering`.
**Hệ quả**: `queue_counters` bị drop; `capacity`/`online_quota` thay cho `max_patients`/`online_slots`; công thức `capacity = max(1, slot_duration / avg_consultation_minutes)`, `online_quota = round(capacity × OnlineRatioPct/100)`. Ba chỗ lệch này ghi ngay đầu [docs/database/schema-reference.sql](database/schema-reference.sql).
## 7. ⚠️ Không dùng Testcontainers.PostgreSql

**Bối cảnh**: `init.md:43` và `init.md:670` yêu cầu test integration chạy bằng Testcontainers. Máy từng thành viên chưa chắc có Docker, và container trắng không chứa dữ liệu đã align của nhóm nên không kiểm được ràng buộc trên DB thật.
**Quyết định**: test integration (`[Trait("Category","Integration")]`) đọc connection string từ biến môi trường `HIS_REHEARSAL_CONNECTION`, nối DB Postgres thật; thiếu biến thì test tự bỏ qua có thông báo. CI dùng service `postgres:16` + `docs/database/ci/*.sql`.
**Hệ quả**: in-process InMemory **không** còn dùng cho test CHECK/UNIQUE (nó không enforce). Xem `tests/HIS.Tests/SlotConstraintTests.cs`.

## 8. ⚠️ Trang đăng nhập portal bệnh nhân dùng Session, không dùng Cookie Authentication

**Bối cảnh**: `init.md:638` chốt Cookie Authentication (`LoginPath=/Account/Login`, `AccessDeniedPath`). Module đăng nhập/authorization là việc của thành viên khác (PR `HIS-19` đã merge ở WinForms).
**Quyết định**: làm **bản demo tối thiểu đủ để vào web**: `src/HIS.Web/Helpers/DemoSession.cs` lưu tài khoản vào `HttpContext.Session`, `AccountController` gọi `AuthService.LoginAsync` thật (BCrypt + khóa 5 lần sai), `PatientPageController.RequireAccount()` redirect khi chưa đăng nhập.
**Hệ quả**: không có cookie auth, không reset mật khẩu, không rate-limit, không trang đăng ký. Khi chủ module làm xong, thay `DemoSession` bằng cơ chế của họ mà **không** đổi controller nghiệp vụ — tất cả chỉ đọc `AccountId` từ base class. Mật khẩu demo set bằng `backup/demologin.sh <username>` (không commit `backup/`).

## 9. Bỏ `patients.account_id`, thay bằng `account_patient_profiles`

**Bối cảnh**: `patients.account_id` là `NOT NULL UNIQUE` → mỗi hồ sơ chỉ thuộc đúng 1 tài khoản. Tính năng "một tài khoản đăng ký cho nhiều người thân" bị chặn cứng; đã tái hiện lỗi thật `duplicate key value violates unique constraint "patients_account_id_key"`.
**Quyết định**: bảng nối nhiều-nhiều `account_patient_profiles` (PK ghép `account_id + patient_id`, `relationship`, `link_status`, `can_view_records`, `verified_by/verified_at`). Di trú: `account_id` cũ → link `SELF` ACTIVE, `can_view_records=true`. Đồng thời drop `insurance_card_number` (5/5 bản ghi trống).
**Hệ quả**: `IPatientAccessService` là cửa duy nhất quyết định được đặt lịch / được xem hồ sơ. Ràng buộc tự nhiên (`uq_link_self_account`, `uq_link_self_patient`, `ck_link_pending_no_records`, `ck_link_adult_records_consent`) nằm trong `AccountPatientProfileConfiguration.cs`.

## 10. Liên kết hồ sơ theo "cách A" — đơn giản hóa

**Bối cảnh**: `init.md:562-571` quy định điều kiện khớp chặt theo tuổi + họ tên + ngày sinh + CCCD/thẻ BHYT, limit `MaxLinkRequestsPerDay`, job 14 ngày cắt link PENDING quá hạn.
**Quyết định**: hồ sơ **mới** → `ACTIVE` + `can_view_records=true`; hồ sơ **đã có** → `PENDING` + `false`, quầy duyệt lúc check-in. Không kiểm điều kiện khớp chặt, không limit số lần yêu cầu, không job hết hạn.
**Hệ quả**: Portal cho phép xin liên kết hồ sơ bất kỳ (chỉ bị chặn bởi CHECK: không tự xin liên kết hồ sơ của chính mình đang PENDING, người ≥18 chỉ bật `can_view_records` khi có consent). Đổi lại luồng demo chạy được ngay. Phần chặt hơn **chưa xóa khỏi `init.md`**, coi như nợ.

## 11. Portal bệnh nhân chỉ hiện lịch sử khám + trạng thái

**Bối cảnh**: module bệnh án (`medical_records`, cận lâm sàng, đơn thuốc) thuộc người khác.
**Quyết định**: trang `LichKham` chỉ liệt kê ngày/giờ/phòng/bác sĩ/trạng thái + nút hủy. Không hiển thị chẩn đoán hay kết quả.
**Hệ quả**: `IPatientAccessService.CanViewRecordsAsync` đã có nhưng chưa có màn nào dùng — viết `access_logs` khi đọc hồ sơ người khác là việc mẻ sau.

## 12. ⚠️ Chưa bật `TreatWarningsAsErrors`, chưa có CPM, chưa `dotnet format`

**Bối cảnh**: `init.md:18-19` yêu cầu `Directory.Build.props` với `TreatWarningsAsErrors=true` và `Directory.Packages.props` (Central Package Management, csproj không ghi Version). Code đồng nghiệp còn warning và mọi `PackageReference` đang pin version trong csproj.
**Quyết định**: **chưa bật, chưa chuyển**. Bật CPM là sửa cả 7 csproj → conflict gần như chắc chắn với 4 nhánh đang chạy.
**Điều kiện để bật**: `dotnet build HIS.sln` ra **0 warning** ở Domain/DAL/BLL trước, rồi bật từng project một. CI cũng chưa chạy `dotnet format --verify-no-changes` vì code cũ chưa format.

## 13. `init.md` nằm trong `.gitignore`

**Bối cảnh**: bản spec do nhóm gửi qua chat, cần ghi đè liên tục để đối chiếu; nếu tracked sẽ conflict mọi lần pull.
**Quyết định**: `init.md` + `backup/` vào `.gitignore` (đã `git rm --cached init.md`).
**Hệ quả**: spec **không** phải nguồn sự thật trong repo. Những phần đã thống nhất được chép vào `docs/`, còn `init.md` chỉ là bản làm việc local của từng người.

## 14. Migration `AlignToGroupSpec` là patch cho DB đã có dữ liệu

**Bối cảnh**: `InitialCreate` cũ lệch model thật (thiếu 3 bảng, 0 seed, sai kiểu timestamp), DB `hospital_his` lại **không** có `__EFMigrationsHistory` → không thể `database update` thẳng.
**Quyết định**: giữ nguyên 3 migration cũ, viết `Up()` bằng SQL tác giả viết tay (không để EF sinh), remap id qua id tạm, chạy `his_rehearsal` trước khi chạm DB thật. Baseline: tạo `__EFMigrationsHistory` và ghi dòng `20261015010838_InitialCreate` **trước** khi EF chạy, để nó không dựng lại bảng đã tồn tại.
**Hệ quả**: DB sạch của thành viên mới **không** tạo bằng `dotnet ef database update`; dùng `docs/database/ci/schema.sql` (hoặc `bash backup/ci_local.sh` dựng DB `his_ci`). `has-pending-model-changes` vẫn chạy được để chặn model trôi.

## 15. Việc còn nợ (mẻ sau, đã chốt phạm vi)

Màn bác sĩ gọi số + `called_at` + no-show 30′ kể từ lần gọi · `hold_until` + `BackgroundService` (`SlotGenerationWorker`, `HoldExpirationWorker`, `NoShowWorker`) · UI `Notification` · trang Thu ngân · module bệnh án · `Result<T>` · CPM + `TreatWarningsAsErrors` · docker-compose · điều kiện khớp hồ sơ chặt + job cắt link 14 ngày.

## 16. Mật khẩu DB dev đang nằm trong file tracked

**Bối cảnh**: `src/HIS.Web/appsettings.json` và `src/HIS.WebApi/appsettings.json` đang được git theo dõi và chứa `Password=` của Postgres dev (`.gitignore:## Secrets` có mục `appsettings.json` nhưng 2 file này đã add trước đó bằng `-f`).
**Quyết định**: **chưa đổi**, vì đồng nghiệp clone về sẽ mất connection string và không chạy được.
**Hệ quả / việc cần nhóm quyết**: hoặc chuyển sang `appsettings.Development.json` (gitignored) + `.env`, hoặc dùng role riêng không phải `postgres`. Đây là điểm nhạy cảm, nên để cả nhóm chốt cùng nhau, không âm thầm sửa.
