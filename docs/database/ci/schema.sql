-- Khung rỗng của CSDL HIS, sinh từ model EF Core (HIS.DAL/AppDbContext + Configurations/).
-- Lý do CI không chạy `dotnet ef database update`: 20261103165420_AlignToGroupSpec viết tay để
-- PATCH CSDL hospital_his sẵn có của nhóm (rename constraint, remap id, di trú dữ liệu),
-- nên nó không chạy được trên CSDL trống. File này là ảnh chụp "model => schema" cho một DB mới,
-- end state giống hệt DB đã migrate.
--
-- Sinh lại sau khi sửa entity/configuration (và trước khi tạo migration mới):
--   1. tạm xóa src/HIS.DAL/Migrations (dòng lịch sử dưới đây chính là 6 migration đã commit);
--   2. dotnet ef migrations add SchemaFromModel --project src/HIS.DAL --output-dir MigrationsFresh
--      (cần env ConnectionStrings__DefaultConnection trỏ tới một DB bất kỳ, lệnh này không ghi DB);
--   3. dotnet ef migrations script --project src/HIS.DAL --output docs/database/ci/schema.sql
--   4. khôi phục src/HIS.DAL/Migrations (git checkout -- src/HIS.DAL/Migrations), xóa MigrationsFresh,
--      thay dòng INSERT __EFMigrationsHistory của SchemaFromModel bằng 6 id migration thật ở cuối file.
--
-- Sau khi nạp: chạy docs/database/ci/schema_checks.sql (18 kiểm tra cấu trúc) để chắc DB mới
-- vẫn khớp đúng cấu trúc mà gate đã chốt trên hospital_his và his_rehearsal.
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;
CREATE TABLE appointment_statuses (
    id integer NOT NULL,
    code character varying(20) NOT NULL,
    name character varying(50) NOT NULL,
    sort_order integer NOT NULL,
    is_terminal boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT pk_appointment_statuses PRIMARY KEY (id)
);

CREATE TABLE departments (
    id integer GENERATED ALWAYS AS IDENTITY,
    name character varying(100) NOT NULL,
    description character varying(255),
    CONSTRAINT pk_departments PRIMARY KEY (id)
);

CREATE TABLE insurance_plans (
    id integer GENERATED ALWAYS AS IDENTITY,
    name character varying(100) NOT NULL,
    coverage_pct numeric(5,2) NOT NULL,
    referral_type character varying(20),
    CONSTRAINT pk_insurance_plans PRIMARY KEY (id),
    CONSTRAINT ck_insurance_plans_referral_type CHECK (referral_type IS NULL OR referral_type IN ('IN_NETWORK','OUT_OF_NETWORK'))
);

CREATE TABLE medications (
    id integer GENERATED ALWAYS AS IDENTITY,
    name character varying(150) NOT NULL,
    active_ingredient character varying(150),
    strength character varying(50),
    unit character varying(20) NOT NULL,
    price numeric(12,2) NOT NULL DEFAULT 0.0,
    is_active boolean NOT NULL DEFAULT TRUE,
    CONSTRAINT pk_medications PRIMARY KEY (id)
);

CREATE TABLE patients (
    id integer GENERATED ALWAYS AS IDENTITY,
    full_name character varying(100) NOT NULL,
    date_of_birth date NOT NULL,
    gender character varying(10),
    national_id character varying(20),
    phone_number character varying(15) NOT NULL,
    address character varying(255),
    email character varying(200),
    created_at timestamp without time zone NOT NULL DEFAULT (now()),
    CONSTRAINT pk_patients PRIMARY KEY (id),
    CONSTRAINT ck_patients_gender CHECK (gender IS NULL OR gender IN ('MALE','FEMALE','OTHER'))
);

CREATE TABLE roles (
    id integer NOT NULL,
    code character varying(20) NOT NULL,
    name character varying(50) NOT NULL,
    description character varying(255),
    CONSTRAINT pk_roles PRIMARY KEY (id)
);

CREATE TABLE service_types (
    id integer NOT NULL,
    code character varying(20) NOT NULL,
    name character varying(50) NOT NULL,
    CONSTRAINT pk_service_types PRIMARY KEY (id)
);

CREATE TABLE clinic_rooms (
    id integer GENERATED ALWAYS AS IDENTITY,
    department_id integer NOT NULL,
    room_name character varying(50) NOT NULL,
    room_number character varying(20) NOT NULL,
    CONSTRAINT pk_clinic_rooms PRIMARY KEY (id),
    CONSTRAINT fk_clinic_rooms_departments_department_id FOREIGN KEY (department_id) REFERENCES departments (id) ON DELETE RESTRICT
);

