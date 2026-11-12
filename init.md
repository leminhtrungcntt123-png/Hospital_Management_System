Bạn là một senior backend engineer .NET. Hãy dựng KHUNG (scaffold) cho dự án web HIS
(Hospital Information System, "Hệ thống hỗ trợ đặt lịch khám và quản lý hồ sơ bệnh án"),
.NET 10, ASP.NET Core MVC, PostgreSQL. Đây là đồ án môn học làm theo nhóm 5 người, quản lý bằng Jira.
Làm ĐÚNG theo cấu trúc dưới đây, không tự ý đổi. Chỗ nào mâu thuẫn hoặc thiếu thông tin: DỪNG và hỏi, không tự đoán.
Chỉ dựng khung, KHÔNG viết nghiệp vụ thật. Các service chỉ có interface + class rỗng ném NotImplementedException.

## 0. CÁCH LÀM VIỆC
- Trước khi chạy lệnh, chạy `dotnet --list-sdks`. Nếu không có SDK .NET 10 thì DỪNG và báo.
- Làm theo 7 bước ở mục 12. Sau mỗi bước: chạy `dotnet build`, báo kết quả ngắn gọn (file đã tạo, lỗi/cảnh báo),
  rồi DỪNG chờ tôi nhắn "tiếp tục".
- Mục tiêu cuối: `dotnet build` 0 error, 0 warning; `dotnet test` pass.
- Nguồn sự thật về cấu trúc dữ liệu là DDL ở mục 5 (đã gồm toàn bộ thay đổi). Không có file SQL nào khác.

## 1. CẤU TRÚC SOLUTION
HIS/
├─ HIS.sln
├─ global.json                      (SDK 10, rollForward: latestFeature)
├─ Directory.Build.props            (Nullable=enable, ImplicitUsings=enable, LangVersion=latest, TreatWarningsAsErrors=true, WarningsNotAsErrors: NU1901;NU1902;NU1903;NU1904)
├─ Directory.Packages.props         (Central Package Management: PackageReference trong .csproj KHÔNG ghi Version)
├─ .editorconfig (đánh dấu Migrations/ là generated_code = true)  .gitignore  .env.example  docker-compose.yml  README.md
├─ .github/workflows/ci.yml
├─ .github/pull_request_template.md
├─ docs/ARCHITECTURE.md  docs/DECISIONS.md  docs/database/erd.md (Mermaid erDiagram từ DDL)  docs/database/schema-reference.sql (chép nguyên DDL mục 5)
├─ src/
│  ├─ HIS.Domain/   (Class Library)  — entity, enum. KHÔNG tham chiếu gì
│  ├─ HIS.DAL/      (Class Library)  — AppDbContext, cấu hình EF, migration, seed lookup
│  ├─ HIS.BLL/      (Class Library)  — service, validator, options, Result. KHÔNG dùng Microsoft.AspNetCore.*
│  └─ HIS.Web/      (ASP.NET Core MVC) — controller, view, auth, background worker
└─ tests/HIS.Tests/ (xUnit)

Project reference (dùng `dotnet add reference`):
- DAL → Domain | BLL → DAL, Domain | Web → BLL, DAL, Domain | Tests → Web, BLL, DAL, Domain
Quy tắc BẤT BIẾN: Domain/DAL/BLL không được tham chiếu ngược lên Web; BLL không dùng HttpContext hay bất kỳ kiểu nào của
ASP.NET (để sau này gắn được WinForms). Web tham chiếu DAL chỉ để đăng ký DI (composition root).

## 2. NUGET (version khai báo trong Directory.Packages.props, dùng bản ổn định mới nhất tương thích .NET 10)
- DAL: Microsoft.EntityFrameworkCore, Npgsql.EntityFrameworkCore.PostgreSQL, EFCore.NamingConventions,
  Microsoft.EntityFrameworkCore.Design (PrivateAssets=all)
- BLL: Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Options, Microsoft.Extensions.Logging.Abstractions,
  FluentValidation, FluentValidation.DependencyInjectionExtensions, BCrypt.Net-Next
- Web: Microsoft.EntityFrameworkCore.Design (PrivateAssets=all, cần cho `dotnet ef` khi startup-project là Web),
  Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore
- Tests: Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio, coverlet.collector, Moq, Testcontainers.PostgreSql,
  Microsoft.AspNetCore.Mvc.Testing
Không cài thêm package nào khác. Không dùng ASP.NET Identity, AutoMapper, MediatR, Generic Repository, UnitOfWork bọc EF.

## 3. QUY ƯỚC MAPPING (áp dụng cho toàn bộ entity)
- Class PascalCase tiếng Anh, mỗi entity 1 file .cs, namespace HIS.Domain. Bảng/cột Postgres là snake_case:
  `optionsBuilder.UseNpgsql(cs).UseSnakeCaseNamingConvention()`. Mỗi entity gọi ToTable("...") tường minh đúng tên bảng trong DDL.
- Property = đúng từng cột trong DDL, 1–1, PascalCase. KHÔNG thêm, KHÔNG bớt cột.
- Kiểu: INTEGER→int, VARCHAR/TEXT→string, BOOLEAN→bool, NUMERIC→decimal, DATE→DateOnly, TIME→TimeOnly,
  TIMESTAMP→DateTime (Kind=Unspecified, lưu GIỜ ĐỊA PHƯƠNG Việt Nam, HasColumnType("timestamp without time zone")).
  Ngoại lệ: DoctorSchedule.DayOfWeek có kiểu System.DayOfWeek (0 = Chủ nhật, EF tự map ra int).
- Nullability theo DDL: cột NOT NULL → kiểu không-null; nullable → `?`. string bắt buộc khởi tạo `= string.Empty;`;
  navigation bắt buộc `= null!;`; collection `= [];`.
- Cột `id` dạng GENERATED ALWAYS AS IDENTITY → `modelBuilder.UseIdentityAlwaysColumns()`.
  Ba bảng lookup (roles, service_types, appointment_statuses) dùng ValueGeneratedNever.
- Mọi FK: DeleteBehavior.Restrict (dữ liệu bệnh viện không cascade). Cấu hình bằng vòng lặp trên model.GetEntityTypes().
- Navigation: thêm cho MỌI FK, kèm collection nghịch đảo cho các quan hệ 1–N chính (vd Account.PatientLinks,
  Account.BookedAppointments, Account.Notifications, Patient.AccountLinks, Patient.Appointments, Patient.InsuranceCards,
  Patient.Invoices, Doctor.Schedules, AppointmentSlot.Appointments, Appointment.StatusHistory, Department.Rooms).
- Các chỗ có ≥2 FK cùng bảng đích phải cấu hình HasOne/WithMany tường minh theo tên FK: AccountPatientProfile→Account
  (account_id / created_by), DutyShift→Account (account_id / assigned_by), ScheduleException→Doctor (doctor_id / replacement_doctor_id),
  ScheduleException→Account (approved_by), Appointment→Appointment (parent_appointment_id), Appointment→Account (booked_by),
  AppointmentStatusHistory→AppointmentStatus (old_status_id / new_status_id), AppointmentStatusHistory→Account (performed_by).
- Enum C# cho mọi cột CHECK dạng danh sách chuỗi. Viết 1 `UpperSnakeCaseEnumConverter<TEnum>` trong DAL
  (PascalCase → UPPER_SNAKE: OutOfNetwork → OUT_OF_NETWORK, BankTransfer → BANK_TRANSFER, WalkIn → WALK_IN, OnSite → ON_SITE),
  áp dụng cho tất cả cột enum. Enum: Gender{Male,Female,Other}, ReferralType{InNetwork,OutOfNetwork}, ExamShift{Morning,Afternoon},
  ScheduleExceptionType{Leave,Training,Surgery,ShiftSwap}, ScheduleExceptionStatus{Pending,Approved,Rejected},
  SlotStatus{Available,Full,Blocked}, BookingChannel{Web,OnSite}, VisitType{Scheduled,WalkIn},
  LabOrderStatus{Pending,Completed}, InvoiceStatus{Unpaid,PartiallyPaid,Paid}, PaymentType{Deposit,Payment,Refund},
  PaymentMethod{Cash,BankTransfer}, InvoiceCategory{Examination,Medication,Service},
  PatientRelationship{Self,Spouse,Child,Parent,Sibling,Other}, LinkStatus{Active,Pending,Revoked}.
  Enum lookup theo id (RoleId, ServiceTypeId, AppointmentStatusId) là enum int, giá trị khớp dữ liệu seed.
