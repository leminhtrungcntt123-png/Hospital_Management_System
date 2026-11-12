-- ─── LOOKUP TABLES ────────────────────────────────────
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

-- ─── ACCOUNTS & STAFF ────────────────────────────────
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
    updated_at          TIMESTAMP NOT NULL DEFAULT now()
);

CREATE TABLE departments (
    id          INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name        VARCHAR(100) NOT NULL,
    description VARCHAR(255)
);

CREATE TABLE duty_shifts (
    id             INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    account_id     INTEGER NOT NULL REFERENCES accounts(id),   -- bác sĩ hoặc y tá được phân trực
    department_id  INTEGER NOT NULL REFERENCES departments(id),
    start_time     TIMESTAMP NOT NULL,                         -- TIMESTAMP để trực đêm qua nửa đêm
    end_time       TIMESTAMP NOT NULL,
    note           VARCHAR(255),
    assigned_by    INTEGER REFERENCES accounts(id),            -- Trưởng khoa phân công
    created_at     TIMESTAMP NOT NULL DEFAULT now(),
    CONSTRAINT ck_duty_time CHECK (end_time > start_time)
);

CREATE TABLE clinic_rooms (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    department_id   INTEGER NOT NULL REFERENCES departments(id),
    room_name       VARCHAR(50) NOT NULL,
    room_number     VARCHAR(20) NOT NULL
);