CREATE TABLE insurance_cards (
    id integer GENERATED ALWAYS AS IDENTITY,
    patient_id integer NOT NULL,
    card_number character varying(20) NOT NULL,
    insurance_plan_id integer NOT NULL,
    registered_facility character varying(150),
    is_registered_here boolean NOT NULL DEFAULT FALSE,
    valid_from date NOT NULL,
    valid_to date NOT NULL,
    is_active boolean NOT NULL DEFAULT TRUE,
    created_at timestamp without time zone NOT NULL DEFAULT (now()),
    CONSTRAINT pk_insurance_cards PRIMARY KEY (id),
    CONSTRAINT ck_insurance_dates CHECK (valid_to >= valid_from),
    CONSTRAINT fk_insurance_cards_insurance_plans_insurance_plan_id FOREIGN KEY (insurance_plan_id) REFERENCES insurance_plans (id) ON DELETE RESTRICT,
    CONSTRAINT fk_insurance_cards_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE RESTRICT
);

CREATE TABLE accounts (
    id integer GENERATED ALWAYS AS IDENTITY,
    username character varying(50) NOT NULL,
    email character varying(100),
    password_hash character varying(255) NOT NULL,
    role_id integer NOT NULL,
    is_active boolean NOT NULL DEFAULT TRUE,
    failed_login_count integer NOT NULL DEFAULT 0,
    locked_until timestamp without time zone,
    booking_blocked_until timestamp without time zone,
    no_show_reset_at timestamp without time zone,
    created_at timestamp without time zone NOT NULL DEFAULT (now()),
    updated_at timestamp without time zone NOT NULL DEFAULT (now()),
    CONSTRAINT pk_accounts PRIMARY KEY (id),
    CONSTRAINT ck_accounts_email_lower CHECK (email IS NULL OR email = lower(email)),
    CONSTRAINT fk_accounts_roles_role_id FOREIGN KEY (role_id) REFERENCES roles (id) ON DELETE RESTRICT
);

CREATE TABLE services (
    id integer GENERATED ALWAYS AS IDENTITY,
    name character varying(150) NOT NULL,
    service_type_id integer NOT NULL,
    price numeric(12,2) NOT NULL DEFAULT 0.0,
    is_active boolean NOT NULL DEFAULT TRUE,
    CONSTRAINT pk_services PRIMARY KEY (id),
    CONSTRAINT fk_services_service_types_service_type_id FOREIGN KEY (service_type_id) REFERENCES service_types (id) ON DELETE RESTRICT
);

CREATE TABLE account_patient_profiles (
    account_id integer NOT NULL,
    patient_id integer NOT NULL,
    relationship character varying(15) NOT NULL,
    status character varying(10) NOT NULL DEFAULT 'ACTIVE',
    can_view_records boolean NOT NULL DEFAULT FALSE,
    created_by integer,
    verified_by integer,
    verified_at timestamp without time zone,
    created_at timestamp without time zone NOT NULL DEFAULT (now()),
    CONSTRAINT pk_account_patient_profiles PRIMARY KEY (account_id, patient_id),
    CONSTRAINT ck_account_patient_profiles_relationship CHECK (relationship IN ('SELF','SPOUSE','CHILD','PARENT','SIBLING','OTHER')),
    CONSTRAINT ck_account_patient_profiles_status CHECK (status IN ('ACTIVE','PENDING','REVOKED')),
    CONSTRAINT ck_link_adult_records_consent CHECK (can_view_records = FALSE OR relationship IN ('SELF','CHILD') OR verified_by IS NOT NULL),
    CONSTRAINT ck_link_pending_no_records CHECK (status = 'ACTIVE' OR can_view_records = FALSE),
    CONSTRAINT ck_link_self_not_pending CHECK (NOT (relationship = 'SELF' AND status = 'PENDING')),
    CONSTRAINT fk_account_patient_profiles_accounts_account_id FOREIGN KEY (account_id) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT fk_account_patient_profiles_accounts_created_by FOREIGN KEY (created_by) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT fk_account_patient_profiles_accounts_verified_by FOREIGN KEY (verified_by) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT fk_account_patient_profiles_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE RESTRICT
);

CREATE TABLE department_heads (
    id integer GENERATED ALWAYS AS IDENTITY,
    account_id integer NOT NULL,
    department_id integer NOT NULL,
    full_name character varying(100) NOT NULL,
    CONSTRAINT pk_department_heads PRIMARY KEY (id),
    CONSTRAINT fk_department_heads_accounts_account_id FOREIGN KEY (account_id) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT fk_department_heads_departments_department_id FOREIGN KEY (department_id) REFERENCES departments (id) ON DELETE RESTRICT
);

CREATE TABLE doctors (
    id integer GENERATED ALWAYS AS IDENTITY,
    account_id integer NOT NULL,
    department_id integer NOT NULL,
    full_name character varying(100) NOT NULL,
    specialty character varying(100),
    license_number character varying(30),
    avg_consultation_minutes integer NOT NULL DEFAULT 15,
    CONSTRAINT pk_doctors PRIMARY KEY (id),
    CONSTRAINT fk_doctors_accounts_account_id FOREIGN KEY (account_id) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT fk_doctors_departments_department_id FOREIGN KEY (department_id) REFERENCES departments (id) ON DELETE RESTRICT
);