- MỌI CHECK / UNIQUE / INDEX (kể cả index một phần có WHERE) / DEFAULT trong DDL phải có trong cấu hình EF với đúng tên.
  CHECK viết bằng `ToTable(t => t.HasCheckConstraint("ck_...", "..."))`, biểu thức SQL dùng TÊN CỘT SNAKE_CASE thật.
  Index một phần dùng `HasIndex(...).IsUnique().HasFilter("...")`.
  Cột CHECK không tên trong DDL thì đặt tên theo mẫu `ck_<bảng>_<cột>`.
- Mặc định: CreatedAt → HasDefaultValueSql("now()"); IsActive → khởi tạo `= true` trong C# VÀ HasDefaultValue(true).
  Appointment.StatusId không có default (BLL phải gán Pending khi tạo).
- AppDbContext nhận `TimeProvider`; override SaveChangesAsync để tự gán UpdatedAt cho Account, Appointment, MedicalRecord khi Modified.
- AppointmentSlot.Version: IsConcurrencyToken().
- Chỉ dùng Fluent API cho entity, KHÔNG Data Annotation trên entity.

## 4. CLASS ↔ BẢNG (33 entity, mỗi entity 1 file)
Role→roles, ServiceType→service_types, AppointmentStatus→appointment_statuses, Account→accounts, Department→departments,
DutyShift→duty_shifts, ClinicRoom→clinic_rooms, Patient→patients, AccountPatientProfile→account_patient_profiles,
Doctor→doctors, Nurse→nurses, DepartmentHead→department_heads, Medication→medications, Service→services,
InsurancePlan→insurance_plans, InsuranceCard→insurance_cards, DoctorSchedule→doctor_schedules,
ScheduleException→schedule_exceptions, AppointmentSlot→appointment_slots, QueueCounter→queue_counters,
Appointment→appointments, AppointmentStatusHistory→appointment_status_history, Notification→notifications,
VitalSigns→vital_signs, MedicalRecord→medical_records, LabOrder→lab_orders, LabResult→lab_results,
Prescription→prescriptions, PrescriptionItem→prescription_items, Invoice→invoices, Payment→payments,
InvoiceItem→invoice_items, AccessLog→access_logs.
Khóa chính ghép: AccountPatientProfile (AccountId, PatientId), QueueCounter (QueueDate, RoomId, Shift, Channel) → dùng HasKey.

