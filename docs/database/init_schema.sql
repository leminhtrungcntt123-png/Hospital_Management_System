-- ==============================================================================
-- 0. RESET DATABASE (XÓA BẢNG CŨ THEO THỨ TỰ AN TOÀN)
-- ==============================================================================
DROP TABLE IF EXISTS 
    access_logs,
    invoice_items,
    payments,
    deposit_usages,
    deposits,
    invoices,
    cashier_shifts,
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

DROP SEQUENCE IF EXISTS seq_invoice_number, seq_transaction_code, seq_deposit_code;

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
    allergies       VARCHAR(500),
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
    id                   INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name                 VARCHAR(150) NOT NULL,
    active_ingredient    VARCHAR(150),
    strength             VARCHAR(50),
    unit                 VARCHAR(20) NOT NULL,       -- viên, chai, ống...
    price                NUMERIC(12,2) NOT NULL DEFAULT 0,
    is_insurance_covered BOOLEAN NOT NULL DEFAULT TRUE,
    is_active            BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE services (
    id                   INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name                 VARCHAR(150) NOT NULL,
    service_type_id      INTEGER NOT NULL REFERENCES service_types(id),
    price                NUMERIC(12,2) NOT NULL DEFAULT 0,
    is_insurance_covered BOOLEAN NOT NULL DEFAULT TRUE,
    is_active            BOOLEAN NOT NULL DEFAULT TRUE
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
-- 6. BILLING, DEPOSITS & CASHIER SHIFT (BHYT chỉ áp dụng ở bước này)
-- ==============================================================================
CREATE TABLE cashier_shifts (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    cashier_id      INTEGER NOT NULL REFERENCES accounts(id),
    opened_at       TIMESTAMP NOT NULL DEFAULT now(),
    closed_at       TIMESTAMP,
    opening_cash    NUMERIC(18,0) NOT NULL DEFAULT 0,
    expected_cash   NUMERIC(18,0),
    counted_cash    NUMERIC(18,0),
    difference      NUMERIC(18,0),
    status          VARCHAR(20) NOT NULL DEFAULT 'OPEN'
                    CHECK (status IN ('OPEN','CLOSED')),
    note            VARCHAR(255)
);

CREATE UNIQUE INDEX uq_cashier_shifts_one_open_per_cashier ON cashier_shifts(cashier_id) WHERE status = 'OPEN';

CREATE TABLE invoices (
    id                          INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    invoice_number              VARCHAR(20) NOT NULL UNIQUE,
    appointment_id              INTEGER NOT NULL UNIQUE REFERENCES appointments(id),
    patient_id                  INTEGER NOT NULL REFERENCES patients(id),
    exam_date                   DATE NOT NULL DEFAULT CURRENT_DATE,
    insurance_plan_id           INTEGER REFERENCES insurance_plans(id),
    insurance_card_id           INTEGER REFERENCES insurance_cards(id),   -- phải là thẻ của chính patient_id (kiểm ở BLL)
    coverage_pct_applied        NUMERIC(5,2) CHECK (coverage_pct_applied BETWEEN 0 AND 100),  -- bản chụp tỉ lệ lúc tính
    total_amount                NUMERIC(18,0) NOT NULL DEFAULT 0,
    insurance_covered_amount    NUMERIC(18,0) NOT NULL DEFAULT 0,
    patient_amount              NUMERIC(18,0) NOT NULL DEFAULT 0,         -- tiền bệnh nhân phải trả sau BHYT
    status                      VARCHAR(20) NOT NULL DEFAULT 'UNPAID'
                                CHECK (status IN ('UNPAID','PARTIALLY_PAID','PAID')),
    created_at                  TIMESTAMP NOT NULL DEFAULT now()
);

CREATE TABLE deposits (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    deposit_code    VARCHAR(32) NOT NULL UNIQUE,
    patient_id      INTEGER NOT NULL REFERENCES patients(id),
    appointment_id  INTEGER REFERENCES appointments(id),
    amount          NUMERIC(18,0) NOT NULL CHECK (amount > 0),
    used_amount     NUMERIC(18,0) NOT NULL DEFAULT 0 CHECK (used_amount >= 0),
    refunded_amount NUMERIC(18,0) NOT NULL DEFAULT 0 CHECK (refunded_amount >= 0),
    status          VARCHAR(20) NOT NULL DEFAULT 'ACTIVE'
                    CHECK (status IN ('ACTIVE','DEPLETED','REFUNDED')),
    payment_method  VARCHAR(20) NOT NULL CHECK (payment_method IN ('CASH','BANK_TRANSFER')),
    reference_code  VARCHAR(100),
    purpose         VARCHAR(255),
    approved_by     VARCHAR(150),
    note            VARCHAR(500),
    cashier_id      INTEGER REFERENCES accounts(id),
    shift_id        INTEGER REFERENCES cashier_shifts(id),
    created_at      TIMESTAMP NOT NULL DEFAULT now()
);

CREATE TABLE payments (
    id                  INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    transaction_code    VARCHAR(30) NOT NULL UNIQUE,
    invoice_id          INTEGER REFERENCES invoices(id),
    deposit_id          INTEGER REFERENCES deposits(id),
    original_payment_id INTEGER REFERENCES payments(id),
    amount              NUMERIC(18,0) NOT NULL CHECK (amount > 0),
    payment_type        VARCHAR(15) NOT NULL DEFAULT 'PAYMENT'
                        CHECK (payment_type IN ('DEPOSIT','PAYMENT','REFUND')),
    payment_method      VARCHAR(25) NOT NULL
                        CHECK (payment_method IN ('CASH','BANK_TRANSFER','DEPOSIT_DEDUCTION')),
    reference_code      VARCHAR(100),
    status              VARCHAR(20) NOT NULL DEFAULT 'CONFIRMED'
                        CHECK (status IN ('PENDING','CONFIRMED')),
    idempotency_key     VARCHAR(100),
    cashier_id          INTEGER REFERENCES accounts(id),
    shift_id            INTEGER REFERENCES cashier_shifts(id),
    receipt_email       VARCHAR(100),                     -- Email nhận biên lai điện tử
    note                VARCHAR(255),
    refund_reason       VARCHAR(500),
    paid_at             TIMESTAMP NOT NULL DEFAULT now()
);

CREATE SEQUENCE IF NOT EXISTS seq_invoice_number START WITH 100 INCREMENT BY 1;
CREATE SEQUENCE IF NOT EXISTS seq_transaction_code START WITH 200 INCREMENT BY 1;
CREATE SEQUENCE IF NOT EXISTS seq_deposit_code START WITH 100 INCREMENT BY 1;

CREATE TABLE invoice_items (
    id                   INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    invoice_id           INTEGER NOT NULL REFERENCES invoices(id),
    category             VARCHAR(20) NOT NULL CHECK (category IN ('EXAMINATION','MEDICATION','SERVICE')),
    description          VARCHAR(255) NOT NULL,
    service_id           INTEGER REFERENCES services(id),
    medication_id        INTEGER REFERENCES medications(id),
    unit_price           NUMERIC(18,0) NOT NULL DEFAULT 0,
    quantity             INTEGER NOT NULL DEFAULT 1,
    amount               NUMERIC(18,0) NOT NULL,
    is_insurance_covered BOOLEAN NOT NULL DEFAULT TRUE,
    coverage_pct_applied NUMERIC(5,2) NOT NULL DEFAULT 0,
    covered_amount       NUMERIC(18,0) NOT NULL DEFAULT 0,
    patient_amount       NUMERIC(18,0) NOT NULL DEFAULT 0
);

CREATE TABLE deposit_usages (
    id          INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    deposit_id  INTEGER NOT NULL REFERENCES deposits(id),
    invoice_id  INTEGER NOT NULL REFERENCES invoices(id),
    amount      NUMERIC(18,0) NOT NULL CHECK (amount > 0),
    used_at     TIMESTAMP NOT NULL DEFAULT now(),
    cashier_id  INTEGER REFERENCES accounts(id)
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
 (5,'PATIENT','Bệnh nhân'),
 (6,'ACCOUNTANT','Kế toán / Thu ngân');

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