CREATE TABLE duty_shifts (
    id integer GENERATED ALWAYS AS IDENTITY,
    account_id integer NOT NULL,
    department_id integer NOT NULL,
    start_time timestamp without time zone NOT NULL,
    end_time timestamp without time zone NOT NULL,
    note character varying(255),
    assigned_by integer,
    created_at timestamp without time zone NOT NULL DEFAULT (now()),
    CONSTRAINT pk_duty_shifts PRIMARY KEY (id),
    CONSTRAINT ck_duty_time CHECK (end_time > start_time),
    CONSTRAINT fk_duty_shifts_accounts_account_id FOREIGN KEY (account_id) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT fk_duty_shifts_accounts_assigned_by FOREIGN KEY (assigned_by) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT fk_duty_shifts_departments_department_id FOREIGN KEY (department_id) REFERENCES departments (id) ON DELETE RESTRICT
);

CREATE TABLE nurses (
    id integer GENERATED ALWAYS AS IDENTITY,
    account_id integer NOT NULL,
    department_id integer,
    full_name character varying(100) NOT NULL,
    CONSTRAINT pk_nurses PRIMARY KEY (id),
    CONSTRAINT fk_nurses_accounts_account_id FOREIGN KEY (account_id) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT fk_nurses_departments_department_id FOREIGN KEY (department_id) REFERENCES departments (id) ON DELETE RESTRICT
);

CREATE TABLE appointment_slots (
    id integer GENERATED ALWAYS AS IDENTITY,
    doctor_id integer NOT NULL,
    room_id integer NOT NULL,
    start_time timestamp without time zone NOT NULL,
    end_time timestamp without time zone NOT NULL,
    capacity integer NOT NULL DEFAULT 1,
    booked_count integer NOT NULL DEFAULT 0,
    online_quota integer NOT NULL DEFAULT 3,
    online_booked integer NOT NULL DEFAULT 0,
    status character varying(15) NOT NULL DEFAULT 'AVAILABLE',
    version integer NOT NULL DEFAULT 0,
    CONSTRAINT pk_appointment_slots PRIMARY KEY (id),
    CONSTRAINT ck_appointment_slots_capacity CHECK (booked_count <= capacity),
    CONSTRAINT ck_appointment_slots_status CHECK (status IN ('AVAILABLE','FULL','BLOCKED')),
    CONSTRAINT ck_online_booked CHECK (online_booked <= online_quota AND online_booked <= booked_count),
    CONSTRAINT ck_online_quota CHECK (online_quota <= capacity),
    CONSTRAINT fk_appointment_slots_clinic_rooms_room_id FOREIGN KEY (room_id) REFERENCES clinic_rooms (id) ON DELETE RESTRICT,
    CONSTRAINT fk_appointment_slots_doctors_doctor_id FOREIGN KEY (doctor_id) REFERENCES doctors (id) ON DELETE RESTRICT
);

CREATE TABLE doctor_schedules (
    id integer GENERATED ALWAYS AS IDENTITY,
    doctor_id integer NOT NULL,
    room_id integer NOT NULL,
    day_of_week integer NOT NULL,
    shift character varying(10) NOT NULL,
    start_time time without time zone NOT NULL,
    end_time time without time zone NOT NULL,
    slot_duration_minutes integer NOT NULL DEFAULT 60,
    online_ratio_pct integer NOT NULL DEFAULT 60,
    effective_from date NOT NULL,
    effective_to date,
    is_active boolean NOT NULL DEFAULT TRUE,
    CONSTRAINT pk_doctor_schedules PRIMARY KEY (id),
    CONSTRAINT ck_doctor_schedules_day_of_week CHECK (day_of_week BETWEEN 0 AND 6),
    CONSTRAINT ck_doctor_schedules_online_ratio CHECK (online_ratio_pct BETWEEN 0 AND 100),
    CONSTRAINT ck_doctor_schedules_shift CHECK (shift IN ('MORNING','AFTERNOON')),
    CONSTRAINT fk_doctor_schedules_clinic_rooms_room_id FOREIGN KEY (room_id) REFERENCES clinic_rooms (id) ON DELETE RESTRICT,
    CONSTRAINT fk_doctor_schedules_doctors_doctor_id FOREIGN KEY (doctor_id) REFERENCES doctors (id) ON DELETE RESTRICT
);