## 5. DDL ĐẦY ĐỦ (nguồn sự thật)
```sql
-- ==============================================================================
-- 0. RESET DATABASE (XÓA BẢNG CŨ THEO THỨ TỰ AN TOÀN)
-- ==============================================================================
DROP TABLE IF EXISTS 
    access_logs,
    invoice_items,
    payments,
    invoices,
    prescription_items,
    prescriptions,
    lab_results,
    lab_orders,
    medical_records,
    vital_signs,
    notifications,
    appointment_status_history,
    appointments,
    queue_counters,
    appointment_slots,
    schedule_exceptions,
    doctor_schedules,
    duty_shifts,
    scheduling_periods,
    insurance_cards,
    insurance_plans,
    services,
    medications,
    department_heads,
    nurses,
    doctors,
    account_patient_profiles,
    patients,
    clinic_rooms,
    departments,
    accounts,
    appointment_statuses,
    service_types,
    roles 
CASCADE;

-- ==============================================================================
-- 1. LOOKUP TABLES
-- ==============================================================================
CREATE TABLE roles (
    id          INTEGER PRIMARY KEY,
    code        VARCHAR(20) NOT NULL UNIQUE,    -- 'ADMIN', 'DOCTOR'...
    name        VARCHAR(50) NOT NULL,
    description VARCHAR(255)
);

CREATE TABLE service_types (
    id          INTEGER PRIMARY KEY,
    code        VARCHAR(20) NOT NULL UNIQUE,    -- 'LAB_TEST', 'EXAMINATION', 'IMAGING'
    name        VARCHAR(50) NOT NULL
);

CREATE TABLE appointment_statuses (
    id          INTEGER PRIMARY KEY,
    code        VARCHAR(20) NOT NULL UNIQUE,
    name        VARCHAR(50) NOT NULL,
    sort_order  INTEGER NOT NULL,
    is_terminal BOOLEAN NOT NULL DEFAULT FALSE
);

-- ==============================================================================
-- 2. ACCOUNTS & STAFF
-- ==============================================================================
CREATE TABLE accounts (
    id                  INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    username            VARCHAR(50) NOT NULL UNIQUE,
    email               VARCHAR(100) UNIQUE,
    password_hash       VARCHAR(255) NOT NULL,
    role_id             INTEGER NOT NULL REFERENCES roles(id),
    is_active           BOOLEAN NOT NULL DEFAULT TRUE,
    failed_login_count  INTEGER NOT NULL DEFAULT 0,
    locked_until        TIMESTAMP NULL,
    created_at          TIMESTAMP NOT NULL DEFAULT now(),
    updated_at          TIMESTAMP NOT NULL DEFAULT now(),
    CONSTRAINT ck_accounts_email_lower CHECK (email IS NULL OR email = lower(email))
);

CREATE TABLE departments (
    id          INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name        VARCHAR(100) NOT NULL,
    description VARCHAR(255)
);

CREATE TABLE clinic_rooms (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    department_id   INTEGER NOT NULL REFERENCES departments(id),
    room_name       VARCHAR(50) NOT NULL,
    room_number     VARCHAR(20) NOT NULL UNIQUE,
    is_active       BOOLEAN NOT NULL DEFAULT TRUE   -- Dùng đếm tổng số phòng khả dụng của khoa (VD: 0/2 phòng)
);

-- Hồ sơ người khám. KHÔNG còn account_id: quan hệ với tài khoản nằm ở account_patient_profiles.
-- Bệnh nhân vãng lai (walk-in) có thể không có dòng liên kết nào.
CREATE TABLE patients (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    full_name       VARCHAR(100) NOT NULL,
    date_of_birth   DATE NOT NULL,
    gender          VARCHAR(10) CHECK (gender IN ('MALE','FEMALE','OTHER')),
    national_id     VARCHAR(20) UNIQUE,             -- NULL với trẻ chưa có CCCD
    phone_number    VARCHAR(15) NOT NULL,
    address         VARCHAR(255),
    allergies       VARCHAR(500),                   -- Tiền sử dị ứng / Lưu ý y khoa (VD: "Dị ứng Penicillin, Ibuprofen")
    created_at      TIMESTAMP NOT NULL DEFAULT now()
);
-- Liên kết nhiều–nhiều: tài khoản đặt lịch ↔ hồ sơ người khám.
-- relationship tính từ góc nhìn CHỦ TÀI KHOẢN (mẹ liên kết con → 'CHILD').
CREATE TABLE account_patient_profiles (
    account_id        INTEGER NOT NULL REFERENCES accounts(id),
    patient_id        INTEGER NOT NULL REFERENCES patients(id),
    relationship      VARCHAR(15) NOT NULL
                      CHECK (relationship IN ('SELF','SPOUSE','CHILD','PARENT','SIBLING','OTHER')),
    status            VARCHAR(10) NOT NULL DEFAULT 'ACTIVE'
                      CHECK (status IN ('ACTIVE','PENDING','REVOKED')),
    can_view_records  BOOLEAN NOT NULL DEFAULT FALSE,
    created_by        INTEGER REFERENCES accounts(id),
    verified_by       INTEGER REFERENCES accounts(id),   -- nhân viên hoặc tài khoản đã duyệt
    verified_at       TIMESTAMP,
    created_at        TIMESTAMP NOT NULL DEFAULT now(),
    PRIMARY KEY (account_id, patient_id),
    CONSTRAINT ck_link_pending_no_records CHECK (status = 'ACTIVE' OR can_view_records = FALSE),
    CONSTRAINT ck_link_self_not_pending   CHECK (NOT (relationship = 'SELF' AND status = 'PENDING')),
    CONSTRAINT ck_link_adult_records_consent
        CHECK (can_view_records = FALSE OR relationship IN ('SELF','CHILD') OR verified_by IS NOT NULL)
);

-- Bảng Bác sĩ: Tích hợp thông tin hiển thị Trang chủ (Public Portal) & Định mức ca (Shift Bidding Quota)
CREATE TABLE doctors (
    id                        INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    account_id                INTEGER NOT NULL UNIQUE REFERENCES accounts(id),
    department_id             INTEGER NOT NULL REFERENCES departments(id),
    full_name                 VARCHAR(100) NOT NULL,
    
    -- Thông tin hiển thị lên Trang chủ (Public Showcase)
    academic_title            VARCHAR(30),             -- 'BS', 'ThS.BS', 'TS.BS', 'BS.CKI', 'BS.CKII', 'PGS.TS'
    specialty                 VARCHAR(100),            -- Chuyên khoa sâu
    avatar_url                VARCHAR(500),            -- Ảnh chân dung bác sĩ
    short_bio                 VARCHAR(300),            -- Mô tả ngắn trên Card trang chủ
    biography                 TEXT,                    -- Bài giới thiệu chi tiết quá trình đào tạo & công tác
    years_of_experience       SMALLINT NOT NULL DEFAULT 0 CHECK (years_of_experience >= 0),
    is_public                 BOOLEAN NOT NULL DEFAULT TRUE,  -- Cho phép hiển thị trên Trang chủ
    is_featured               BOOLEAN NOT NULL DEFAULT FALSE, -- Ghim lên khu vực "Bác sĩ nổi bật"
    display_order             INTEGER NOT NULL DEFAULT 100,   -- Thứ tự sắp xếp trên Trang chủ (số nhỏ xếp trước)
    
    -- Thông tin phục vụ Tự đăng ký ca & Điều phối (Shift Bidding)
    employment_type           VARCHAR(20) NOT NULL DEFAULT 'FULL_TIME'
                              CHECK (employment_type IN ('FULL_TIME', 'PART_TIME')), -- Cơ hữu vs Thỉnh giảng
    min_shifts_per_week       SMALLINT NOT NULL DEFAULT 6,    -- Định mức ca tối thiểu/tuần
    max_shifts_per_week       SMALLINT NOT NULL DEFAULT 10,   -- Trần số ca tối đa/tuần
    
    license_number            VARCHAR(30) UNIQUE,      -- Số chứng chỉ hành nghề
    avg_consultation_minutes  INTEGER NOT NULL DEFAULT 15,    -- Dùng tính ETA hàng đợi
    CONSTRAINT ck_doctor_quota CHECK (max_shifts_per_week >= min_shifts_per_week)
);

CREATE TABLE nurses (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    account_id      INTEGER NOT NULL UNIQUE REFERENCES accounts(id),
    department_id   INTEGER REFERENCES departments(id),
    full_name       VARCHAR(100) NOT NULL
);

CREATE TABLE department_heads (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    account_id      INTEGER NOT NULL UNIQUE REFERENCES accounts(id),
    department_id   INTEGER NOT NULL UNIQUE REFERENCES departments(id),
    full_name       VARCHAR(100) NOT NULL
);

-- ==============================================================================
-- 3. CATALOGS (ADMIN-MANAGED)
-- ==============================================================================
CREATE TABLE medications (
    id                  INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name                VARCHAR(150) NOT NULL,
    active_ingredient   VARCHAR(150),
    strength            VARCHAR(50),
    unit                VARCHAR(20) NOT NULL,       -- viên, chai, ống...
    price               NUMERIC(12,2) NOT NULL DEFAULT 0,
    is_active           BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE services (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name            VARCHAR(150) NOT NULL,
    service_type_id INTEGER NOT NULL REFERENCES service_types(id),
    price           NUMERIC(12,2) NOT NULL DEFAULT 0,
    is_active       BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE insurance_plans (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name            VARCHAR(100) NOT NULL,          -- VD: "Đúng tuyến - 80%"
    coverage_pct    NUMERIC(5,2) NOT NULL,          -- 80.00 / 95.00 / 100.00
    referral_type   VARCHAR(20) CHECK (referral_type IN ('IN_NETWORK','OUT_OF_NETWORK'))
);

-- Thẻ BHYT thuộc về BỆNH NHÂN (không thuộc tài khoản). Chỉ dùng ở bước tính tiền.
CREATE TABLE insurance_cards (
    id                   INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    patient_id           INTEGER NOT NULL REFERENCES patients(id),
    card_number          VARCHAR(20) NOT NULL UNIQUE,
    insurance_plan_id    INTEGER NOT NULL REFERENCES insurance_plans(id),
    registered_facility  VARCHAR(150),                      -- nơi đăng ký KCB ban đầu
    is_registered_here   BOOLEAN NOT NULL DEFAULT FALSE,    -- đăng ký tại bệnh viện này = đúng tuyến
    valid_from           DATE NOT NULL,
    valid_to             DATE NOT NULL,
    is_active            BOOLEAN NOT NULL DEFAULT TRUE,     -- false khi thẻ cũ bị thay
    created_at           TIMESTAMP NOT NULL DEFAULT now(),
    CONSTRAINT ck_insurance_dates CHECK (valid_to >= valid_from)
);

-- ==============================================================================
-- 4. SCHEDULING & SHIFT BIDDING ENGINE
-- ==============================================================================
-- Đợt mở cổng đăng ký lịch khám & lịch trực (Bidding Window)
CREATE TABLE scheduling_periods (
    id                  INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    department_id       INTEGER REFERENCES departments(id), -- NULL = Mở toàn viện; Có ID = Mở riêng theo khoa
    name                VARCHAR(100) NOT NULL,              -- VD: 'Lịch làm việc Tuần 47 (16/11 - 22/11/2026)'
    start_date          DATE NOT NULL,
    end_date            DATE NOT NULL,
    bidding_opens_at    TIMESTAMP NOT NULL,                 -- Thời điểm mở cổng đăng ký
    bidding_closes_at   TIMESTAMP NOT NULL,                 -- Thời điểm đóng cổng để Trưởng khoa duyệt
    status              VARCHAR(20) NOT NULL DEFAULT 'OPEN'
                        CHECK (status IN ('DRAFT', 'OPEN', 'REVIEWING', 'PUBLISHED', 'CLOSED')),
    created_by          INTEGER REFERENCES accounts(id),
    created_at          TIMESTAMP NOT NULL DEFAULT now(),
    CONSTRAINT ck_period_dates CHECK (end_date >= start_date AND bidding_closes_at > bidding_opens_at)
);

-- Lịch trực đêm / Trực cấp cứu: Hỗ trợ cả Tự đăng ký (BIDDING) và Trưởng khoa phân công (APPROVED)
CREATE TABLE duty_shifts (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    period_id       INTEGER REFERENCES scheduling_periods(id),
    account_id      INTEGER NOT NULL REFERENCES accounts(id),   -- bác sĩ hoặc y tá
    department_id   INTEGER NOT NULL REFERENCES departments(id),
    start_time      TIMESTAMP NOT NULL,                         -- TIMESTAMP để trực đêm qua nửa đêm
    end_time        TIMESTAMP NOT NULL,
    status          VARCHAR(20) NOT NULL DEFAULT 'APPROVED'
                    CHECK (status IN ('BIDDING', 'APPROVED', 'REJECTED', 'CANCELLED')),
    note            VARCHAR(255),
    assigned_by     INTEGER REFERENCES accounts(id),            -- Trưởng khoa duyệt hoặc phân công
    created_at      TIMESTAMP NOT NULL DEFAULT now(),
    CONSTRAINT ck_duty_time CHECK (end_time > start_time)
);

-- Lịch khám Bác sĩ (Tích hợp Bác sĩ Tự đăng ký Shift Bidding & Trưởng khoa Điều phối)
CREATE TABLE doctor_schedules (
    id                      INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    period_id               INTEGER REFERENCES scheduling_periods(id),
    doctor_id               INTEGER NOT NULL REFERENCES doctors(id),
    department_id           INTEGER NOT NULL REFERENCES departments(id),
    
    -- Khi Bác sĩ mới bấm Pick (status = 'BIDDING'): room_id = NULL.
    -- Khi Trưởng khoa Duyệt (status = 'APPROVED'): BẮT BUỘC gán room_id (VD: P401, P402).
    room_id                 INTEGER REFERENCES clinic_rooms(id),
    
    work_date               DATE NOT NULL,              -- Ngày làm việc cụ thể
    shift                   VARCHAR(10) NOT NULL CHECK (shift IN ('MORNING','AFTERNOON')),
    start_time              TIME NOT NULL DEFAULT '07:30:00',
    end_time                TIME NOT NULL DEFAULT '11:30:00',
    slot_duration_minutes   INTEGER NOT NULL DEFAULT 15,
    max_patients            INTEGER NOT NULL DEFAULT 40,    -- tổng bệnh nhân / ca
    online_slots            INTEGER NOT NULL DEFAULT 28,    -- số slot online; tại quầy tối đa = max_patients - online_slots
    
    -- Tự động gán TRUE nếu ca tuần trước lấp đầy >80% hoặc số người pick vượt số phòng của khoa
    is_hot_shift            BOOLEAN NOT NULL DEFAULT FALSE,
    
    -- Vòng đời:
    -- 'BIDDING'     : Bác sĩ vừa đăng ký (không tính ai nhanh tay hơn)
    -- 'APPROVED'    : Trưởng khoa đã duyệt & gán buồng khám -> Sinh appointment_slots
    -- 'REJECTED'    : Từ chối (do hết phòng)
    -- 'REALLOCATED' : Ca gốc bị hết phòng, bác sĩ đã được điều chuyển sang một dòng ca trống mới
    -- 'CANCELLED'   : Đã duyệt nhưng hủy đột xuất
    status                  VARCHAR(20) NOT NULL DEFAULT 'BIDDING'
                            CHECK (status IN ('BIDDING','APPROVED','REJECTED','REALLOCATED','CANCELLED')),
    doctor_note             VARCHAR(255),                   -- Ghi chú của bác sĩ khi xin ca
    reallocated_from_id     INTEGER REFERENCES doctor_schedules(id), -- Lưu vết dòng ca Hot bị trượt khi chuyển sang ca trống
    reviewed_by             INTEGER REFERENCES accounts(id),         -- Trưởng khoa / Admin phê duyệt
    reviewed_at             TIMESTAMP,
    created_at              TIMESTAMP NOT NULL DEFAULT now(),
    
    CONSTRAINT ck_online_slots CHECK (online_slots BETWEEN 0 AND max_patients),
    CONSTRAINT ck_schedule_time CHECK (end_time > start_time),
    CONSTRAINT ck_approved_requires_room CHECK (status <> 'APPROVED' OR room_id IS NOT NULL)
);

CREATE TABLE schedule_exceptions (
    id                      INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    schedule_id             INTEGER REFERENCES doctor_schedules(id), -- Ca làm việc đã duyệt xin nghỉ/đổi
    doctor_id               INTEGER NOT NULL REFERENCES doctors(id),
    date                    DATE NOT NULL,
    shift                   VARCHAR(10) CHECK (shift IN ('MORNING','AFTERNOON')),  -- NULL = cả ngày
    type                    VARCHAR(20) NOT NULL
                            CHECK (type IN ('LEAVE','TRAINING','SURGERY','SHIFT_SWAP')),
    status                  VARCHAR(10) NOT NULL DEFAULT 'PENDING'
                            CHECK (status IN ('PENDING','APPROVED','REJECTED')),
    replacement_doctor_id   INTEGER REFERENCES doctors(id),
    reason                  VARCHAR(255),
    approved_by             INTEGER REFERENCES accounts(id),
    approved_at             TIMESTAMP,
    created_at              TIMESTAMP NOT NULL DEFAULT now(),
    CONSTRAINT ck_exception_replacement CHECK (replacement_doctor_id IS NULL OR replacement_doctor_id <> doctor_id),
    CONSTRAINT ck_exception_approved CHECK (status <> 'APPROVED' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL))
);

-- Slot ONLINE. Khi bác sĩ hủy ca: chuyển lịch hẹn rồi XÓA dòng slot (không có trạng thái CLOSED/CANCELLED).
CREATE TABLE appointment_slots (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    schedule_id     INTEGER REFERENCES doctor_schedules(id), -- Liên kết về ca khám đã duyệt sinh ra slot này
    doctor_id       INTEGER NOT NULL REFERENCES doctors(id),
    room_id         INTEGER NOT NULL REFERENCES clinic_rooms(id),
    start_time      TIMESTAMP NOT NULL,
    end_time        TIMESTAMP NOT NULL,
    capacity        INTEGER NOT NULL DEFAULT 1,
    booked_count    INTEGER NOT NULL DEFAULT 0,
    status          VARCHAR(15) NOT NULL DEFAULT 'AVAILABLE'
                    CHECK (status IN ('AVAILABLE','FULL','BLOCKED')),
    version         INTEGER NOT NULL DEFAULT 0,     -- Optimistic locking
    CONSTRAINT uq_slot UNIQUE (doctor_id, start_time),
    CONSTRAINT ck_capacity CHECK (booked_count <= capacity)
);

-- Bộ đếm số thứ tự (khóa dòng khi cấp số)
CREATE TABLE queue_counters (
    queue_date  DATE NOT NULL,
    room_id     INTEGER NOT NULL REFERENCES clinic_rooms(id),
    shift       VARCHAR(10) NOT NULL CHECK (shift IN ('MORNING','AFTERNOON')),
    channel     VARCHAR(15) NOT NULL CHECK (channel IN ('WEB','ON_SITE')),
    last_number INTEGER NOT NULL DEFAULT 0 CHECK (last_number >= 0),
    PRIMARY KEY (queue_date, room_id, shift, channel)
);

-- patient_id = NGƯỜI ĐƯỢC KHÁM; booked_by = TÀI KHOẢN ĐẶT LỊCH (bệnh nhân hoặc nhân viên quầy).
CREATE TABLE appointments (
    id                      INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    appointment_code        VARCHAR(30) NOT NULL UNIQUE,      -- HIS-{yyyyMMdd}-{6 ký tự base32 ngẫu nhiên}
    patient_id              INTEGER NOT NULL REFERENCES patients(id),
    booked_by               INTEGER REFERENCES accounts(id),
    slot_id                 INTEGER REFERENCES appointment_slots(id),   -- NULL nếu walk-in hoặc RESCHEDULE_REQUIRED
    doctor_id               INTEGER REFERENCES doctors(id),             -- walk-in chưa có slot
    department_id           INTEGER REFERENCES departments(id),
    room_id                 INTEGER REFERENCES clinic_rooms(id),
    status_id               INTEGER NOT NULL REFERENCES appointment_statuses(id),
    booking_channel         VARCHAR(15) NOT NULL DEFAULT 'WEB'
                            CHECK (booking_channel IN ('WEB','ON_SITE')),
    visit_type              VARCHAR(15) NOT NULL DEFAULT 'SCHEDULED'
                            CHECK (visit_type IN ('SCHEDULED','WALK_IN')),
    priority_level          INTEGER NOT NULL DEFAULT 0 CHECK (priority_level BETWEEN 0 AND 3),
    queue_number            VARCHAR(10),          -- "O01" (WEB) / "W03" (ON_SITE)
    reason                  VARCHAR(255),
    hold_until              TIMESTAMP,            -- Hạn giữ chỗ PENDING
    checked_in_at           TIMESTAMP,
    called_at               TIMESTAMP,
    parent_appointment_id   INTEGER REFERENCES appointments(id),  -- Dùng khi dời lịch
    created_at              TIMESTAMP NOT NULL DEFAULT now(),
    updated_at              TIMESTAMP NOT NULL DEFAULT now()
);

CREATE TABLE appointment_status_history (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    appointment_id  INTEGER NOT NULL REFERENCES appointments(id),
    old_status_id   INTEGER REFERENCES appointment_statuses(id),
    new_status_id   INTEGER NOT NULL REFERENCES appointment_statuses(id),
    performed_by    INTEGER REFERENCES accounts(id),
    changed_at      TIMESTAMP NOT NULL DEFAULT now()
);

-- Thông báo gửi về TÀI KHOẢN ĐẶT LỊCH (booked_by), nội dung ghi rõ tên người khám.
CREATE TABLE notifications (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    account_id      INTEGER NOT NULL REFERENCES accounts(id),
    appointment_id  INTEGER REFERENCES appointments(id),
    title           VARCHAR(150) NOT NULL,
    content         TEXT,
    is_read         BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMP NOT NULL DEFAULT now()
);

-- ==============================================================================
-- 5. CLINICAL & MEDICAL RECORDS
-- ==============================================================================
CREATE TABLE vital_signs (
    id                  INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    appointment_id      INTEGER NOT NULL UNIQUE REFERENCES appointments(id),
    pulse               INTEGER,
    systolic_bp         INTEGER,
    diastolic_bp        INTEGER,
    temperature         NUMERIC(4,1),
    respiratory_rate    INTEGER,
    spo2                INTEGER,
    weight              NUMERIC(5,2),
    height              NUMERIC(5,2),
    recorded_by         INTEGER NOT NULL REFERENCES nurses(id),
    recorded_at         TIMESTAMP NOT NULL DEFAULT now()
);

CREATE TABLE medical_records (
    id                          INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    appointment_id              INTEGER NOT NULL UNIQUE REFERENCES appointments(id),
    doctor_id                   INTEGER NOT NULL REFERENCES doctors(id),
    symptoms                    TEXT,
    clinical_examination        TEXT,
    primary_diagnosis_code      VARCHAR(20),   -- Mã ICD-10
    primary_diagnosis_desc      VARCHAR(255),
    secondary_diagnosis         TEXT,
    conclusion                  TEXT,
    follow_up_date              DATE,          -- Ngày hẹn tái khám (dùng tính chỉ số Continuity of Care)
    created_at                  TIMESTAMP NOT NULL DEFAULT now(),
    updated_at                  TIMESTAMP NOT NULL DEFAULT now()
);

CREATE TABLE lab_orders (
    id                  INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    medical_record_id   INTEGER NOT NULL REFERENCES medical_records(id),
    service_id          INTEGER NOT NULL REFERENCES services(id),
    note                VARCHAR(255),
    status              VARCHAR(20) NOT NULL DEFAULT 'PENDING'
                        CHECK (status IN ('PENDING','COMPLETED')),
    created_at          TIMESTAMP NOT NULL DEFAULT now()
);

CREATE TABLE lab_results (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    order_id        INTEGER NOT NULL UNIQUE REFERENCES lab_orders(id),
    result          TEXT,
    performed_by    INTEGER REFERENCES accounts(id),
    recorded_at     TIMESTAMP NOT NULL DEFAULT now()
);

CREATE TABLE prescriptions (
    id                  INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    medical_record_id   INTEGER NOT NULL REFERENCES medical_records(id),
    prescribed_date     DATE NOT NULL DEFAULT CURRENT_DATE,
    note                VARCHAR(255)
);

CREATE TABLE prescription_items (
    id                  INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    prescription_id     INTEGER NOT NULL REFERENCES prescriptions(id),
    medication_id       INTEGER NOT NULL REFERENCES medications(id),
    dosage              VARCHAR(100),        -- "2 viên/lần, 2 lần/ngày"
    quantity            INTEGER NOT NULL,
    duration_days       INTEGER,
    usage_instructions  VARCHAR(255)         -- "uống sau ăn"
);

-- ==============================================================================
-- 6. BILLING (BHYT chỉ áp dụng ở bước này)
-- ==============================================================================
CREATE TABLE invoices (
    id                          INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    appointment_id              INTEGER NOT NULL UNIQUE REFERENCES appointments(id),
    patient_id                  INTEGER NOT NULL REFERENCES patients(id),
    insurance_plan_id           INTEGER REFERENCES insurance_plans(id),
    insurance_card_id           INTEGER REFERENCES insurance_cards(id),   -- phải là thẻ của chính patient_id (kiểm ở BLL)
    coverage_pct_applied        NUMERIC(5,2) CHECK (coverage_pct_applied BETWEEN 0 AND 100),  -- bản chụp tỉ lệ lúc tính
    total_amount                NUMERIC(12,2) NOT NULL DEFAULT 0,
    insurance_covered_amount    NUMERIC(12,2) NOT NULL DEFAULT 0,
    patient_amount              NUMERIC(12,2) NOT NULL DEFAULT 0,         -- tiền bệnh nhân phải trả sau BHYT
    status                      VARCHAR(20) NOT NULL DEFAULT 'UNPAID'
                                CHECK (status IN ('UNPAID','PARTIALLY_PAID','PAID')),
    created_at                  TIMESTAMP NOT NULL DEFAULT now()
);

CREATE TABLE payments (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    invoice_id      INTEGER NOT NULL REFERENCES invoices(id),
    amount          NUMERIC(12,2) NOT NULL CHECK (amount > 0),
    payment_type    VARCHAR(15) NOT NULL DEFAULT 'PAYMENT'
                    CHECK (payment_type IN ('DEPOSIT','PAYMENT','REFUND')),
    payment_method  VARCHAR(20) NOT NULL CHECK (payment_method IN ('CASH','BANK_TRANSFER')),
    cashier_id      INTEGER REFERENCES accounts(id),
    receipt_email   VARCHAR(100),                     -- Email nhận biên lai điện tử
    note            VARCHAR(255),
    paid_at         TIMESTAMP NOT NULL DEFAULT now()
);

CREATE TABLE invoice_items (
    id          INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    invoice_id  INTEGER NOT NULL REFERENCES invoices(id),
    category    VARCHAR(20) NOT NULL CHECK (category IN ('EXAMINATION','MEDICATION','SERVICE')),
    description VARCHAR(255) NOT NULL,
    amount      NUMERIC(12,2) NOT NULL
);

-- ==============================================================================
-- 7. SYSTEM & AUDIT TRAIL CHUẨN Y TẾ
-- ==============================================================================
CREATE TABLE access_logs (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    account_id          INTEGER REFERENCES accounts(id),        -- Cho phép NULL để ghi nhận cả lỗi đăng nhập sai tài khoản
    patient_id          INTEGER REFERENCES patients(id),
    medical_record_id   INTEGER REFERENCES medical_records(id),
    action              VARCHAR(50) NOT NULL,                   -- 'VIEW_RECORD', 'SHIFT_BID', 'SHIFT_APPROVE', 'LOGIN_FAILED'...
    entity_name         VARCHAR(50),                            -- Tên bảng bị tác động ('doctor_schedules', 'invoices'...)
    entity_id           INTEGER,                                -- ID bản ghi bị tác động
    is_success          BOOLEAN NOT NULL DEFAULT TRUE,          -- TRUE (200/201), FALSE (401/403)
    severity            VARCHAR(20) NOT NULL DEFAULT 'INFO'
                        CHECK (severity IN ('INFO', 'WARNING', 'SECURITY', 'CRITICAL')),
    occurred_at         TIMESTAMP NOT NULL DEFAULT now(),
    ip_address          VARCHAR(45),                            -- IPv4 hoặc IPv6 của client
    user_agent          VARCHAR(255),                           -- Trình duyệt & hệ điều hành client
    details             TEXT                                    -- Chi tiết payload / ghi chú thay đổi (JSON hoặc Text)
);

-- ==============================================================================
-- 8. INDEXES & PARTIAL UNIQUE CONSTRAINTS
-- ==============================================================================
-- Indexes cho Doctors (Trang chủ) & Medical Records (Hẹn tái khám)
CREATE INDEX idx_doctors_homepage ON doctors (is_featured DESC, display_order ASC) WHERE is_public = TRUE;
CREATE INDEX idx_medical_records_followup ON medical_records (doctor_id, follow_up_date) WHERE follow_up_date IS NOT NULL;

-- Indexes & Ràng buộc chống đụng độ cho Shift Bidding (doctor_schedules)
CREATE UNIQUE INDEX uq_doctor_active_shift ON doctor_schedules (doctor_id, work_date, shift)
    WHERE status IN ('BIDDING', 'APPROVED');

-- Chặn cứng 2 bác sĩ được duyệt vào cùng 1 buồng khám trong cùng ngày và cùng ca (Room Collision Check)
CREATE UNIQUE INDEX uq_room_collision_guard ON doctor_schedules (room_id, work_date, shift)
    WHERE status = 'APPROVED';

CREATE INDEX idx_schedules_demand_grid ON doctor_schedules (department_id, work_date, shift, status);
CREATE INDEX idx_schedules_doctor_rotation ON doctor_schedules (doctor_id, work_date, is_hot_shift, status);

-- Indexes cho Slots, Appointments & Clinical
CREATE INDEX idx_slots_doctor_date ON appointment_slots (doctor_id, start_time);
CREATE INDEX idx_slots_schedule ON appointment_slots (schedule_id);
CREATE INDEX idx_appointments_patient ON appointments (patient_id);
CREATE INDEX idx_appointments_status_id ON appointments (status_id);
CREATE INDEX idx_appointments_booked_by ON appointments (booked_by, status_id);
CREATE INDEX idx_appointments_slot ON appointments (slot_id);
CREATE INDEX idx_records_appointment ON medical_records (appointment_id);
CREATE INDEX idx_insurance_cards_patient ON insurance_cards (patient_id, valid_to);
CREATE INDEX idx_payments_invoice ON payments (invoice_id);
CREATE INDEX idx_duty_shifts_account ON duty_shifts (account_id, start_time);
CREATE INDEX idx_notifications_account ON notifications (account_id, is_read);
CREATE INDEX idx_link_patient ON account_patient_profiles (patient_id, status);

-- Indexes cho Access Logs (Đã dọn sạch lỗi khai báo trùng lặp)
CREATE INDEX idx_access_logs_account ON access_logs (account_id);
CREATE INDEX idx_access_logs_patient ON access_logs (patient_id);
CREATE INDEX idx_access_logs_record ON access_logs (medical_record_id);
CREATE INDEX idx_access_logs_occurred_at ON access_logs (occurred_at DESC);
CREATE INDEX idx_access_logs_action_time ON access_logs (action, occurred_at DESC);
CREATE INDEX idx_access_logs_entity ON access_logs (entity_name, entity_id) WHERE entity_name IS NOT NULL;

-- Job nhả giữ chỗ hết hạn (status_id 1 = PENDING)
CREATE INDEX idx_appointments_hold ON appointments (hold_until) WHERE status_id = 1;

-- Mỗi tài khoản chỉ 1 hồ sơ "bản thân"; mỗi bệnh nhân là "bản thân" của tối đa 1 tài khoản
CREATE UNIQUE INDEX uq_link_self_account ON account_patient_profiles (account_id)
    WHERE relationship = 'SELF' AND status <> 'REVOKED';
CREATE UNIQUE INDEX uq_link_self_patient ON account_patient_profiles (patient_id)
    WHERE relationship = 'SELF' AND status = 'ACTIVE';

-- Một bệnh nhân không đặt trùng 1 slot khi lịch còn hiệu lực (status 1..5 = PENDING..IN_PROGRESS)
CREATE UNIQUE INDEX uq_patient_slot_active ON appointments (patient_id, slot_id)
    WHERE slot_id IS NOT NULL AND status_id IN (1,2,3,4,5);

-- ==============================================================================
-- 9. SEED DATA (ID CỐ ĐỊNH)
-- ==============================================================================
INSERT INTO roles (id, code, name) VALUES
 (1,'ADMIN','Quản trị viên'),
 (2,'DEPARTMENT_HEAD','Trưởng khoa'),
 (3,'DOCTOR','Bác sĩ'),
 (4,'NURSE','Y tá / Lễ tân'),
 (5,'PATIENT','Bệnh nhân');

INSERT INTO service_types (id, code, name) VALUES
 (1,'EXAMINATION','Khám bệnh'),
 (2,'LAB_TEST','Xét nghiệm'),
 (3,'IMAGING','Chẩn đoán hình ảnh');

INSERT INTO appointment_statuses (id, code, name, sort_order, is_terminal) VALUES
 (1,'PENDING','Chờ xác nhận',1,false),
 (2,'CONFIRMED','Đã xác nhận',2,false),
 (3,'CHECKED_IN','Đã check-in',3,false),
 (4,'READY_FOR_DOCTOR','Chờ bác sĩ',4,false),
 (5,'IN_PROGRESS','Đang khám',5,false),
 (6,'COMPLETED','Hoàn thành',6,true),
 (7,'CANCELLED','Đã hủy',7,true),
 (8,'NO_SHOW','Không đến',8,true),
 (9,'EXPIRED','Hết hạn giữ chỗ',9,true),
 (10,'RESCHEDULE_REQUIRED','Cần đặt lại lịch',10,false);
```
Lưu ý: KHÔNG chạy đoạn DDL trên lên database. Nó chỉ để tham chiếu khi viết entity/cấu hình; schema thật sinh từ EF migration.