-- Hồ sơ người khám. KHÔNG còn account_id: quan hệ với tài khoản nằm ở account_patient_profiles.
-- Bệnh nhân vãng lai (walk-in) có thể không có dòng liên kết nào.
CREATE TABLE patients (
    id              INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    full_name       VARCHAR(100) NOT NULL,
    date_of_birth   DATE NOT NULL,
    gender          VARCHAR(10) CHECK (gender IN ('MALE','FEMALE','OTHER')),
    national_id     VARCHAR(20) UNIQUE,               -- NULL với trẻ chưa có CCCD
    phone_number    VARCHAR(15) NOT NULL,
    address         VARCHAR(255),
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

CREATE TABLE doctors (
    id                          INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    account_id                  INTEGER NOT NULL UNIQUE REFERENCES accounts(id),
    department_id               INTEGER NOT NULL REFERENCES departments(id),
    full_name                   VARCHAR(100) NOT NULL,
    specialty                   VARCHAR(100),
    license_number              VARCHAR(30),                 -- Số chứng chỉ hành nghề
    avg_consultation_minutes    INTEGER NOT NULL DEFAULT 15  -- Dùng tính ETA hàng đợi
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

-- ─── CATALOGS (ADMIN-MANAGED) ────────────────────────
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
    name            VARCHAR(100) NOT NULL,       -- VD: "Đúng tuyến - 80%"
    coverage_pct    NUMERIC(5,2) NOT NULL,       -- 80.00 / 95.00 / 100.00
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

-- ─── SCHEDULING ENGINE ────────────────────────────────
-- Ca KHÁM chỉ có sáng/chiều. Ca tối là ca TRỰC (duty_shifts), không sinh slot.
CREATE TABLE doctor_schedules (
    id                      INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    doctor_id               INTEGER NOT NULL REFERENCES doctors(id),
    room_id                 INTEGER NOT NULL REFERENCES clinic_rooms(id),
    day_of_week             INTEGER NOT NULL CHECK (day_of_week BETWEEN 0 AND 6), -- 0=CN, 1=T2 ... 6=T7
    shift                   VARCHAR(10) NOT NULL CHECK (shift IN ('MORNING','AFTERNOON')),
    start_time              TIME NOT NULL,
    end_time                TIME NOT NULL,
    slot_duration_minutes   INTEGER NOT NULL DEFAULT 15,
    max_patients            INTEGER NOT NULL DEFAULT 40,   -- tổng bệnh nhân / ca
    online_slots            INTEGER NOT NULL DEFAULT 28,   -- số slot online; tại quầy tối đa = max_patients - online_slots
    effective_from          DATE NOT NULL,
    effective_to            DATE,
    is_active               BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT ck_online_slots CHECK (online_slots BETWEEN 0 AND max_patients)
);

CREATE TABLE schedule_exceptions (
    id                      INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
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
    doctor_id       INTEGER NOT NULL REFERENCES doctors(id),
    room_id         INTEGER NOT NULL REFERENCES clinic_rooms(id),
    start_time      TIMESTAMP NOT NULL,
    end_time        TIMESTAMP NOT NULL,
    capacity        INTEGER NOT NULL DEFAULT 1,
    booked_count    INTEGER NOT NULL DEFAULT 0,
    status          VARCHAR(15) NOT NULL DEFAULT 'AVAILABLE'
                    CHECK (status IN ('AVAILABLE','FULL','BLOCKED')),
    version         INTEGER NOT NULL DEFAULT 0,    -- Optimistic locking
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

-- ─── CLINICAL & MEDICAL RECORDS ───────────────────────
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

-- ─── BILLING (BHYT chỉ áp dụng ở bước này) ────────────
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
    receipt_email   VARCHAR(100),                         -- Email nhận biên lai điện tử
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

-- ─── SYSTEM ────────────────────────────────────────────
-- patient_id: ghi vết khi người nhà xem/được liên kết hồ sơ (kể cả hành động không gắn với một medical_record)
CREATE TABLE access_logs (
    id                  INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    account_id          INTEGER NOT NULL REFERENCES accounts(id),
    patient_id          INTEGER REFERENCES patients(id),
    medical_record_id   INTEGER REFERENCES medical_records(id),
    action              VARCHAR(50) NOT NULL,   -- "VIEW_RECORD", "EDIT_RECORD", "LINK_PATIENT", "REVOKE_LINK"
    occurred_at         TIMESTAMP NOT NULL DEFAULT now(),
    ip_address          VARCHAR(45)
);

-- ─── INDEXES ───────────────────────────────────────────
CREATE INDEX idx_slots_doctor_date ON appointment_slots (doctor_id, start_time);
CREATE INDEX idx_appointments_patient ON appointments (patient_id);
CREATE INDEX idx_appointments_status_id ON appointments (status_id);
CREATE INDEX idx_appointments_booked_by ON appointments (booked_by, status_id);
CREATE INDEX idx_appointments_slot ON appointments (slot_id);
CREATE INDEX idx_records_appointment ON medical_records (appointment_id);
CREATE INDEX idx_access_logs_account ON access_logs (account_id);
CREATE INDEX idx_access_logs_patient ON access_logs (patient_id);
CREATE INDEX idx_insurance_cards_patient ON insurance_cards (patient_id, valid_to);
CREATE INDEX idx_payments_invoice ON payments (invoice_id);
CREATE INDEX idx_duty_shifts_account ON duty_shifts (account_id, start_time);
CREATE INDEX idx_notifications_account ON notifications (account_id, is_read);
CREATE INDEX idx_link_patient ON account_patient_profiles (patient_id, status);

-- job nhả giữ chỗ hết hạn (status_id 1 = PENDING)
CREATE INDEX idx_appointments_hold ON appointments (hold_until) WHERE status_id = 1;

-- mỗi tài khoản chỉ 1 hồ sơ "bản thân"; mỗi bệnh nhân là "bản thân" của tối đa 1 tài khoản
CREATE UNIQUE INDEX uq_link_self_account ON account_patient_profiles (account_id)
    WHERE relationship = 'SELF' AND status <> 'REVOKED';
CREATE UNIQUE INDEX uq_link_self_patient ON account_patient_profiles (patient_id)
    WHERE relationship = 'SELF' AND status = 'ACTIVE';

-- một bệnh nhân không đặt trùng 1 slot khi lịch còn hiệu lực (status 1..5 = PENDING..IN_PROGRESS)
CREATE UNIQUE INDEX uq_patient_slot_active ON appointments (patient_id, slot_id)
    WHERE slot_id IS NOT NULL AND status_id IN (1,2,3,4,5);

-- ─── SEED (cấu hình bằng HasData trong EF, id cố định) ─
INSERT INTO roles (id, code, name) VALUES
 (1,'ADMIN','Quản trị viên'),(2,'DEPARTMENT_HEAD','Trưởng khoa'),(3,'DOCTOR','Bác sĩ'),
 (4,'NURSE','Y tá / Lễ tân'),(5,'PATIENT','Bệnh nhân');
INSERT INTO service_types (id, code, name) VALUES
 (1,'EXAMINATION','Khám bệnh'),(2,'LAB_TEST','Xét nghiệm'),(3,'IMAGING','Chẩn đoán hình ảnh');
INSERT INTO appointment_statuses (id, code, name, sort_order, is_terminal) VALUES
 (1,'PENDING','Chờ xác nhận',1,false),(2,'CONFIRMED','Đã xác nhận',2,false),
 (3,'CHECKED_IN','Đã check-in',3,false),(4,'READY_FOR_DOCTOR','Chờ bác sĩ',4,false),
 (5,'IN_PROGRESS','Đang khám',5,false),(6,'COMPLETED','Hoàn thành',6,true),
 (7,'CANCELLED','Đã hủy',7,true),(8,'NO_SHOW','Không đến',8,true),
 (9,'EXPIRED','Hết hạn giữ chỗ',9,true),(10,'RESCHEDULE_REQUIRED','Cần đặt lại lịch',10,false);