CREATE TABLE schedule_exceptions (
    id integer GENERATED ALWAYS AS IDENTITY,
    doctor_id integer NOT NULL,
    date date NOT NULL,
    shift character varying(10),
    type character varying(20) NOT NULL,
    status character varying(10) NOT NULL DEFAULT 'PENDING',
    replacement_doctor_id integer,
    reason character varying(255),
    approved_by integer,
    approved_at timestamp without time zone,
    created_at timestamp without time zone NOT NULL DEFAULT (now()),
    CONSTRAINT pk_schedule_exceptions PRIMARY KEY (id),
    CONSTRAINT ck_exception_approved CHECK (status <> 'APPROVED' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL)),
    CONSTRAINT ck_exception_replacement CHECK (replacement_doctor_id IS NULL OR replacement_doctor_id <> doctor_id),
    CONSTRAINT ck_schedule_exceptions_shift CHECK (shift IS NULL OR shift IN ('MORNING','AFTERNOON')),
    CONSTRAINT ck_schedule_exceptions_status CHECK (status IN ('PENDING','APPROVED','REJECTED')),
    CONSTRAINT ck_schedule_exceptions_type CHECK (type IN ('LEAVE','TRAINING','SURGERY','SHIFT_SWAP')),
    CONSTRAINT fk_schedule_exceptions_accounts_approved_by FOREIGN KEY (approved_by) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT fk_schedule_exceptions_doctors_doctor_id FOREIGN KEY (doctor_id) REFERENCES doctors (id) ON DELETE RESTRICT,
    CONSTRAINT fk_schedule_exceptions_doctors_replacement_doctor_id FOREIGN KEY (replacement_doctor_id) REFERENCES doctors (id) ON DELETE RESTRICT
);

CREATE TABLE appointments (
    id integer GENERATED ALWAYS AS IDENTITY,
    appointment_code character varying(30) NOT NULL,
    patient_id integer NOT NULL,
    booked_by integer,
    slot_id integer,
    doctor_id integer,
    department_id integer,
    room_id integer,
    status_id integer NOT NULL,
    booking_channel character varying(15) NOT NULL DEFAULT 'WEB',
    visit_type character varying(15) NOT NULL DEFAULT 'SCHEDULED',
    priority_level integer NOT NULL DEFAULT 0,
    slot_position integer,
    overtaken_count integer NOT NULL DEFAULT 0,
    queue_number character varying(10),
    reason character varying(255),
    has_health_insurance boolean NOT NULL DEFAULT FALSE,
    has_private_insurance boolean NOT NULL DEFAULT FALSE,
    hold_until timestamp without time zone,
    checked_in_at timestamp without time zone,
    called_at timestamp without time zone,
    parent_appointment_id integer,
    created_at timestamp without time zone NOT NULL DEFAULT (now()),
    updated_at timestamp without time zone NOT NULL DEFAULT (now()),
    CONSTRAINT pk_appointments PRIMARY KEY (id),
    CONSTRAINT ck_appointments_booking_channel CHECK (booking_channel IN ('WEB','ON_SITE')),
    CONSTRAINT ck_appointments_overtaken_count CHECK (overtaken_count BETWEEN 0 AND 3),
    CONSTRAINT ck_appointments_priority_level CHECK (priority_level BETWEEN 0 AND 2),
    CONSTRAINT ck_appointments_slot_position CHECK (slot_position >= 1),
    CONSTRAINT ck_appointments_visit_type CHECK (visit_type IN ('SCHEDULED','WALK_IN')),
    CONSTRAINT fk_appointments_accounts_booked_by FOREIGN KEY (booked_by) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT fk_appointments_appointment_slots_slot_id FOREIGN KEY (slot_id) REFERENCES appointment_slots (id) ON DELETE RESTRICT,
    CONSTRAINT fk_appointments_appointment_statuses_status_id FOREIGN KEY (status_id) REFERENCES appointment_statuses (id) ON DELETE RESTRICT,
    CONSTRAINT fk_appointments_appointments_parent_appointment_id FOREIGN KEY (parent_appointment_id) REFERENCES appointments (id) ON DELETE RESTRICT,
    CONSTRAINT fk_appointments_clinic_rooms_room_id FOREIGN KEY (room_id) REFERENCES clinic_rooms (id) ON DELETE RESTRICT,
    CONSTRAINT fk_appointments_departments_department_id FOREIGN KEY (department_id) REFERENCES departments (id) ON DELETE RESTRICT,
    CONSTRAINT fk_appointments_doctors_doctor_id FOREIGN KEY (doctor_id) REFERENCES doctors (id) ON DELETE RESTRICT,
    CONSTRAINT fk_appointments_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE RESTRICT
);

CREATE TABLE appointment_status_history (
    id integer GENERATED ALWAYS AS IDENTITY,
    appointment_id integer NOT NULL,
    old_status_id integer,
    new_status_id integer NOT NULL,
    performed_by integer,
    changed_at timestamp without time zone NOT NULL DEFAULT (now()),
    is_late_cancel boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT pk_appointment_status_history PRIMARY KEY (id),
    CONSTRAINT fk_appointment_status_history_accounts_performed_by FOREIGN KEY (performed_by) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT fk_appointment_status_history_appointment_statuses_new_status_ FOREIGN KEY (new_status_id) REFERENCES appointment_statuses (id) ON DELETE RESTRICT,
    CONSTRAINT fk_appointment_status_history_appointment_statuses_old_status_ FOREIGN KEY (old_status_id) REFERENCES appointment_statuses (id) ON DELETE RESTRICT,
    CONSTRAINT fk_appointment_status_history_appointments_appointment_id FOREIGN KEY (appointment_id) REFERENCES appointments (id) ON DELETE RESTRICT
);