## 6. DAL (HIS.DAL)
- AppDbContext + DbSet cho đủ 33 entity; cấu hình tách thành `IEntityTypeConfiguration<T>` mỗi entity 1 file trong thư mục
  Configurations/, nạp bằng ApplyConfigurationsFromAssembly.
- Extension `AddHisDataAccess(this IServiceCollection, IConfiguration)` đăng ký AddDbContext (UseNpgsql + UseSnakeCaseNamingConvention,
  EnableRetryOnFailure), connection string tên "HisDb".
- Không viết repository.

## 7. BLL (HIS.BLL)
- Extension `AddHisBusiness(this IServiceCollection)`: AddScoped các service, AddValidatorsFromAssembly, AddOptions.
- `Result` / `Result<T>` (IsSuccess, Error{Code,Message}) cho lỗi nghiệp vụ; không dùng exception cho luồng thường.
- `BookingOptions` bind từ section "Booking", ValidateOnStart: HoldMinutes=10, MaxPatientProfilesPerAccount=5,
  MaxActiveAppointmentsPerAccount=5, MaxLinkRequestsPerDay=5, PendingLinkExpiryDays=14, SlotGenerationDaysAhead=14,
  DefaultMaxPatients=40, DefaultOnlineSlots=28, CancelCutoffMinutes=120, CheckInEarlyMinutes=60,
  MaxNoShowsBeforeBlock=3, NoShowWindowDays=90.
