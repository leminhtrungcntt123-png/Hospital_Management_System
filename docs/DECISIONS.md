# Quyết định kiến trúc (DECISIONS)

Mỗi mục: **Bối cảnh → Quyết định → Hệ quả**. Đánh dấu ⚠️ là chỗ **lệch `init.md` có chủ đích** — ai định "sửa cho đúng spec" phải đọc mục đó trước.

Trạng thái cập nhật: đợt sửa luồng OTP nhánh "chưa từng khám" (06/11/2026) — 24 quyết định.

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
**Hệ quả**: `IPatientAccessService.CanViewRecordsAsync` đã có nhưng chưa có màn nào dùng — viết `access_logs` khi đọc hồ sơ người khác là việc mẻ sau. *(Cập nhật 05/11/2026: `/HoSo/Chitiet` đã ghi `access_logs` qua `PatientAccessService`; vẫn chỉ hiển thị lịch sử khám + trạng thái, không có chẩn đoán.)*

## 17. Đồng bộ realtime bằng WebSocket thô, không SignalR và không polling

**Bối cảnh**: người dùng báo "đặt trên web mà không thấy hiện ở WinForms để check-in". Nguyên nhân thật là lưới quầy lọc theo **ngày hôm nay** còn lịch web luôn từ ngày mai (`FirstBookableDate()` = today+1), nhưng yêu cầu được chốt là phải có đồng bộ push.
**Quyết định**: `app.UseWebSockets()` + middleware riêng `/ws/quay` trong HIS.Web, `QueueChangeNotifier` (singleton) broadcast sự kiện `{kind, at}`; WinForms dùng `ClientWebSocket` (BCL) tự nối lại sau 3s và reload lưới qua `BeginInvoke`. Không thêm package, không dùng SignalR (phải thêm `Microsoft.AspNetCore.SignalR.*` ở cả hai phía, và WinForms phải mang theo `Client.Core`), không polling (mỗi 30s một lệnh SELECT cho từng máy quầy).
**Hệ quả**: kênh chỉ chạy khi HIS.Web bật; quầy mất kết nối thì im lặng và tự thử lại, dữ liệu vẫn luôn đọc từ DB nên không có trạng thái sai. BLL **không** biết gì về WebSocket (controller web gọi thẳng `QueueChangeNotifier`, giữ đúng quy tắc bất biến #2). Muốn nhiều máy chủ web thì phải thay broadcaster in-memory bằng redis/pub-sub — chưa cần.

## 18. Hồ sơ là trang riêng; wizard đặt lịch chỉ chọn, nút Back thay cho "Chỉnh sửa"

**Bối cảnh**: người dùng muốn tạo/xem hồ sơ theo từng folder riêng và quay lại chỉnh bước đã qua mà không phải hủy lượt đặt.
**Quyết định**: thêm `HoSoController` + Views `HoSo/` (danh sách, tạo hồ sơ, chi tiết kèm lịch sử khám, xin liên kết) và mục "Hồ sơ" trên menu; wizard bỏ hẳn nút "Thêm hồ sơ mới", bỏ link "Chỉnh sửa" và dòng "Chi phí dự kiến" ở bước xác nhận, thay bằng nút Back ở chân **và** đầu mọi bước (`ViewData["BackUrl"]` trong `_BookingLayout`).
**Hệ quả**: trạng thái chọn lịch vẫn chỉ nằm trong `Session["BookingState"]`; Back đơn thuần là redirect về bước trước nên không cần API mới. Chi phí dự kiến bỏ vì bảng giá thuộc module thu ngân (người khác phụ trách), hiển thị số giả định dễ gây hiểu nhầm.

## 19. Bỏ hẳn cơ chế phạt (hủy trễ / vắng mặt), đổi sang cửa sổ đặt 30 ngày + hạn mức ngày

**Bối cảnh**: yêu cầu mới chốt các mốc nghiệp vụ: đặt trước tối đa 30 ngày và trước 15:00 ngày liền trước, hủy đến 23:59 ngày liền trước, mỗi ngày tối đa 3 người thân / 6 lượt đăng ký, mỗi tài khoản (và mỗi SĐT chủ tài khoản) tối đa 5 hồ sơ. Người dùng quyết định "đến trễ thì hủy cho bắt số lại", không giữ hình thức phạt nào.
**Quyết định**: bỏ `FreeCancelHours`, `MaxLateCancelsPerMonth`, `BookingBlockDays`, `MaxNoShowsBeforeBlock`, `NoShowWindowDays` khỏi `BookingOptions`; `CreateAppointmentAsync` thêm `EnsureDailyLimitsAsync` (đếm lượt còn hiệu lực trong 00:00–23:59 theo `booked_by`) và chặn theo `MaxAdvanceDays` + cutoff 15:00; `CancelAppointmentAsync` chỉ còn một luật là `IsCancelAllowed` (trước 00:00 ngày khám); `ReceptionService` thôi ghi `booking_blocked_until`; `PatientProfileService.ValidateProfileQuotaAsync` áp 5 hồ sơ/tài khoản, 5 hồ sơ/SĐT chủ và chống trùng SĐT giữa hai tài khoản.
**Hệ quả**: cột `accounts.booking_blocked_until`, `accounts.no_show_reset_at`, `appointment_status_history.is_late_cancel` **vẫn nằm trong DB** (không ALTER để_drop) nhưng code không đọc/ghi — ai đọc schema tưởng còn dùng sẽ nhầm, nên ghi rõ ở ARCHITECTURE §5.1. Hủy lượt nào là hoàn chỉ tiêu lượt đó ngay trong ngày, vì bộ đếm loại trạng thái CANCELLED. SĐT chủ tài khoản lấy từ hồ sơ `SELF` (`patients.phone_number`) vì `accounts` không có cột phone và module đăng nhập không thuộc phạm vi.

## 20. Một màn "Tạo hồ sơ" hai nhánh, bỏ màn "Xin liên kết"

**Bối cảnh**: yêu cầu nghiệp vụ mô tả người dùng phải tự khai báo "người này đã khám ở bệnh viện chưa" ngay khi thêm hồ sơ — đã khám thì chỉ cần CCCD, chưa khám thì khai đầy đủ (họ tên, ngày sinh, giới tính, quan hệ, SĐT, CCCD; trẻ nhỏ không có CCCD thì dùng CCCD người thân kèm quan hệ, địa chỉ chọn hoặc nhập, hotline `08080808`). Repo đang có hai màn tách rời `HoSo/Them` và `HoSo/LienKet`, người dùng phải tự đoán mình thuộc màn nào.
**Quyết định**: gộp thành **một** màn `HoSo/Them` với radio `Branch=seen|new`; xóa `LienKet.cshtml` + action `LienKet` + `LienKetHoSoRequest`. Nhánh `seen` gọi thẳng `RequestLinkAsync` (vẫn `PENDING` cho tới khi quầy duyệt — tự động `ACTIVE` sau OTP để dành Mẻ 4); nhánh `new` gọi `CreateProfileAsync` với thêm `Address` + `InsuranceCardNumber` + `GuardianNationalId`. Thêm `BookingOptions.OwnIdFromAge=14` và `ValidateIdentityDocumentsAsync` trong BLL; hotline đổi thành `0808 0808` ở footer/ChonKhoa/Error.
**Hệ quả**: không cần ALTER cho mẻ này — "CCCD người thân" chỉ là **điều kiện chặn** được kiểm tra trong BLL rồi tái lập qua liên kết `account_patient_profiles` (cha mẹ ↔ con suy ra từ quan hệ của hai hồ sơ với cùng tài khoản), không lưu thành cột. Số thẻ BHYT đi vào `insurance_cards` sẵn có. Địa chỉ chỉ là một ô text + `datalist` tỉnh thành vì `patients.address` là một cột varchar, làm cascading quận/xã phải thêm bảng.

## 21. Hai khai báo bảo hiểm nằm trên `appointments`, không phải `insurance_cards`

**Bối cảnh**: người bệnh cần tick "có BHYT" / "có bảo hiểm tư nhân" ngay khi đặt lịch để quầy nhắc mang thẻ và làm thủ tục viện phí. Hệ thống đã có bảng `insurance_cards` (số thẻ thật, ngày hiệu lực, plan) nhưng nó là hồ sơ bảo hiểm của **người bệnh**, còn hai ô này là khai báo cho **từng lượt khám** — một người có thể hôm nay mang thẻ, hôm sau không.
**Quyết định**: thêm hai cột `boolean NOT NULL DEFAULT false` trên `appointments` (`has_health_insurance`, `has_private_insurance`), backup pg_dump trước khi ALTER và chạy ALTER in-place trên cả `hospital_his` lẫn `his_ci`, migration `20261105131631_AppointmentInsuranceDeclaration` chỉ để ghi lại lịch sử. Cờ đi từ bước 4 của wizard qua `BookingState` (Session) vào `CreateAppointmentAsync(..., hasHealthInsurance, hasPrivateInsurance)`; BLL **không** validate và không đổi giá; `ReceptionItemDto.InsuranceDeclaration` sinh nhãn cho lưới quầy.
**Hệ quả**: `schema.sql` phải sinh lại và gate thêm kiểm tra thứ **17**; `init.md` mục 5 + `schema-reference.sql` + `erd.md` cùng có hai cột này. Hai cột không liên lạc gì với `insurance_cards`: quầy thấy "BHYT + tư nhân" nhưng bệnh nhân chưa từng nhập thẻ thì vẫn phải tra giấy tờ ở quầy. Quầy chưa sửa được khai báo (chỉ bệnh nhân đổi trước cutoff) và đây là điểm treo đã ghi ở `Changed.md` 11.4.

## 22. OTP xác thực hồ sơ: email lấy theo từng nhánh, chỉ tìm hồ sơ bằng mã BN, mã chỉ sống trong Session

**Bối cảnh**: thêm hồ sơ trên web là chỗ duy nhất cần chặn kẻ gian liên kết bừa hồ sơ của người khác, nên nghiệp vụ chốt "chỉ xác thực khi thêm hồ sơ". Câu hỏi còn lại là **gửi mã cho ai** và **tìm hồ sơ bằng gì**. Phương án ban đầu (thử rồi bỏ) là để người dùng gõ email ngay trên form: nó mở cửa tự tay chỉ định hộp thư nhận mã, và cho phép tìm hồ sơ bằng CCCD/SĐT/tên nên một người lạ dò đúng số CCCD là xin liên kết được.
**Quyết định**: form **không thu thập email nào cả**, và mỗi nhánh lấy một nguồn cố định, không cho ghi đè. *(Nhánh "chưa từng khám" đã bị bác ở mục 24: nay form có ô email và hồ sơ chỉ tạo sau khi nhập đúng mã. Nhánh "đã từng khám" vẫn giữ nguyên như dưới đây.)*
* nhánh *chưa từng khám* → tạo hồ sơ mới, mã gửi về `accounts.email` của tài khoản đang đăng nhập, rồi **sao chép** email đó sang `patients.email` của hồ sơ mới (để lần sau hồ sơ này còn có mail mà nhận mã);
* nhánh *đã từng khám* → ô tìm kiếm chỉ chấp nhận **mã bệnh nhân** `BN######` in trên phiếu khám, mã gửi về `patients.email` **đã lưu trong hồ sơ**; không được ghi đè từ form. Liên kết tạo trước ở trạng thái `PENDING`, nhập đúng mã mới `ACTIVE` (giữ nguyên thứ tự `VerifyLinkAsync` vốn có);
* thiếu email ở nguồn nào thì báo lỗi tiếng Việt đúng nguyên nhân (kèm hotline 0808 0808) chứ không im lặng: "Tài khoản đăng nhập chưa có email…" / "Hồ sơ … chưa có email trong hệ thống…".
`OtpService` (BLL) sinh mã bằng `RandomNumberGenerator`, 6 số, 5 phút, 5 lần nhập, cách lần gửi lại 60 giây; mã **chỉ nằm trong Session** của trình duyệt, không có cột/bảng mới. Mỗi lần cấp mã ghi một dòng `notifications` (`title = "Mã xác thực hồ sơ"`) làm dấu vết đối chiếu — nội dung giữ nguyên như bản trước. Gửi thật/ghi log nằm ở `IOtpDelivery` phía HIS.Web (`SmtpOtpDelivery` / `DemoOtpDelivery`), BLL không biết SMTP.

**Hệ quả**: cấu trúc DB chỉ phải thêm một cột `patients.email varchar(200) NULL` (backup `hospital_his_pre_otp_20261105_211043.dump`, ALTER in-place trên `hospital_his` + `his_ci`, migration `20261105141229_PatientEmail`), gate thêm kiểm tra thứ **18**. `FindByKeywordAsync` đổi thành `FindByPatientCodeAsync` (hàm private, duy nhất `RequestLinkAsync` gọi — WinForms không gọi nên quầy vẫn tìm hồ sơ bằng các trường riêng của nó). Người quên mã BN phải gọi hotline hoặc chọn nhánh *chưa từng khám*, đó là đánh đổi đã chốt để không lộ "hồ sơ này có tồn tại hay không" qua ô tìm kiếm. SMTP Gmail đặt ở `src/HIS.Web/appsettings.Otp.local.json` (**đã `.gitignore` qua `*.local.json`**), không bao giờ bỏ vào `appsettings.json` đang tracked. Bản demo này tắt `Otp:RevealCodeOnScreen` nên mã không hiện trên màn hình; muốn tự kiểm tra luồng mà không có mail thì bật lại cờ đó.

## 23. Ba lối vào đặt khám hội tụ về một `slotId`; bỏ hẳn bước "chọn ca"

**Bối cảnh**: người dùng mô tả portal theo app UMC Care, nơi bệnh nhân bắt đầu từ ba tình huống khác nhau — "khám theo chuyên khoa", "khám theo ngày" (đã biết hôm nào rảnh), "khám theo bác sĩ" (đã tin một người). Bản cũ chỉ có một đường duy nhất (khoa → ca → ngày → giờ → người), nên người đã biết bác sĩ vẫn phải đi qua ba màn không liên quan, và "ca" là một bước thật sự trong khi người bệnh chỉ quan tâm khung giờ.
**Quyết định**: `BookingState.Mode` (`khoa` / `ngay` / `bacsi`) quyết định **thứ tự** các bước, không quyết định nghiệp vụ:
* `khoa` = Chuyên khoa → Bác sĩ → Ngày & giờ → Người khám → Xác nhận (5 bước);
* `ngay` = Ngày khám → Chuyên khoa → Bác sĩ & khung giờ → Người khám → Xác nhận (5 bước);
* `bacsi` = Bác sĩ → Ngày & giờ → Người khám → Xác nhận (4 bước).
Cả ba chỉ khác nhau ở màn nào hiện trước; bước cuối luôn gửi đúng một `slotId` vào `CreateAppointmentAsync`, nên mọi quy tắc (cutoff 15:00 hôm trước, cửa sổ 30 ngày, chặn Chủ nhật, hạn mức 3 người thân/6 lượt, suất A) chỉ có **một** chỗ thực hiện trong BLL. `Guard` + `Wizard` trong `BookingController` suy ra bước hợp lệ và stepper động; `ModeHasStep` quyết định màn "Đổi" nào được phép quay lại.
Ca khám **không còn là một bước**: `ExamShifts.For(...)` chỉ sinh nhãn nhóm chip "Ca sáng / Ca chiều" trong `_SlotChips.cshtml`. Xóa `ChonNgayCa.cshtml`, `ChonGio.cshtml`, `_ShiftList.cshtml`, action `CaKham`, `BookingShiftDto`, `ShiftVm`, `BookingState.Shift`, `IBookingService.GetShiftsAsync`. Khung đã kín vẫn trả về `Status = "FULL"` để hiện chữ "Đã đầy" thay vì ẩn đi — người bệnh thấy vì sao hết chỗ.
Khai báo bảo hiểm dịch từ bước "Người khám" sang bước "Xác nhận" vì nó là một phần của phiếu khám cùng chi phí; bước chọn hồ sơ chỉ còn danh sách hồ sơ + nút "Thêm hồ sơ mới".

**Hệ quả**: DB **không đổi cấu trúc** (chỉ thêm truy vấn đọc: `GetDepartmentsOnDateAsync`, `GetDoctorsAsync(dept, date)`, `GetDoctorSlotsAsync`, `GetDaysAsync(from, count, dept, doctor)`, `GetSlotByIdAsync`). Link "Đặt lịch khám" ở header và nút "Đặt lịch mới" nay vào `Booking/Index` (màn chọn lối vào) thay vì nhảy thẳng vào `ChonKhoa`, vì GET `ChonKhoa` không còn nghĩa khi chưa có mode. Giao diện degrade trung thực theo dữ liệu thật: chưa có cột ảnh nên avatar là tên viết tắt, chưa có rating/số lượt khám nên thẻ bác sĩ hiện **số suất online còn lại + ngày gần nhất còn chỗ** (đọc từ `appointment_slots`), chưa có bảng nghỉ lễ nên chỉ chặn Chủ nhật, chưa có module thanh toán nên bước 5 ghi "phí khám dự kiến + đóng tại quầy viện phí" chứ không bày thẻ ATM/QR/ví điện tử, và modal bác sĩ **ẩn** dòng Chuyên khoa/Ngày trực khi dữ liệu đổ về từ khung giờ không mang theo chúng. Test mới `tests/HIS.Tests/BookingEntryPathTests.cs` (3 test) không giả định dữ liệu mà **so chiếu chéo** ba đường đọc với nhau: số suất trên thẻ khoa = tổng suất từng bác sĩ của khoa đó, danh sách khung của khoa = gộp danh sách khung từng bác sĩ, lịch một bác sĩ không bao giờ mở ngày Chủ nhật, và `GetSlotByIdAsync` đọc lại đúng khung vừa chọn.


## 24. Nhánh "chưa từng khám": form có ô email và cấp mã OTP **trước** khi ghi hồ sơ

**Bối cảnh**: hai lỗi người dùng bắt được trên cùng một màn hình. (1) Nhánh *chưa từng khám* không có ô nhập email, trong khi app tham chiếu (UMC Care) luôn xin email ngay cạnh số điện thoại — và vì bản trước lấy `accounts.email`, một tài khoản demo chưa có email là **mù hoàn toàn**, không còn đường nào để xin mã. (2) Nghiêm trọng hơn: `CreateNewAsync` lưu hồ sơ **trước** rồi mới xin mã, nên khi xin mã thất bại, hồ sơ vẫn nằm lại DB ở trạng thái `ACTIVE` và dùng để đặt khám được — người dùng gọi là "thoát ra vẫn thêm thành công hồ sơ đó". Bằng chứng thật: `BN000057 Lê Minh Chí` gắn tài khoản 8 có `accounts.email = NULL`.
**Quyết định**: 
* `ThemHoSoRequest` nhận lại `Email`, ô input đặt **cùng dòng** với Số điện thoại (bỏ `class="wide"` ở cả hai ô), có `[StringLength(200)]` phía server và regex phía `hoso.js`; để trống hoặc sai định dạng thì ở lại form với câu "Hồ sơ chưa được lưu."
* **Thứ tự ghi đảo lại**: `CreateNewAsync` chỉ validate rồi xin mã; hồ sơ chỉ do `CreateProfileAsync` tạo ra trong POST của `XacOtp`, sau khi mã đúng. `PendingOtp.Source` thay bằng `ThemHoSoRequest? Draft` (bản khai giữ trong Session, `null` = nhánh *seen*).
* BLL bỏ enum `ProfileOtpSource` vì nó chỉ còn một ý nghĩa (chọn cột để đọc email) và không áp dụng được cho hồ sơ chưa tồn tại: tách thành `IssueProfileOtpAsync(accountId, patientId)` (đọc `patients.email`) và `IssueNewProfileOtpAsync(accountId, email, fullName)` (email từ form), cả hai dồn về `IssueAndSendAsync` — phần sinh mã/ghi `notifications`/gửi qua `IOtpDelivery` chỉ có một bản.
* **Không** dùng phương án "tạo rồi rollback xóa": `access_logs` và `appointments` đều FK `Restrict` vào `patients`, nên mọi cách xóa ngược phải biết thứ tự bảng và dễ bỏ sót; cũng **không** cho hồ sơ mới ở `PENDING` vì sẽ phá vỡ bất biến "SELF chỉ `PENDING` khi chưa xác thực liên kết" và làm màn danh sách hiển thị hồ sơ chưa ai xác nhận.
* Nhánh *đã từng khám* **giữ nguyên** thứ tự cũ (tạo `PENDING` → `VerifyLinkAsync` → `ACTIVE`), email vẫn chỉ lấy từ `patients.email` đã lưu, không cho form ghi đè — đây là nhánh cần chống kẻ gian liên kết hồ sơ người khác, nên không được để form chọn hộp thư.
**Hệ quả**: DB **không đổi cấu trúc** (cột `patients.email` mục 22 đã đủ), gate vẫn 18. Test `OtpProfileFlowTests` bỏ tham số source, thêm `Cap_ma_khi_ho_so_chua_ton_tai_khong_tao_ra_ho_so` chốt bằng bất biến "đếm `patients` không đổi sau khi cấp mã" — chính là điều bản cũ vi phạm. Đánh đổi còn lại: người dùng phải nhớ email của mình và mã xác thực chỉ là 6 số trong Session 5 phút; nếu bỏ giữa màn nhập mã thì phải làm lại từ đầu, nhưng đổi lại không bao giờ có hồ sơ rác. Bằng chứng chạy thật: `Changed.md` §14.

## 25. Chỉ liên kết `ACTIVE` mới đặt lịch được; PII của hồ sơ `PENDING` bị che; mã OTP có hạn mức phát

**Bối cảnh**: người dùng review và bắt còn sáu lỗi trên luồng OTP + liên kết hồ sơ. (1) `OtpService` ghi nguyên mã vào `notifications.content`, nên ai đọc dump DB hoặc mở mục thông báo là cầm được mã còn hiệu lực. (2) Liên kết `PENDING` được tạo **trước** khi nhập OTP, mà `PatientAccessService.CanBookAsync` và `BookingController.IsBookable` đều coi `PENDING` là đặt được: người bỏ dở form hoặc nhập sai 5 lần **vẫn đặt được khám bằng hồ sơ người khác**; comment của `VerifyLinkAsync` ("duyệt chỉ mở quyền đặt lịch") mâu thuẫn thẳng với code. (3) Họ tên / ngày sinh / giới tính / SĐT hiện ra ở `HoSo/Index` và `HoSo/Chitiet` ngay sau khi xin liên kết, tức trước mọi xác thực, mà mã BN lại là số tăng dần nên dò được; câu "không tìm thấy" khác "đã ghi nhận" còn giúp kẻ tấn công thử đúng mã. (5) Không giới hạn tần suất phát mã: chỉ `GuiLaiOtp` kiểm tra 60 giây, còn mỗi lần cấp mã mới lại reset `Attempts` nên luật "5 lần mỗi mã" mất nghĩa. (6) Toàn bộ quy tắc nghiệp vụ của nhánh *chưa từng khám* chỉ chạy **sau** khi nhập OTP (`CreateProfileAsync`): người dùng bị đốt mã rồi mới nhận lỗi, và khi `CreateProfileAsync` gặp `DbUpdateException` thì nó bị bọc thành `InvalidOperationException` không ai bắt → trang 500, mất sạch phiếu đã điền; SĐT còn bị kiểm hai lần với luật lệch nhau (Web 10–11 số, BLL 9–15 số).
**Quyết định**:
* `CanBookAsync` và `IsBookable` **chỉ nhận `Status == ACTIVE`**. `PENDING` có nghĩa là "chưa nhập xong mã xác thực", không phải "chờ quầy duyệt cho đặt lịch".
* Che PII ngay tại controller: `HoSoController.LoadProfilesAsync` và `BookingController.GetProfilesAsync` set `IsMasked` cho mọi link không `ACTIVE` và trả họ tên/ngày sinh/SĐT **rỗng**; view chỉ được đọc các property `Display*` (`DisplayName`, `DisplayDateOfBirth`, `DisplayPhone`, `DisplayInitials`) để không ai vô tình in lại cột thô. Màn chi tiết hồ sơ bị che cũng **bỏ hẳn truy vấn lịch sử khám**.
* Dấu vết OTP không còn mã: tiêu đề cố định `OtpService.TraceTitle`, nội dung chỉ ghi "Đã gửi mã 6 số xác thực hồ sơ `BNxxxxxx` tới `m***@gmail.com` lúc HH:mm ngày dd/MM/yyyy"; thêm `ProfileEmail.Mask`, đổi `OtpIssueResult.PatientName` và mọi lời báo của `RequestLinkAsync` sang **mã bệnh nhân** thay vì họ tên.
* Hạn mức phát mã đếm từ bảng **đã có**: `IssueQuotaMessageAsync` đếm dòng `notifications` cùng tài khoản + cùng `TraceTitle` trong `OtpOptions.IssueWindowMinutes` (15'), chặn từ lần thứ `MaxIssuesPerWindow` (5) và hoàn lại số phút phải chờ. Không cần bảng mới, không đổi cấu trúc DB.
* Tách `IPatientProfileService.ValidateNewProfileAsync`: chạy **toàn bộ** quy tắc mà không ghi gì xuống DB, được `CreateNewAsync` gọi **trước** khi cấp mã. `CreateProfileAsync` và hàm validate dùng chung `ValidateNewProfileDraftAsync` (một bản luật duy nhất, trả `NewProfileDraft`), `DbUpdateException` được bắt ngay trong `CreateProfileAsync` thành câu thân thiện, và controller có `RenderThemFormAsync(draft, error)` nên phiếu không mất.
* Một luật SĐT cho hồ sơ: `PhoneNumbers` (10–11 chữ số, bắt đầu bằng 0) dùng ở BLL lẫn `HoSoController`; `ReceptionService`/WinForms **cố ý** giữ 9–15 vì quầy còn gõ số bàn, ghi rõ trong XML doc của class.
**Hệ quả**: DB **không đổi cấu trúc**, gate vẫn 18/18. Hồ sơ `PENDING` không còn là đường tắt để đặt khám, nhưng không bị kẹt: chính chủ nhập đúng mã OTP (`VerifyLinkAsync(accountId, patientId, accountId)` ở màn *Nhập mã xác thực*) hoặc quầy bấm duyệt (`LinkApprovalDialog`) đều chuyển `ACTIVE`. Vì card PENDING bị che nên thêm đường hồi sức: màn chi tiết hồ sơ bị che có nút "Nhập mã xác thực" → `HoSo/Them?branch=seen&patientCode=...`, và `RequestLinkAsync` gặp link `PENDING` có sẵn là cấp mã mới chứ không tạo liên kết trùng. Test: unit **89/89** (thêm `Mask` + `PhoneNumbers`), integration **24/24** (thêm `Xin_ma_day_dac_hon_nguong_thi_bi_chan`, `Lien_ket_pending_khong_dat_lich_duoc_den_khi_chuyen_active`, `ValidateNewProfile_noi_dung_mot_loi_CreateProfile_tu_choi_va_khong_ghi`). Kiểm tra dữ liệu thật trước khi siết luật SĐT: `hospital_his` 8/8 và `his_rehearsal` 6/6 số điện thoại đã khớp `^0[0-9]{9,10}$` nên không phải sửa dòng nào. Chi phí còn lại: muốn xem hồ sơ của người thân thì bắt buộc phải xong OTP hoặc ra quầy, và ai xin mã dồn dập sẽ bị chờ 15 phút.

## 26. Ba chỗ hở còn lại của mẻ trước: mặc định không hiện mã, email thô bị che cả trong lời báo, hạn mức tính theo đồng hồ ứng dụng

**Bối cảnh**: người dùng đọc lại commit `3dbee72` (mục 25) và bắt ba chỗ hở, trong đó chỗ thứ nhất **làm yếu chính bản sửa số 2**:
1. `OtpOptions.RevealCodeOnScreen` mặc định `true` và `appsettings.json` được commit **không có section `Otp`** → mọi máy thiếu `appsettings.Otp.local.json` (file gitignored) đều in mã 6 số ngay trên màn *Nhập mã xác thực*. Kẻ chỉ cần biết mã `BN######` — thứ tự tăng dần, đã bị chặn ở mục 25 chính vì lý do đó — tự đọc mã, tự nhập và liên kết `PENDING` thành `ACTIVE`. tức là quay lại đúng lỗ hổng "PENDING đặt được lịch".
2. `OtpService` vẫn trả `message` chứa **email nguyên văn** ("Đã gửi mã 6 số tới `test probe@mail.vn` qua Email"), còn `HoSoController.SavePendingAndAskCodeAsync` set `TempData["Message"] = issue.Message` → email thô sống tới trang kế tiếp và hiện lại ở *Hồ sơ của tôi* / *Lịch khám*.
3. `Notification.CreatedAt` **không được gán trong code**; cột khai theo `DEFAULT now()` của Postgres (`timestamp without time zone` = múi giờ server) trong khi `IssueQuotaMessageAsync` so với `DateTime.Now` của app. Máy dev cùng múi giờ (+07) nên ăn khớp, nhưng DB chạy UTC + app chạy UTC+7 thì các dòng dấu vết trông như đã cũ 7 tiếng → **hạn mức 5 mã/15 phút không bao giờ kích hoạt**. Nội dung dấu vết còn ghi "…lúc {expiresAt}" trong khi đó là giờ **hết hạn**, không phải giờ gửi.

**Quyết định**:
* `RevealCodeOnScreen` bỏ khởi tạo `= true` → mặc định `false`; bật demo là chủ đích, không phải phần thưởng cho thiếu cấu hình. Thêm `src/HIS.Web/appsettings.Development.json` (`"Otp": { "RevealCodeOnScreen": true }`) và **negate trong `.gitignore`** (`appsettings.Development.json` đang bị ignore, file này không chứa bí mật nên được commit một cách có ý thức).
* `Program.cs`: kênh `DemoOtpDelivery` chỉ đăng ký khi `builder.Environment.IsDevelopment()`; thiếu SMTP ở môi trường khác là **không có kênh gửi nào**, `OtpService` trả "Chưa cấu hình kênh gửi mã OTP." thay vì âm thầm phát mã. Thêm chốt cổng startup: ngoài Development mà cờ `RevealCodeOnScreen` bật là **ném `InvalidOperationException`** — thà không chạy được còn hơn lộ mã.
* Che email ở **cả hai lớp**: `OtpService` dùng `ProfileEmail.Mask(recipient)` cho `message` trả về, và `SavePendingAndAskCodeAsync` **không** set `TempData["Message"]` nữa, chỉ set `TempData["Error"]` khi `!issue.Success` (mã đã cấp thì màn *Nhập mã xác thực* là nơi duy nhất nói cho người dùng biết).
* Giới hạn không phụ thuộc múi giờ DB: `IssueAndSendAsync` lấy **một** `DateTime now = DateTime.Now` rồi truyền xuống cả `IssueQuotaMessageAsync` lẫn `Notification.CreatedAt` (giống các service khác trong BLL), dấu vết viết "...tới `m***@x` lúc {giờ gửi} ngày ...; mã hết hạn lúc {giờ hết hạn}".

**Hệ quả**: DB **không đổi cấu trúc**, gate vẫn 18/18. Test: unit **90/90** (thêm `Mac_dinh_khong_in_ma_ra_man_hinh`), integration **25/25** (thêm `Han_muc_dem_theo_dong_ho_ung_dung_khong_phai_dong_ho_server` — chèn 5 dòng `TraceTitle` có `CreatedAt = DateTime.Now.AddMinutes(-1)` rồi bắt lần thứ 6 bị chặn; hai assert `DoesNotContain(email thô, issued.Message)` ở cả hai nhánh; `InRange((DateTime.Now - note.CreatedAt).TotalSeconds, -5, 5)` và `Contains("mã hết hạn lúc")` trong nội dung dấu vết). Bằng chứng thật: xem `Changed.md` §16.6. Ghi chú cho máy dev: `appsettings.Otp.local.json` được nạp **sau** các nguồn mặc định nên nó thắng `appsettings.Development.json`; trên máy đã cấu hình SMTP thì cờ vẫn về `false` và màn hình không hiện mã.

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