CREATE TABLE invoices (
    id integer GENERATED ALWAYS AS IDENTITY,
    appointment_id integer NOT NULL,
    patient_id integer NOT NULL,
    insurance_plan_id integer,
    insurance_card_id integer,
    coverage_pct_applied numeric(5,2),
    total_amount numeric(12,2) NOT NULL DEFAULT 0.0,
    insurance_covered_amount numeric(12,2) NOT NULL DEFAULT 0.0,
    patient_amount numeric(12,2) NOT NULL DEFAULT 0.0,
    status character varying(20) NOT NULL DEFAULT 'UNPAID',
    created_at timestamp without time zone NOT NULL DEFAULT (now()),
    CONSTRAINT pk_invoices PRIMARY KEY (id),
    CONSTRAINT ck_invoices_coverage_pct_applied CHECK (coverage_pct_applied IS NULL OR coverage_pct_applied BETWEEN 0 AND 100),
    CONSTRAINT ck_invoices_status CHECK (status IN ('UNPAID','PARTIALLY_PAID','PAID')),
    CONSTRAINT fk_invoices_appointments_appointment_id FOREIGN KEY (appointment_id) REFERENCES appointments (id) ON DELETE RESTRICT,
    CONSTRAINT fk_invoices_insurance_cards_insurance_card_id FOREIGN KEY (insurance_card_id) REFERENCES insurance_cards (id) ON DELETE RESTRICT,
    CONSTRAINT fk_invoices_insurance_plans_insurance_plan_id FOREIGN KEY (insurance_plan_id) REFERENCES insurance_plans (id) ON DELETE RESTRICT,
    CONSTRAINT fk_invoices_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE RESTRICT
);

CREATE TABLE medical_records (
    id integer GENERATED ALWAYS AS IDENTITY,
    appointment_id integer NOT NULL,
    doctor_id integer NOT NULL,
    symptoms text,
    clinical_examination text,
    primary_diagnosis_code character varying(20),
    primary_diagnosis_desc character varying(255),
    secondary_diagnosis text,
    conclusion text,
    created_at timestamp without time zone NOT NULL DEFAULT (now()),
    updated_at timestamp without time zone NOT NULL DEFAULT (now()),
    CONSTRAINT pk_medical_records PRIMARY KEY (id),
    CONSTRAINT fk_medical_records_appointments_appointment_id FOREIGN KEY (appointment_id) REFERENCES appointments (id) ON DELETE RESTRICT,
    CONSTRAINT fk_medical_records_doctors_doctor_id FOREIGN KEY (doctor_id) REFERENCES doctors (id) ON DELETE RESTRICT
);

CREATE TABLE notifications (
    id integer GENERATED ALWAYS AS IDENTITY,
    account_id integer NOT NULL,
    appointment_id integer,
    title character varying(150) NOT NULL,
    content text,
    is_read boolean NOT NULL DEFAULT FALSE,
    created_at timestamp without time zone NOT NULL DEFAULT (now()),
    CONSTRAINT pk_notifications PRIMARY KEY (id),
    CONSTRAINT fk_notifications_accounts_account_id FOREIGN KEY (account_id) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT fk_notifications_appointments_appointment_id FOREIGN KEY (appointment_id) REFERENCES appointments (id) ON DELETE RESTRICT
);

CREATE TABLE vital_signs (
    id integer GENERATED ALWAYS AS IDENTITY,
    appointment_id integer NOT NULL,
    pulse integer,
    systolic_bp integer,
    diastolic_bp integer,
    temperature numeric(4,1),
    respiratory_rate integer,
    spo2 integer,
    weight numeric(5,2),
    height numeric(5,2),
    recorded_by integer NOT NULL,
    recorded_at timestamp without time zone NOT NULL DEFAULT (now()),
    CONSTRAINT pk_vital_signs PRIMARY KEY (id),
    CONSTRAINT fk_vital_signs_appointments_appointment_id FOREIGN KEY (appointment_id) REFERENCES appointments (id) ON DELETE RESTRICT,
    CONSTRAINT fk_vital_signs_nurses_recorded_by FOREIGN KEY (recorded_by) REFERENCES nurses (id) ON DELETE RESTRICT
);

CREATE TABLE invoice_items (
    id integer GENERATED ALWAYS AS IDENTITY,
    invoice_id integer NOT NULL,
    category character varying(20) NOT NULL,
    description character varying(255) NOT NULL,
    amount numeric(12,2) NOT NULL,
    CONSTRAINT pk_invoice_items PRIMARY KEY (id),
    CONSTRAINT ck_invoice_items_category CHECK (category IN ('EXAMINATION','MEDICATION','SERVICE')),
    CONSTRAINT fk_invoice_items_invoices_invoice_id FOREIGN KEY (invoice_id) REFERENCES invoices (id) ON DELETE RESTRICT
);