- Interface + class rỗng (ném NotImplementedException), mỗi interface 1–3 method đại diện trả `Task<Result<...>>`, DTO là `record` tối thiểu:
  IAuthService (đăng nhập, khóa sau 5 lần sai bằng FailedLoginCount/LockedUntil; đăng ký bệnh nhân),
  IPatientProfileService (RequestLinkAsync, VerifyLinkAsync(linkId, grantRecordAccess) cho nhân viên, RevokeLinkAsync, CreateProfileAsync),
  IPatientAccessService (CanBook: link ACTIVE hoặc PENDING; CanViewRecords: link ACTIVE và can_view_records=true),
  IBookingService, ISlotGenerationService, IScheduleChangeService, IQueueService, IBillingService, INotificationService.
- GHI QUY TẮC NGHIỆP VỤ vào XML-doc của từng interface (đây là hợp đồng cho cả nhóm):
    * Tài khoản ↔ người khám: đăng ký bệnh nhân tạo Account + Patient + link SELF (ACTIVE, can_view_records=true) trong 1 transaction.
    Tạo hồ sơ MỚI → link ACTIVE ngay. can_view_records mặc định true cho SELF và cho CHILD khi bệnh nhân < 18 tuổi;
    false cho mọi quan hệ còn lại (và cho CHILD đã đủ 18 tuổi).
    Hồ sơ ĐÃ CÓ: được tự yêu cầu liên kết với mọi relationship TRỪ SELF. Điều kiện khớp:
      - bệnh nhân < 18 tuổi: họ tên + ngày sinh (chuẩn hóa) + ít nhất 1 trong {national_id, card_number thẻ BHYT, phone_number};
      - bệnh nhân >= 18 tuổi: họ tên + ngày sinh + (national_id HOẶC card_number thẻ BHYT); KHÔNG nhận phone_number.
    Khớp → link PENDING (can_view_records=false); không khớp → lỗi chung "Thông tin không khớp", không tiết lộ dữ liệu hồ sơ.
    Giới hạn MaxLinkRequestsPerDay yêu cầu/tài khoản/ngày. SELF trên hồ sơ có sẵn chỉ do nhân viên quầy tạo sau khi kiểm giấy tờ.
    PENDING → ACTIVE: nhân viên xác nhận khi check-in (có mặt người bệnh hoặc người giám hộ), ghi verified_by/verified_at,
    HOẶC tài khoản ACTIVE đang liên kết duyệt. Với người >= 18 tuổi, chỉ bật can_view_records khi người bệnh đồng ý,
    do nhân viên thực hiện (CHECK ck_link_adult_records_consent). Khi bệnh nhân đủ 18 tuổi, link CHILD phải được xác nhận lại.
    Link PENDING quá PendingLinkExpiryDays không xác nhận → REVOKED. Nhân viên có thể REVOKE link và hủy lịch nếu người bệnh
    không đồng ý; ghi access_logs (LINK_PATIENT / REVOKE_LINK).
    Link PENDING chỉ thấy các lịch có booked_by = chính tài khoản đó.
    Cảnh báo (không chặn) khi tạo hồ sơ mới mà trùng (họ tên + ngày sinh + SĐT). Bệnh nhân vãng lai có thể không có link.
  * appointments.patient_id = người được khám; booked_by = tài khoản đặt (WEB: tài khoản bệnh nhân; ON_SITE: nhân viên quầy).
    Với WEB, patient_id phải thuộc link ACTIVE hoặc PENDING của tài khoản đặt.
  * Thông báo gửi về booked_by, nội dung ghi rõ tên người khám.
  * Slot sinh từ doctor_schedules trừ schedule_exceptions đã APPROVED; job sinh slot phải BỎ QUA (bác sĩ, ngày, ca) có exception đã duyệt
    (shift NULL = cả ngày). Số slot online = online_slots; tại quầy tối đa = max_patients - online_slots (có trần).
  * Giữ chỗ: PENDING với hold_until = now + HoldMinutes; đặt slot bằng UPDATE nguyên tử (booked_count < capacity), kiểm tra số dòng bị ảnh hưởng.
  * Chống gom slot: ≤ MaxPatientProfilesPerAccount link (ACTIVE + PENDING), ≤ MaxActiveAppointmentsPerAccount lịch hiệu lực (theo booked_by),
    1 bệnh nhân không có 2 lịch hiệu lực cùng ngày cùng khoa.
  * Hủy ca: chỉ cho ca bắt đầu trong tương lai. Có bác sĩ thay → UPDATE slot.doctor_id (kiểm tra không trùng uq_slot) giữ nguyên lịch hẹn.
    Không ai thay → trải đều lịch hẹn sang bác sĩ cùng khoa/ngày/ca còn slot (ưu tiên priority_level giảm dần rồi created_at),
    hết chỗ → slot_id=NULL, status RESCHEDULE_REQUIRED. Chuyển lịch hẹn TRƯỚC, rồi mới DELETE slot rỗng, tất cả trong 1 transaction;
    luôn ghi appointment_status_history và gửi Notification dạng chữ (tên bác sĩ/giờ cũ → mới).
  * Bốc số: số cấp từ queue_counters (khóa dòng), tiền tố theo kênh (O.. = WEB, W.. = ON_SITE); hàng đợi sắp theo priority_level giảm dần.
  * BHYT: chỉ ở IBillingService; thẻ phải thuộc đúng invoices.patient_id; kiểm tra hạn thẻ theo NGÀY KHÁM (không theo ngày thanh toán);
    lưu coverage_pct_applied.
    Biên lai điện tử: chỉ gửi đến payments.receipt_email; nếu trống thì dùng email của tài khoản booked_by CHỈ KHI link của tài khoản đó
    với bệnh nhân là ACTIVE. Nội dung biên lai chỉ gồm mã lịch hẹn, ngày, tổng tiền, KHÔNG liệt kê dịch vụ/thuốc. Chuẩn hóa email: trim + chữ thường.
  * Phân quyền dữ liệu: mọi truy vấn phía bệnh nhân đi qua IPatientAccessService; xem bệnh án cần link ACTIVE và can_view_records = true;
    xem hồ sơ của người khác (không phải SELF) phải ghi access_logs có patient_id.
    Quyền của nhân viên với bệnh án: Bác sĩ xem bệnh án của bệnh nhân có lịch hẹn với mình hoặc cùng khoa, nhưng chỉ sửa bệnh án có doctor_id là chính mình;
    y tá xem lịch và sinh hiệu trong khoa mình; trưởng khoa xem trong khoa; ADMIN quản trị hệ thống và KHÔNG xem nội dung bệnh án.
    Mọi lần đọc/sửa medical_records ghi access_logs (VIEW_RECORD / EDIT_RECORD) kèm IP.