CREATE TABLE payments (
    id integer GENERATED ALWAYS AS IDENTITY,
    invoice_id integer NOT NULL,
    amount numeric(12,2) NOT NULL,
    payment_type character varying(15) NOT NULL DEFAULT 'PAYMENT',
    payment_method character varying(20) NOT NULL DEFAULT 'CASH',
    cashier_id integer,
    receipt_email character varying(100),
    note character varying(255),
    paid_at timestamp without time zone NOT NULL DEFAULT (now()),
    CONSTRAINT pk_payments PRIMARY KEY (id),
    CONSTRAINT ck_payments_amount CHECK (amount > 0),
    CONSTRAINT ck_payments_payment_method CHECK (payment_method IN ('CASH','BANK_TRANSFER')),
    CONSTRAINT ck_payments_payment_type CHECK (payment_type IN ('DEPOSIT','PAYMENT','REFUND')),
    CONSTRAINT fk_payments_accounts_cashier_id FOREIGN KEY (cashier_id) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT fk_payments_invoices_invoice_id FOREIGN KEY (invoice_id) REFERENCES invoices (id) ON DELETE RESTRICT
);

CREATE TABLE access_logs (
    id integer GENERATED ALWAYS AS IDENTITY,
    account_id integer NOT NULL,
    patient_id integer,
    medical_record_id integer,
    action character varying(50) NOT NULL,
    occurred_at timestamp without time zone NOT NULL DEFAULT (now()),
    ip_address character varying(45),
    CONSTRAINT pk_access_logs PRIMARY KEY (id),
    CONSTRAINT fk_access_logs_accounts_account_id FOREIGN KEY (account_id) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT fk_access_logs_medical_records_medical_record_id FOREIGN KEY (medical_record_id) REFERENCES medical_records (id) ON DELETE RESTRICT,
    CONSTRAINT fk_access_logs_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id) ON DELETE RESTRICT
);

CREATE TABLE lab_orders (
    id integer GENERATED ALWAYS AS IDENTITY,
    medical_record_id integer NOT NULL,
    service_id integer NOT NULL,
    note character varying(255),
    status character varying(20) NOT NULL DEFAULT 'PENDING',
    created_at timestamp without time zone NOT NULL DEFAULT (now()),
    CONSTRAINT pk_lab_orders PRIMARY KEY (id),
    CONSTRAINT ck_lab_orders_status CHECK (status IN ('PENDING','COMPLETED')),
    CONSTRAINT fk_lab_orders_medical_records_medical_record_id FOREIGN KEY (medical_record_id) REFERENCES medical_records (id) ON DELETE RESTRICT,
    CONSTRAINT fk_lab_orders_services_service_id FOREIGN KEY (service_id) REFERENCES services (id) ON DELETE RESTRICT
);

CREATE TABLE prescriptions (
    id integer GENERATED ALWAYS AS IDENTITY,
    medical_record_id integer NOT NULL,
    prescribed_date date NOT NULL DEFAULT (CURRENT_DATE),
    note character varying(255),
    CONSTRAINT pk_prescriptions PRIMARY KEY (id),
    CONSTRAINT fk_prescriptions_medical_records_medical_record_id FOREIGN KEY (medical_record_id) REFERENCES medical_records (id) ON DELETE RESTRICT
);

CREATE TABLE lab_results (
    id integer GENERATED ALWAYS AS IDENTITY,
    order_id integer NOT NULL,
    result text,
    performed_by integer,
    recorded_at timestamp without time zone NOT NULL DEFAULT (now()),
    CONSTRAINT pk_lab_results PRIMARY KEY (id),
    CONSTRAINT fk_lab_results_accounts_performed_by FOREIGN KEY (performed_by) REFERENCES accounts (id) ON DELETE RESTRICT,
    CONSTRAINT fk_lab_results_lab_orders_order_id FOREIGN KEY (order_id) REFERENCES lab_orders (id) ON DELETE RESTRICT
);

CREATE TABLE prescription_items (
    id integer GENERATED ALWAYS AS IDENTITY,
    prescription_id integer NOT NULL,
    medication_id integer NOT NULL,
    dosage character varying(100),
    quantity integer NOT NULL,
    duration_days integer,
    usage_instructions character varying(255),
    CONSTRAINT pk_prescription_items PRIMARY KEY (id),
    CONSTRAINT fk_prescription_items_medications_medication_id FOREIGN KEY (medication_id) REFERENCES medications (id) ON DELETE RESTRICT,
    CONSTRAINT fk_prescription_items_prescriptions_prescription_id FOREIGN KEY (prescription_id) REFERENCES prescriptions (id) ON DELETE RESTRICT
);

INSERT INTO appointment_statuses (id, code, name, sort_order)
VALUES (1, 'PENDING', 'Chờ xác nhận', 1);
INSERT INTO appointment_statuses (id, code, name, sort_order)
VALUES (2, 'CONFIRMED', 'Đã xác nhận', 2);
INSERT INTO appointment_statuses (id, code, name, sort_order)
VALUES (3, 'CHECKED_IN', 'Đã check-in', 3);
INSERT INTO appointment_statuses (id, code, name, sort_order)
VALUES (4, 'READY_FOR_DOCTOR', 'Chờ bác sĩ', 4);
INSERT INTO appointment_statuses (id, code, name, sort_order)
VALUES (5, 'IN_PROGRESS', 'Đang khám', 5);

INSERT INTO appointment_statuses (id, code, is_terminal, name, sort_order)
VALUES (6, 'COMPLETED', TRUE, 'Hoàn thành', 6);
INSERT INTO appointment_statuses (id, code, is_terminal, name, sort_order)
VALUES (7, 'CANCELLED', TRUE, 'Đã hủy', 7);
INSERT INTO appointment_statuses (id, code, is_terminal, name, sort_order)
VALUES (8, 'NO_SHOW', TRUE, 'Không đến', 8);
INSERT INTO appointment_statuses (id, code, is_terminal, name, sort_order)
VALUES (9, 'EXPIRED', TRUE, 'Hết hạn giữ chỗ', 9);

INSERT INTO appointment_statuses (id, code, name, sort_order)
VALUES (10, 'RESCHEDULE_REQUIRED', 'Cần đặt lại lịch', 10);

INSERT INTO roles (id, code, description, name)
VALUES (1, 'ADMIN', NULL, 'Quản trị hệ thống');
INSERT INTO roles (id, code, description, name)
VALUES (2, 'DOCTOR', NULL, 'Bác sĩ');
INSERT INTO roles (id, code, description, name)
VALUES (3, 'NURSE', NULL, 'Y tá / Điều dưỡng');
INSERT INTO roles (id, code, description, name)
VALUES (4, 'RECEPTIONIST', NULL, 'Lễ tân / Tiếp đón');
INSERT INTO roles (id, code, description, name)
VALUES (5, 'CASHIER', NULL, 'Thu ngân');
INSERT INTO roles (id, code, description, name)
VALUES (6, 'DEPT_HEAD', NULL, 'Trưởng khoa');
INSERT INTO roles (id, code, description, name)
VALUES (7, 'PATIENT', NULL, 'Bệnh nhân');

INSERT INTO service_types (id, code, name)
VALUES (1, 'EXAMINATION', 'Khám bệnh');
INSERT INTO service_types (id, code, name)
VALUES (2, 'LAB_TEST', 'Xét nghiệm');
INSERT INTO service_types (id, code, name)
VALUES (3, 'IMAGING', 'Chẩn đoán hình ảnh');
INSERT INTO service_types (id, code, name)
VALUES (4, 'FUNCTIONAL', 'Thăm dò chức năng');

CREATE INDEX idx_access_logs_account ON access_logs (account_id);

CREATE INDEX idx_access_logs_patient ON access_logs (patient_id);

CREATE INDEX ix_access_logs_medical_record_id ON access_logs (medical_record_id);

CREATE INDEX idx_link_patient ON account_patient_profiles (patient_id, status);

CREATE INDEX ix_account_patient_profiles_created_by ON account_patient_profiles (created_by);

CREATE INDEX ix_account_patient_profiles_verified_by ON account_patient_profiles (verified_by);

CREATE UNIQUE INDEX uq_link_self_account ON account_patient_profiles (account_id) WHERE relationship = 'SELF' AND status <> 'REVOKED';

CREATE UNIQUE INDEX uq_link_self_patient ON account_patient_profiles (patient_id) WHERE relationship = 'SELF' AND status = 'ACTIVE';

CREATE UNIQUE INDEX ix_accounts_email ON accounts (email);

CREATE INDEX ix_accounts_role_id ON accounts (role_id);

CREATE UNIQUE INDEX ix_accounts_username ON accounts (username);

CREATE INDEX ix_appointment_slots_room_id ON appointment_slots (room_id);

CREATE UNIQUE INDEX uq_slot ON appointment_slots (doctor_id, start_time);

CREATE INDEX idx_history_cancel ON appointment_status_history (performed_by, changed_at) WHERE is_late_cancel;

CREATE INDEX ix_appointment_status_history_appointment_id ON appointment_status_history (appointment_id);

CREATE INDEX ix_appointment_status_history_new_status_id ON appointment_status_history (new_status_id);

CREATE INDEX ix_appointment_status_history_old_status_id ON appointment_status_history (old_status_id);

CREATE UNIQUE INDEX ix_appointment_statuses_code ON appointment_statuses (code);

CREATE INDEX idx_appointments_booked_by ON appointments (booked_by, status_id);

CREATE INDEX idx_appointments_hold ON appointments (hold_until) WHERE status_id = 1;

CREATE INDEX idx_appointments_patient ON appointments (patient_id);

CREATE INDEX idx_appointments_slot ON appointments (slot_id);

CREATE INDEX idx_appointments_status_id ON appointments (status_id);

CREATE UNIQUE INDEX ix_appointments_appointment_code ON appointments (appointment_code);