- AppointmentStateMachine (static, hàm thuần, BLL, có unit test cho mọi cặp hợp lệ/không hợp lệ):
  PENDING → CONFIRMED | EXPIRED | CANCELLED
  CONFIRMED → CHECKED_IN | CANCELLED | NO_SHOW | RESCHEDULE_REQUIRED
  CHECKED_IN → READY_FOR_DOCTOR | CANCELLED
  READY_FOR_DOCTOR → IN_PROGRESS
  IN_PROGRESS → COMPLETED
  RESCHEDULE_REQUIRED → CANCELLED (đặt lịch mới thì lịch mới có parent_appointment_id trỏ về lịch cũ, lịch cũ → CANCELLED)
  COMPLETED, CANCELLED, NO_SHOW, EXPIRED là trạng thái kết thúc, không chuyển tiếp.
  Mọi chuyển trạng thái ghi appointment_status_history.
  Khởi tạo: lịch web bắt đầu ở PENDING. Lịch walk-in (ON_SITE) tạo thẳng ở CHECKED_IN với checked_in_at = now.
  Lịch ở PENDING bị ảnh hưởng bởi hủy ca thì chuyển CANCELLED và thông báo (không đi qua RESCHEDULE_REQUIRED).
- Đặt lịch web 2 bước: chọn slot → PENDING (hold_until) → bấm xác nhận → CONFIRMED. Hết hạn giữ chỗ → EXPIRED và nhả slot.
- Không cho hủy trong vòng CancelCutoffMinutes trước giờ khám. Check-in mở từ CheckInEarlyMinutes trước giờ khám.
- Sinh mã lịch hẹn không đoán được: appointment_code = HIS-{yyyyMMdd}-{6 ký tự base32 ngẫu nhiên}; nếu vi phạm unique thì sinh lại (tối đa 5 lần).
- Email là thông tin liên lạc CHƯA XÁC MINH, chỉ dùng để gửi thông báo/biên lai, KHÔNG dùng để định danh hay xác thực.
  IAuthService thêm: ChangePasswordAsync (người dùng đã đăng nhập), ResetPasswordByAdminAsync (Admin đặt lại mật khẩu).
  `IEmailSender` (interface trong BLL) + `LoggingEmailSender` (chỉ ghi log) đăng ký mặc định; SMTP thật để sprint sau.