CREATE INDEX ix_appointments_department_id ON appointments (department_id);

CREATE INDEX ix_appointments_doctor_id ON appointments (doctor_id);

CREATE INDEX ix_appointments_parent_appointment_id ON appointments (parent_appointment_id);

CREATE INDEX ix_appointments_room_id ON appointments (room_id);

CREATE UNIQUE INDEX uq_patient_slot_active ON appointments (patient_id, slot_id) WHERE slot_id IS NOT NULL AND status_id IN (1,2,3,4,5);

CREATE UNIQUE INDEX uq_slot_position ON appointments (slot_id, slot_position) WHERE slot_position IS NOT NULL AND status_id IN (1,2,3,4,5);

CREATE INDEX ix_clinic_rooms_department_id ON clinic_rooms (department_id);

CREATE UNIQUE INDEX ix_department_heads_account_id ON department_heads (account_id);

CREATE UNIQUE INDEX ix_department_heads_department_id ON department_heads (department_id);

CREATE INDEX ix_doctor_schedules_doctor_id ON doctor_schedules (doctor_id);

CREATE INDEX ix_doctor_schedules_room_id ON doctor_schedules (room_id);

CREATE UNIQUE INDEX ix_doctors_account_id ON doctors (account_id);

CREATE INDEX ix_doctors_department_id ON doctors (department_id);

CREATE INDEX idx_duty_shifts_account ON duty_shifts (account_id, start_time);

CREATE INDEX ix_duty_shifts_assigned_by ON duty_shifts (assigned_by);

CREATE INDEX ix_duty_shifts_department_id ON duty_shifts (department_id);

CREATE INDEX idx_insurance_cards_patient ON insurance_cards (patient_id, valid_to);

CREATE UNIQUE INDEX ix_insurance_cards_card_number ON insurance_cards (card_number);

CREATE INDEX ix_insurance_cards_insurance_plan_id ON insurance_cards (insurance_plan_id);

CREATE INDEX ix_invoice_items_invoice_id ON invoice_items (invoice_id);

CREATE UNIQUE INDEX ix_invoices_appointment_id ON invoices (appointment_id);

CREATE INDEX ix_invoices_insurance_card_id ON invoices (insurance_card_id);

CREATE INDEX ix_invoices_insurance_plan_id ON invoices (insurance_plan_id);

CREATE INDEX ix_invoices_patient_id ON invoices (patient_id);

CREATE INDEX ix_lab_orders_medical_record_id ON lab_orders (medical_record_id);

CREATE INDEX ix_lab_orders_service_id ON lab_orders (service_id);

CREATE UNIQUE INDEX ix_lab_results_order_id ON lab_results (order_id);

CREATE INDEX ix_lab_results_performed_by ON lab_results (performed_by);

CREATE UNIQUE INDEX idx_records_appointment ON medical_records (appointment_id);

CREATE INDEX ix_medical_records_doctor_id ON medical_records (doctor_id);

CREATE INDEX idx_notifications_account ON notifications (account_id, is_read);

CREATE INDEX ix_notifications_appointment_id ON notifications (appointment_id);

CREATE UNIQUE INDEX ix_nurses_account_id ON nurses (account_id);

CREATE INDEX ix_nurses_department_id ON nurses (department_id);

CREATE UNIQUE INDEX ix_patients_national_id ON patients (national_id);

CREATE INDEX idx_payments_invoice ON payments (invoice_id);

CREATE INDEX ix_payments_cashier_id ON payments (cashier_id);

CREATE INDEX ix_prescription_items_medication_id ON prescription_items (medication_id);

CREATE INDEX ix_prescription_items_prescription_id ON prescription_items (prescription_id);

CREATE INDEX ix_prescriptions_medical_record_id ON prescriptions (medical_record_id);

CREATE UNIQUE INDEX ix_roles_code ON roles (code);

CREATE INDEX ix_schedule_exceptions_approved_by ON schedule_exceptions (approved_by);

CREATE INDEX ix_schedule_exceptions_doctor_id ON schedule_exceptions (doctor_id);

CREATE INDEX ix_schedule_exceptions_replacement_doctor_id ON schedule_exceptions (replacement_doctor_id);

CREATE UNIQUE INDEX ix_service_types_code ON service_types (code);

CREATE INDEX ix_services_service_type_id ON services (service_type_id);

CREATE UNIQUE INDEX ix_vital_signs_appointment_id ON vital_signs (appointment_id);

CREATE INDEX ix_vital_signs_recorded_by ON vital_signs (recorded_by);

INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20261015010838_InitialCreate', '10.0.13');
INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20261103165420_AlignToGroupSpec', '10.0.13');
INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20261103173448_SanityCheck', '10.0.13');
INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20261104144910_QueuePositionNumbering', '10.0.13');
INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20261105131631_AppointmentInsuranceDeclaration', '10.0.13');
INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20261105141229_PatientEmail', '10.0.13');

COMMIT;