- 1 FluentValidation validator mẫu (RegisterPatientRequestValidator).
- `VietnamTimeProvider : TimeProvider` (LocalTimeZone = "Asia/Ho_Chi_Minh") đăng ký làm TimeProvider; BLL chỉ lấy giờ qua TimeProvider.

## 8. WEB MVC (HIS.Web)
- Program.cs: AddHisDataAccess, AddHisBusiness, Cookie Authentication (LoginPath /Account/Login, AccessDeniedPath /Account/AccessDenied,
  HttpOnly, SameSite=Lax, Secure=Always trừ Development, SlidingExpiration, ExpireTimeSpan 30 phút), AutoValidateAntiforgeryToken toàn cục,
  UseExceptionHandler + trang lỗi, UseHsts/UseHttpsRedirection, AddRateLimiter (policy "login": 5 request/phút/IP áp cho POST login),
  AddHealthChecks().AddDbContextCheck<AppDbContext>() tại /health, app.UseRequestLocalization("vi-VN"), toàn bộ nhãn và thông báo bằng tiếng Việt, ngày hiển thị dd/MM/yyyy.
- Claims: NameIdentifier = account id; Role = role code. Authorization policies: AdminOnly, DepartmentHeadOnly, DoctorOnly, NurseOnly,
  PatientOnly, StaffAny (DepartmentHead/Doctor/Nurse/Admin).
- Areas: Patient, Staff, Admin (+ khu công khai: HomeController, AccountController với Login/Register/Logout/AccessDenied).
  Mỗi controller chỉ có action Index trả view placeholder và `[Authorize(Policy=...)]` ở cấp controller:
  * Patient: BookingController (bước "Khám cho ai": bản thân / người thân / thêm người mới), MyAppointmentsController,
    FamilyProfilesController, MedicalRecordsController, NotificationsController
  * Staff: ReceptionController (NurseOnly: walk-in, liên kết hồ sơ, check-in, hàng đợi, màn check-in có hai ô:
    Xác nhận liên kết người đặt hộ và Người bệnh đồng ý cho xem bệnh án), ConsultationController (DoctorOnly),
    ScheduleController (DepartmentHeadOnly: lịch + duyệt exception), CashierController (NurseOnly)
  * Admin: AccountsController (quản lý tài khoản, đặt lại mật khẩu), CatalogController (khoa, phòng, dịch vụ, thuốc, gói BHYT)
  * Khu công khai: HomeController, AccountController với Login/Register/Logout/AccessDenied/ChangePassword (đổi mật khẩu).
- ViewModel nằm trong Web, dùng DataAnnotations cho validate phía client; nghiệp vụ validate bằng FluentValidation ở BLL.
  Controller mỏng: chỉ gọi service, map Result → ModelState/redirect.
- Layout dùng chung có menu theo role. Dùng Bootstrap có sẵn của template.
- Background worker (BackgroundService, tạo scope qua IServiceScopeFactory): SlotGenerationWorker (mỗi đêm),
  HoldExpirationWorker (mỗi phút), và NoShowWorker (sau khi hết ca: lịch CONFIRMED chưa check-in → NO_SHOW; tài khoản có ≥ MaxNoShowsBeforeBlock lịch NO_SHOW theo booked_by trong NoShowWindowDays ngày bị chặn đặt web mới) — chỉ khung, gọi service rỗng, có try/catch + log.
- Dev seeder `DevDataSeeder`: chỉ chạy khi Development và `Seed:Enabled=true`; nếu bảng accounts rỗng thì tạo:
  1 ADMIN; 2 khoa (mỗi khoa 1 trưởng khoa); 3 phòng khám; 4 bác sĩ và 1 y tá (mỗi người có đủ dòng accounts + doctors/nurses/department_heads);
  1 tài khoản PATIENT kèm 1 Patient + link SELF. Mật khẩu đọc từ `Seed:DefaultPassword` (User Secrets), băm BCrypt.
  Dữ liệu demo: doctor_schedules thứ 2–thứ 6 (ca sáng + chiều, slot 15 phút, max_patients=40, online_slots=28);
  appointment_slots cho 7 ngày tới được CHÈN TRỰC TIẾP theo lịch trên (không gọi SlotGenerationService vì đang là stub), bỏ qua thứ 7/CN;
  3 insurance_plans (đúng tuyến 80%, đúng tuyến 100%, trái tuyến 40%), vài services và medications.
  Thiếu cấu hình thì bỏ qua và log cảnh báo.
- appsettings.json: ConnectionStrings:HisDb = "Host=localhost;Port=5432;Database=his_db;Username=postgres;Password=CHANGE_ME",
  section "Booking", "Seed". KHÔNG hardcode mật khẩu thật; ghi chú dùng `dotnet user-secrets` cho local.
  appsettings.Development.json nằm trong .gitignore.

## 9. TESTS (HIS.Tests)
- Thư mục Unit/ (Moq, không DB) và Integration/ (Testcontainers.PostgreSql, đánh [Trait("Category","Integration")]).
- Mỗi loại 1 test mẫu: Unit — validator đăng ký; Integration — migration áp dụng lên Postgres thật và kiểm tra:
  (a) insert Account với IsActive=false vẫn lưu false, (b) vi phạm CHECK booked_count <= capacity bị từ chối,
  (c) 2 appointment_slots trùng (doctor_id, start_time) bị từ chối, (d) 2 link SELF cho cùng 1 tài khoản bị từ chối,
  (e) cùng 1 bệnh nhân đặt trùng 1 slot khi lịch còn hiệu lực bị từ chối.
  (f) link PENDING có can_view_records=true bị từ chối, (g) hai tài khoản cùng yêu cầu liên kết một hồ sơ nhưng chỉ có một SELF ACTIVE, (h) bật can_view_records=true cho link PARENT khi verified_by NULL bị từ chối.
- KHÔNG dùng EF InMemory (không kiểm tra CHECK/UNIQUE/concurrency nên gây tin sai).

## 10. CI (.github/workflows/ci.yml)
Chạy on push và pull_request, runs-on: ubuntu-latest, setup-dotnet 10.x, restore, step kiểm tra định dạng `dotnet format --verify-no-changes --no-restore`, `dotnet build -c Release --no-restore`,
`dotnet test --no-build -c Release --filter "Category!=Integration"`, rồi step riêng chạy
`--filter "Category=Integration"` (runner có sẵn Docker cho Testcontainers). Bật cache NuGet.

## 11. TÀI LIỆU
- docker-compose.yml ở gốc: postgres:16, cổng 5432, database his_db, user/password lấy từ .env, volume pgdata, healthcheck pg_isready. Thêm .env.example (không chứa mật khẩu thật); .env nằm trong .gitignore.
- README.md: mô tả, yêu cầu (SDK 10, PostgreSQL 16+, Docker nếu chạy integration test), cách chạy local (`docker compose up -d`, user-secrets, `dotnet ef database update`, `dotnet run --project src/HIS.Web`), quy ước nhánh `feature/HIS-<số>-<mô-tả>` và commit `HIS-<số>: <nội dung>` khớp Jira.
- docs/database/erd.md: chứa sơ đồ Mermaid erDiagram sinh từ DDL mục 5 (tiện diff trong PR và làm báo cáo).
- docs/ARCHITECTURE.md: sơ đồ phụ thuộc giữa các project, quy tắc bất biến mục 1.
- docs/DECISIONS.md: EF migration là nguồn sự thật duy nhất của schema (docs/database/schema-reference.sql chỉ để tham chiếu);
  ca khám chỉ sáng/chiều, ca tối là ca trực; xóa slot thay vì thêm trạng thái; quota online/tại quầy nằm ở doctor_schedules;
  BHYT chỉ ở bước tính tiền, thẻ thuộc bệnh nhân; tài khoản–bệnh nhân nhiều–nhiều qua account_patient_profiles,
  tách quyền đặt lịch và quyền xem hồ sơ bệnh án; booked_by tách khỏi patient_id;
  PENDING đặt lịch được nhưng không xem bệnh án; xác minh diễn ra khi check-in; người lớn phải đồng ý mới mở bệnh án;
  id seed của appointment_statuses là cố định vì index một phần phụ thuộc vào chúng;
  accounts.email (unique không phân biệt hoa thường, nullable) và payments.receipt_email (nullable) chỉ là kênh liên lạc chưa xác minh;
  quên mật khẩu qua email (cần bảng token + SMTP) để sprint sau, hiện Admin đặt lại mật khẩu;
- .github/pull_request_template.md: checklist (build xanh, test, Jira key, ảnh chụp màn hình nếu có UI).

## 12. CÁC BƯỚC (dừng chờ tôi sau mỗi bước)
1. Solution + 5 project + reference + Directory.*.props + global.json + .gitignore + .editorconfig + docs/database/schema-reference.sql. Chạy `dotnet build`.
2. Domain: enum + 33 entity (mục 3–5). Chạy `dotnet build`.
3. DAL: AppDbContext, Configurations, converter, seed, AddHisDataAccess. Chạy `dotnet build`.
4. BLL: Options, Result, interface + class rỗng + XML-doc quy tắc, TimeProvider, AddHisBusiness. Chạy `dotnet build`.
5. Web: Program.cs, auth, policy, areas/controller/view placeholder, worker, health, seeder, appsettings. Chạy `dotnet build` và
   `dotnet run` ngắn để chắc ứng dụng khởi động được (lỗi kết nối DB là chấp nhận được, nhưng phải báo rõ).
6. Tests + CI + tài liệu (docker-compose.yml, .env.example, erd.md, ARCHITECTURE.md, DECISIONS.md). Chạy dotnet format một lần để CI không đỏ ngay lần push đầu, rồi chạy `dotnet test --filter "Category!=Integration"`.
7. Chạy `dotnet ef migrations add InitialCreate --project src/HIS.DAL --startup-project src/HIS.Web --output-dir Migrations`.
   Sau đó ĐỐI CHIẾU migration với DDL mục 5: liệt kê từng bảng, cột, CHECK, UNIQUE, index một phần và nêu mọi sai lệch
   (đặc biệt: tên bảng/cột snake_case, 3 index một phần, khóa chính ghép, CHECK ck_online_slots/ck_exception_*).
   TUYỆT ĐỐI KHÔNG chạy `dotnet ef database update`.