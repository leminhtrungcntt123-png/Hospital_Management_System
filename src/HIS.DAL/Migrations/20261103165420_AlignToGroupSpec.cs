using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HIS.DAL.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Toàn bộ thân Up()/Down() viết bằng SQL thủ công, KHÔNG dùng SQL EF sinh ra:
    /// CSDL hospital_his không được tạo bởi InitialCreate nên diff của EF sai lệch lớn.
    /// Danh mục rename/add/check/index lấy từ model qua backup/align_plan.js (xem backup/sql/diff_report.txt),
    /// các bước remap id và di trú dữ liệu viết tay ở dưới.
    /// </summary>
    public partial class AlignToGroupSpec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Chuẩn hoá tên đối tượng (PK/FK/UNIQUE->INDEX/CHECK) về đúng tên model khai báo
            migrationBuilder.Sql(@"ALTER TABLE access_logs RENAME CONSTRAINT access_logs_pkey TO pk_access_logs;
                ALTER TABLE access_logs RENAME CONSTRAINT access_logs_account_id_fkey TO fk_access_logs_accounts_account_id;
                ALTER TABLE access_logs RENAME CONSTRAINT access_logs_medical_record_id_fkey TO fk_access_logs_medical_records_medical_record_id;
                ALTER TABLE accounts RENAME CONSTRAINT accounts_pkey TO pk_accounts;
                ALTER TABLE accounts RENAME CONSTRAINT accounts_role_id_fkey TO fk_accounts_roles_role_id;
                ALTER TABLE appointments RENAME CONSTRAINT appointments_pkey TO pk_appointments;
                ALTER TABLE appointments RENAME CONSTRAINT appointments_parent_appointment_id_fkey TO fk_appointments_appointments_parent_appointment_id;
                ALTER TABLE appointments RENAME CONSTRAINT appointments_patient_id_fkey TO fk_appointments_patients_patient_id;
                ALTER TABLE appointments RENAME CONSTRAINT appointments_room_id_fkey TO fk_appointments_clinic_rooms_room_id;
                ALTER TABLE appointments RENAME CONSTRAINT appointments_slot_id_fkey TO fk_appointments_appointment_slots_slot_id;
                ALTER TABLE appointments RENAME CONSTRAINT appointments_status_id_fkey TO fk_appointments_appointment_statuses_status_id;
                ALTER TABLE appointments RENAME CONSTRAINT appointments_booking_channel_check TO ck_appointments_booking_channel;
                ALTER TABLE appointments RENAME CONSTRAINT appointments_priority_level_check TO ck_appointments_priority_level;
                ALTER TABLE appointments RENAME CONSTRAINT appointments_visit_type_check TO ck_appointments_visit_type;
                ALTER TABLE appointment_slots RENAME CONSTRAINT appointment_slots_pkey TO pk_appointment_slots;
                ALTER TABLE appointment_slots RENAME CONSTRAINT appointment_slots_doctor_id_fkey TO fk_appointment_slots_doctors_doctor_id;
                ALTER TABLE appointment_slots RENAME CONSTRAINT appointment_slots_room_id_fkey TO fk_appointment_slots_clinic_rooms_room_id;
                ALTER TABLE appointment_slots RENAME CONSTRAINT ck_capacity TO ck_appointment_slots_capacity;
                ALTER TABLE appointment_slots RENAME CONSTRAINT appointment_slots_status_check TO ck_appointment_slots_status;
                ALTER TABLE appointment_statuses RENAME CONSTRAINT appointment_statuses_pkey TO pk_appointment_statuses;
                ALTER TABLE appointment_status_history RENAME CONSTRAINT appointment_status_history_pkey TO pk_appointment_status_history;
                ALTER TABLE appointment_status_history RENAME CONSTRAINT appointment_status_history_appointment_id_fkey TO fk_appointment_status_history_appointments_appointment_id;
                ALTER TABLE appointment_status_history RENAME CONSTRAINT appointment_status_history_new_status_id_fkey TO fk_appointment_status_history_appointment_statuses_new_status_;
                ALTER TABLE appointment_status_history RENAME CONSTRAINT appointment_status_history_old_status_id_fkey TO fk_appointment_status_history_appointment_statuses_old_status_;
                ALTER TABLE appointment_status_history RENAME CONSTRAINT appointment_status_history_performed_by_fkey TO fk_appointment_status_history_accounts_performed_by;
                ALTER TABLE clinic_rooms RENAME CONSTRAINT clinic_rooms_pkey TO pk_clinic_rooms;
                ALTER TABLE clinic_rooms RENAME CONSTRAINT clinic_rooms_department_id_fkey TO fk_clinic_rooms_departments_department_id;
                ALTER TABLE departments RENAME CONSTRAINT departments_pkey TO pk_departments;
                ALTER TABLE department_heads RENAME CONSTRAINT department_heads_pkey TO pk_department_heads;
                ALTER TABLE department_heads RENAME CONSTRAINT department_heads_account_id_fkey TO fk_department_heads_accounts_account_id;
                ALTER TABLE department_heads RENAME CONSTRAINT department_heads_department_id_fkey TO fk_department_heads_departments_department_id;
                ALTER TABLE doctors RENAME CONSTRAINT doctors_pkey TO pk_doctors;
                ALTER TABLE doctors RENAME CONSTRAINT doctors_account_id_fkey TO fk_doctors_accounts_account_id;
                ALTER TABLE doctors RENAME CONSTRAINT doctors_department_id_fkey TO fk_doctors_departments_department_id;
                ALTER TABLE doctor_schedules RENAME CONSTRAINT doctor_schedules_pkey TO pk_doctor_schedules;
                ALTER TABLE doctor_schedules RENAME CONSTRAINT doctor_schedules_doctor_id_fkey TO fk_doctor_schedules_doctors_doctor_id;
                ALTER TABLE doctor_schedules RENAME CONSTRAINT doctor_schedules_room_id_fkey TO fk_doctor_schedules_clinic_rooms_room_id;
                ALTER TABLE doctor_schedules RENAME CONSTRAINT doctor_schedules_day_of_week_check TO ck_doctor_schedules_day_of_week;
                ALTER TABLE duty_shifts RENAME CONSTRAINT duty_shifts_pkey TO pk_duty_shifts;
                ALTER TABLE duty_shifts RENAME CONSTRAINT duty_shifts_account_id_fkey TO fk_duty_shifts_accounts_account_id;
                ALTER TABLE duty_shifts RENAME CONSTRAINT duty_shifts_assigned_by_fkey TO fk_duty_shifts_accounts_assigned_by;
                ALTER TABLE duty_shifts RENAME CONSTRAINT duty_shifts_department_id_fkey TO fk_duty_shifts_departments_department_id;
                ALTER TABLE insurance_cards RENAME CONSTRAINT insurance_cards_pkey TO pk_insurance_cards;
                ALTER TABLE insurance_cards RENAME CONSTRAINT insurance_cards_insurance_plan_id_fkey TO fk_insurance_cards_insurance_plans_insurance_plan_id;
                ALTER TABLE insurance_cards RENAME CONSTRAINT insurance_cards_patient_id_fkey TO fk_insurance_cards_patients_patient_id;
                ALTER TABLE insurance_plans RENAME CONSTRAINT insurance_plans_pkey TO pk_insurance_plans;
                ALTER TABLE insurance_plans RENAME CONSTRAINT insurance_plans_referral_type_check TO ck_insurance_plans_referral_type;
                ALTER TABLE invoices RENAME CONSTRAINT invoices_pkey TO pk_invoices;
                ALTER TABLE invoices RENAME CONSTRAINT invoices_appointment_id_fkey TO fk_invoices_appointments_appointment_id;
                ALTER TABLE invoices RENAME CONSTRAINT invoices_insurance_card_id_fkey TO fk_invoices_insurance_cards_insurance_card_id;
                ALTER TABLE invoices RENAME CONSTRAINT invoices_insurance_plan_id_fkey TO fk_invoices_insurance_plans_insurance_plan_id;
                ALTER TABLE invoices RENAME CONSTRAINT invoices_patient_id_fkey TO fk_invoices_patients_patient_id;
                ALTER TABLE invoices RENAME CONSTRAINT invoices_status_check TO ck_invoices_status;
                ALTER TABLE invoice_items RENAME CONSTRAINT invoice_items_pkey TO pk_invoice_items;
                ALTER TABLE invoice_items RENAME CONSTRAINT invoice_items_invoice_id_fkey TO fk_invoice_items_invoices_invoice_id;
                ALTER TABLE invoice_items RENAME CONSTRAINT invoice_items_category_check TO ck_invoice_items_category;
                ALTER TABLE lab_orders RENAME CONSTRAINT lab_orders_pkey TO pk_lab_orders;
                ALTER TABLE lab_orders RENAME CONSTRAINT lab_orders_medical_record_id_fkey TO fk_lab_orders_medical_records_medical_record_id;
                ALTER TABLE lab_orders RENAME CONSTRAINT lab_orders_service_id_fkey TO fk_lab_orders_services_service_id;
                ALTER TABLE lab_orders RENAME CONSTRAINT lab_orders_status_check TO ck_lab_orders_status;
                ALTER TABLE lab_results RENAME CONSTRAINT lab_results_pkey TO pk_lab_results;
                ALTER TABLE lab_results RENAME CONSTRAINT lab_results_order_id_fkey TO fk_lab_results_lab_orders_order_id;
                ALTER TABLE lab_results RENAME CONSTRAINT lab_results_performed_by_fkey TO fk_lab_results_accounts_performed_by;
                ALTER TABLE medical_records RENAME CONSTRAINT medical_records_pkey TO pk_medical_records;
                ALTER TABLE medical_records RENAME CONSTRAINT medical_records_appointment_id_fkey TO fk_medical_records_appointments_appointment_id;
                ALTER TABLE medical_records RENAME CONSTRAINT medical_records_doctor_id_fkey TO fk_medical_records_doctors_doctor_id;
                ALTER TABLE medications RENAME CONSTRAINT medications_pkey TO pk_medications;
                ALTER TABLE nurses RENAME CONSTRAINT nurses_pkey TO pk_nurses;
                ALTER TABLE nurses RENAME CONSTRAINT nurses_account_id_fkey TO fk_nurses_accounts_account_id;
                ALTER TABLE nurses RENAME CONSTRAINT nurses_department_id_fkey TO fk_nurses_departments_department_id;
                ALTER TABLE patients RENAME CONSTRAINT patients_pkey TO pk_patients;
                ALTER TABLE patients RENAME CONSTRAINT patients_gender_check TO ck_patients_gender;
                ALTER TABLE payments RENAME CONSTRAINT payments_pkey TO pk_payments;
                ALTER TABLE payments RENAME CONSTRAINT payments_cashier_id_fkey TO fk_payments_accounts_cashier_id;
                ALTER TABLE payments RENAME CONSTRAINT payments_invoice_id_fkey TO fk_payments_invoices_invoice_id;
                ALTER TABLE payments RENAME CONSTRAINT payments_amount_check TO ck_payments_amount;
                ALTER TABLE payments RENAME CONSTRAINT payments_payment_method_check TO ck_payments_payment_method;
                ALTER TABLE payments RENAME CONSTRAINT payments_payment_type_check TO ck_payments_payment_type;
                ALTER TABLE prescriptions RENAME CONSTRAINT prescriptions_pkey TO pk_prescriptions;
                ALTER TABLE prescriptions RENAME CONSTRAINT prescriptions_medical_record_id_fkey TO fk_prescriptions_medical_records_medical_record_id;
                ALTER TABLE prescription_items RENAME CONSTRAINT prescription_items_pkey TO pk_prescription_items;
                ALTER TABLE prescription_items RENAME CONSTRAINT prescription_items_medication_id_fkey TO fk_prescription_items_medications_medication_id;
                ALTER TABLE prescription_items RENAME CONSTRAINT prescription_items_prescription_id_fkey TO fk_prescription_items_prescriptions_prescription_id;
                ALTER TABLE roles RENAME CONSTRAINT roles_pkey TO pk_roles;
                ALTER TABLE schedule_exceptions RENAME CONSTRAINT schedule_exceptions_pkey TO pk_schedule_exceptions;
                ALTER TABLE schedule_exceptions RENAME CONSTRAINT schedule_exceptions_approved_by_fkey TO fk_schedule_exceptions_accounts_approved_by;
                ALTER TABLE schedule_exceptions RENAME CONSTRAINT schedule_exceptions_doctor_id_fkey TO fk_schedule_exceptions_doctors_doctor_id;
                ALTER TABLE schedule_exceptions RENAME CONSTRAINT schedule_exceptions_replacement_doctor_id_fkey TO fk_schedule_exceptions_doctors_replacement_doctor_id;
                ALTER TABLE schedule_exceptions RENAME CONSTRAINT schedule_exceptions_type_check TO ck_schedule_exceptions_type;
                ALTER TABLE services RENAME CONSTRAINT services_pkey TO pk_services;
                ALTER TABLE services RENAME CONSTRAINT services_service_type_id_fkey TO fk_services_service_types_service_type_id;
                ALTER TABLE service_types RENAME CONSTRAINT service_types_pkey TO pk_service_types;
                ALTER TABLE vital_signs RENAME CONSTRAINT vital_signs_pkey TO pk_vital_signs;
                ALTER TABLE vital_signs RENAME CONSTRAINT vital_signs_appointment_id_fkey TO fk_vital_signs_appointments_appointment_id;
                ALTER TABLE vital_signs RENAME CONSTRAINT vital_signs_recorded_by_fkey TO fk_vital_signs_nurses_recorded_by;
                ALTER TABLE accounts DROP CONSTRAINT accounts_username_key;
                ALTER TABLE appointments DROP CONSTRAINT appointments_appointment_code_key;
                ALTER TABLE appointment_statuses DROP CONSTRAINT appointment_statuses_code_key;
                ALTER TABLE department_heads DROP CONSTRAINT department_heads_account_id_key;
                ALTER TABLE department_heads DROP CONSTRAINT department_heads_department_id_key;
                ALTER TABLE doctors DROP CONSTRAINT doctors_account_id_key;
                ALTER TABLE insurance_cards DROP CONSTRAINT insurance_cards_card_number_key;
                ALTER TABLE invoices DROP CONSTRAINT invoices_appointment_id_key;
                ALTER TABLE lab_results DROP CONSTRAINT lab_results_order_id_key;
                ALTER TABLE nurses DROP CONSTRAINT nurses_account_id_key;
                ALTER TABLE patients DROP CONSTRAINT patients_national_id_key;
                ALTER TABLE roles DROP CONSTRAINT roles_code_key;
                ALTER TABLE service_types DROP CONSTRAINT service_types_code_key;
                ALTER TABLE vital_signs DROP CONSTRAINT vital_signs_appointment_id_key;");
            // 2. Remap id appointment_statuses theo init.md (id cũ hoán vị với spec)
            migrationBuilder.Sql(@"-- 2.1 FK trỏ vào appointment_statuses phải tạm gỡ vì id sẽ hoán vị
                ALTER TABLE appointments DROP CONSTRAINT fk_appointments_appointment_statuses_status_id;
                ALTER TABLE appointment_status_history DROP CONSTRAINT fk_appointment_status_history_appointment_statuses_old_status_;
                ALTER TABLE appointment_status_history DROP CONSTRAINT fk_appointment_status_history_appointment_statuses_new_status_;
                
                -- 2.2 Bảng tra id cũ -> id mới theo init.md mục 6 (PENDING=1 ... RESCHEDULE_REQUIRED=10)
                CREATE TEMP TABLE _status_remap (old_id integer PRIMARY KEY, new_id integer NOT NULL);
                INSERT INTO _status_remap (old_id, new_id) VALUES (1,1),(2,2),(3,5),(4,6),(5,7),(6,3),(8,8),(9,9);
                
                -- 2.3 Dời toàn bộ id sang vùng 1000+ rồi mới thay bảng danh mục, tránh va khóa chính
                UPDATE appointment_statuses SET id = id + 1000;
                UPDATE appointments SET status_id = status_id + 1000;
                UPDATE appointment_status_history SET new_status_id = new_status_id + 1000, old_status_id = old_status_id + 1000;
                
                DELETE FROM appointment_statuses;
                INSERT INTO appointment_statuses (id, code, name, sort_order, is_terminal) VALUES
                    (1,  'PENDING',             'Chờ xác nhận',      1,  false),
                    (2,  'CONFIRMED',           'Đã xác nhận',       2,  false),
                    (3,  'CHECKED_IN',          'Đã check-in',       3,  false),
                    (4,  'READY_FOR_DOCTOR',    'Chờ bác sĩ',        4,  false),
                    (5,  'IN_PROGRESS',         'Đang khám',         5,  false),
                    (6,  'COMPLETED',           'Hoàn thành',        6,  true),
                    (7,  'CANCELLED',           'Đã hủy',            7,  true),
                    (8,  'NO_SHOW',             'Không đến',         8,  true),
                    (9,  'EXPIRED',             'Hết hạn giữ chỗ',   9,  true),
                    (10, 'RESCHEDULE_REQUIRED', 'Cần đặt lại lịch',  10, false);
                
                UPDATE appointments a SET status_id = r.new_id FROM _status_remap r WHERE a.status_id = r.old_id + 1000;
                UPDATE appointment_status_history h SET old_status_id = r.new_id FROM _status_remap r WHERE h.old_status_id = r.old_id + 1000;
                UPDATE appointment_status_history h SET new_status_id = r.new_id FROM _status_remap r WHERE h.new_status_id = r.old_id + 1000;
                DROP TABLE _status_remap;");
            // 3a. Ba bảng mới
            migrationBuilder.Sql(@"CREATE TABLE account_patient_profiles (
                  account_id integer NOT NULL,
                  patient_id integer NOT NULL,
                  can_view_records boolean NOT NULL DEFAULT false,
                  created_at timestamp without time zone NOT NULL DEFAULT now(),
                  created_by integer,
                  relationship character varying(15) NOT NULL,
                  status character varying(10) NOT NULL DEFAULT 'ACTIVE',
                  verified_at timestamp without time zone,
                  verified_by integer,
                  CONSTRAINT pk_account_patient_profiles PRIMARY KEY (account_id, patient_id)
                );
                
                CREATE TABLE notifications (
                  id integer NOT NULL GENERATED ALWAYS AS IDENTITY,
                  account_id integer NOT NULL,
                  appointment_id integer,
                  content text,
                  created_at timestamp without time zone NOT NULL DEFAULT now(),
                  is_read boolean NOT NULL DEFAULT false,
                  title character varying(150) NOT NULL,
                  CONSTRAINT pk_notifications PRIMARY KEY (id)
                );
                
                CREATE TABLE queue_counters (
                  queue_date date NOT NULL,
                  room_id integer NOT NULL,
                  shift character varying(10) NOT NULL,
                  channel character varying(15) NOT NULL,
                  last_number integer NOT NULL DEFAULT 0,
                  CONSTRAINT pk_queue_counters PRIMARY KEY (queue_date, room_id, shift, channel)
                );");
            // 3b. Cột còn thiếu
            migrationBuilder.Sql(@"ALTER TABLE access_logs ADD COLUMN patient_id integer;
                ALTER TABLE accounts ADD COLUMN email character varying(100);
                ALTER TABLE appointments ADD COLUMN booked_by integer;
                ALTER TABLE appointments ADD COLUMN called_at timestamp without time zone;
                ALTER TABLE appointments ADD COLUMN checked_in_at timestamp without time zone;
                ALTER TABLE appointments ADD COLUMN department_id integer;
                ALTER TABLE appointments ADD COLUMN doctor_id integer;
                ALTER TABLE doctor_schedules ADD COLUMN max_patients integer DEFAULT 40;
                ALTER TABLE doctor_schedules ADD COLUMN online_slots integer DEFAULT 28;
                ALTER TABLE invoices ADD COLUMN coverage_pct_applied numeric(5,2);
                ALTER TABLE payments ADD COLUMN receipt_email character varying(100);
                ALTER TABLE schedule_exceptions ADD COLUMN status character varying(10) DEFAULT 'PENDING';");
            // 3c. Siết kiểu / NOT NULL / DEFAULT cho cột đã có
            migrationBuilder.Sql(@"ALTER TABLE doctor_schedules ALTER COLUMN day_of_week SET NOT NULL;
                ALTER TABLE payments ALTER COLUMN payment_method SET DEFAULT 'CASH';");
            // 4a. Bỏ CHECK cũ cho phép ca tối
            migrationBuilder.Sql(@"ALTER TABLE doctor_schedules DROP CONSTRAINT doctor_schedules_shift_check;
                ALTER TABLE schedule_exceptions DROP CONSTRAINT schedule_exceptions_shift_check;");
            // 4b. CHECK theo spec (ck_<bang>_<cot>)
            migrationBuilder.Sql(@"ALTER TABLE accounts ADD CONSTRAINT ck_accounts_email_lower CHECK (email IS NULL OR email = lower(email));
                ALTER TABLE account_patient_profiles ADD CONSTRAINT ck_account_patient_profiles_relationship CHECK (relationship IN ('SELF','SPOUSE','CHILD','PARENT','SIBLING','OTHER'));
                ALTER TABLE account_patient_profiles ADD CONSTRAINT ck_account_patient_profiles_status CHECK (status IN ('ACTIVE','PENDING','REVOKED'));
                ALTER TABLE account_patient_profiles ADD CONSTRAINT ck_link_adult_records_consent CHECK (can_view_records = FALSE OR relationship IN ('SELF','CHILD') OR verified_by IS NOT NULL);
                ALTER TABLE account_patient_profiles ADD CONSTRAINT ck_link_pending_no_records CHECK (status = 'ACTIVE' OR can_view_records = FALSE);
                ALTER TABLE account_patient_profiles ADD CONSTRAINT ck_link_self_not_pending CHECK (NOT (relationship = 'SELF' AND status = 'PENDING'));
                ALTER TABLE doctor_schedules ADD CONSTRAINT ck_doctor_schedules_shift CHECK (shift IN ('MORNING','AFTERNOON'));
                ALTER TABLE doctor_schedules ADD CONSTRAINT ck_online_slots CHECK (online_slots BETWEEN 0 AND max_patients);
                ALTER TABLE invoices ADD CONSTRAINT ck_invoices_coverage_pct_applied CHECK (coverage_pct_applied IS NULL OR coverage_pct_applied BETWEEN 0 AND 100);
                ALTER TABLE queue_counters ADD CONSTRAINT ck_queue_counters_channel CHECK (channel IN ('WEB','ON_SITE'));
                ALTER TABLE queue_counters ADD CONSTRAINT ck_queue_counters_last_number CHECK (last_number >= 0);
                ALTER TABLE queue_counters ADD CONSTRAINT ck_queue_counters_shift CHECK (shift IN ('MORNING','AFTERNOON'));
                ALTER TABLE schedule_exceptions ADD CONSTRAINT ck_exception_approved CHECK (status <> 'APPROVED' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL));
                ALTER TABLE schedule_exceptions ADD CONSTRAINT ck_exception_replacement CHECK (replacement_doctor_id IS NULL OR replacement_doctor_id <> doctor_id);
                ALTER TABLE schedule_exceptions ADD CONSTRAINT ck_schedule_exceptions_shift CHECK (shift IS NULL OR shift IN ('MORNING','AFTERNOON'));
                ALTER TABLE schedule_exceptions ADD CONSTRAINT ck_schedule_exceptions_status CHECK (status IN ('PENDING','APPROVED','REJECTED'));");
            // 5a. FK mới
            migrationBuilder.Sql(@"ALTER TABLE access_logs ADD CONSTRAINT fk_access_logs_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id);
                ALTER TABLE account_patient_profiles ADD CONSTRAINT fk_account_patient_profiles_accounts_account_id FOREIGN KEY (account_id) REFERENCES accounts (id);
                ALTER TABLE account_patient_profiles ADD CONSTRAINT fk_account_patient_profiles_accounts_created_by FOREIGN KEY (created_by) REFERENCES accounts (id);
                ALTER TABLE account_patient_profiles ADD CONSTRAINT fk_account_patient_profiles_patients_patient_id FOREIGN KEY (patient_id) REFERENCES patients (id);
                ALTER TABLE account_patient_profiles ADD CONSTRAINT fk_account_patient_profiles_accounts_verified_by FOREIGN KEY (verified_by) REFERENCES accounts (id);
                ALTER TABLE appointments ADD CONSTRAINT fk_appointments_accounts_booked_by FOREIGN KEY (booked_by) REFERENCES accounts (id);
                ALTER TABLE appointments ADD CONSTRAINT fk_appointments_departments_department_id FOREIGN KEY (department_id) REFERENCES departments (id);
                ALTER TABLE appointments ADD CONSTRAINT fk_appointments_doctors_doctor_id FOREIGN KEY (doctor_id) REFERENCES doctors (id);
                ALTER TABLE notifications ADD CONSTRAINT fk_notifications_accounts_account_id FOREIGN KEY (account_id) REFERENCES accounts (id);
                ALTER TABLE notifications ADD CONSTRAINT fk_notifications_appointments_appointment_id FOREIGN KEY (appointment_id) REFERENCES appointments (id);
                ALTER TABLE queue_counters ADD CONSTRAINT fk_queue_counters_clinic_rooms_room_id FOREIGN KEY (room_id) REFERENCES clinic_rooms (id);");
            // 5b. Tạo lại 3 FK trạng thái sau remap
            migrationBuilder.Sql(@"-- 5.2 Tạo lại 3 FK trạng thái sau khi id đã về đúng spec
                ALTER TABLE appointments ADD CONSTRAINT fk_appointments_appointment_statuses_status_id
                    FOREIGN KEY (status_id) REFERENCES appointment_statuses (id);
                ALTER TABLE appointment_status_history ADD CONSTRAINT fk_appointment_status_history_appointment_statuses_old_status_
                    FOREIGN KEY (old_status_id) REFERENCES appointment_statuses (id);
                ALTER TABLE appointment_status_history ADD CONSTRAINT fk_appointment_status_history_appointment_statuses_new_status_
                    FOREIGN KEY (new_status_id) REFERENCES appointment_statuses (id);");
            // 6. Xóa đối tượng thừa rồi di trú dữ liệu hồ sơ
            migrationBuilder.Sql(@"DROP INDEX idx_slots_doctor_date;
                ALTER TABLE medical_records DROP CONSTRAINT medical_records_appointment_id_key;
                ALTER TABLE patients DROP CONSTRAINT patients_account_id_fkey;
                ALTER TABLE patients DROP CONSTRAINT patients_account_id_key;
                
                -- 6.1 patients.account_id sắp bị xóa: mỗi hồ sơ thành một liên kết SELF đã duyệt (cách A)
                INSERT INTO account_patient_profiles
                    (account_id, patient_id, relationship, status, can_view_records, created_by, verified_by, verified_at, created_at)
                SELECT p.account_id, p.id, 'SELF', 'ACTIVE', TRUE, 1, 1, p.created_at, p.created_at
                FROM patients p
                WHERE p.account_id <> 1;
                
                -- Tài khoản ADMIN (id 1) đang giữ hồ sơ thử nghiệm: ghi liên kết OTHER, không tự cấp quyền xem bệnh án
                INSERT INTO account_patient_profiles
                    (account_id, patient_id, relationship, status, can_view_records, created_by, verified_by, verified_at, created_at)
                SELECT 1, p.id, 'OTHER', 'ACTIVE', FALSE, 1, NULL, NULL, p.created_at
                FROM patients p WHERE p.account_id = 1;
                
                -- 6.2 Ai là người đặt lịch: backfill từ chủ hồ sơ cũ
                UPDATE appointments a SET booked_by = p.account_id FROM patients p WHERE a.patient_id = p.id;
                
                -- 6.3 Bác sĩ / phòng / khoa của từng lịch hẹn theo slot đã chọn
                UPDATE appointments a SET doctor_id = s.doctor_id FROM appointment_slots s WHERE a.slot_id = s.id AND a.doctor_id IS NULL;
                UPDATE appointments a SET department_id = r.department_id FROM clinic_rooms r WHERE a.room_id = r.id AND a.department_id IS NULL;
                
                -- 6.4 Ca trực đã duyệt (approved_by có trước khi có cột status) -> APPROVED
                UPDATE schedule_exceptions SET status = 'APPROVED' WHERE approved_by IS NOT NULL;
                
                -- 6.5 Hai tài khoản do quầy tự sinh khi tiếp nhận ẩn danh: giữ liên kết hồ sơ, tắt quyền đăng nhập
                UPDATE accounts SET is_active = FALSE WHERE username LIKE 'bn26%';
                
                -- 6.6 Xóa 2 cột khỏi patients (insurance_card_number đã được cả 5 hồ sơ bỏ trống, thẻ bảo hiểm nằm ở insurance_cards)
                ALTER TABLE patients DROP COLUMN account_id;
                ALTER TABLE patients DROP COLUMN insurance_card_number;");
            // 7. Index (chạy sau remap vì 2 index partial lọc theo status_id)
            migrationBuilder.Sql(@"CREATE INDEX ix_access_logs_medical_record_id ON access_logs (medical_record_id);
                CREATE INDEX idx_access_logs_patient ON access_logs (patient_id);
                CREATE UNIQUE INDEX ix_accounts_email ON accounts (email);
                CREATE INDEX ix_accounts_role_id ON accounts (role_id);
                CREATE UNIQUE INDEX ix_accounts_username ON accounts (username);
                CREATE UNIQUE INDEX uq_link_self_account ON account_patient_profiles (account_id) WHERE relationship = 'SELF' AND status <> 'REVOKED';
                CREATE INDEX ix_account_patient_profiles_created_by ON account_patient_profiles (created_by);
                CREATE UNIQUE INDEX uq_link_self_patient ON account_patient_profiles (patient_id) WHERE relationship = 'SELF' AND status = 'ACTIVE';
                CREATE INDEX ix_account_patient_profiles_verified_by ON account_patient_profiles (verified_by);
                CREATE INDEX idx_link_patient ON account_patient_profiles (patient_id, status);
                CREATE UNIQUE INDEX ix_appointments_appointment_code ON appointments (appointment_code);
                CREATE INDEX ix_appointments_department_id ON appointments (department_id);
                CREATE INDEX ix_appointments_doctor_id ON appointments (doctor_id);
                CREATE INDEX idx_appointments_hold ON appointments (hold_until) WHERE status_id = 1;
                CREATE INDEX ix_appointments_parent_appointment_id ON appointments (parent_appointment_id);
                CREATE INDEX ix_appointments_room_id ON appointments (room_id);
                CREATE INDEX idx_appointments_slot ON appointments (slot_id);
                CREATE INDEX idx_appointments_booked_by ON appointments (booked_by, status_id);
                CREATE UNIQUE INDEX uq_patient_slot_active ON appointments (patient_id, slot_id) WHERE slot_id IS NOT NULL AND status_id IN (1,2,3,4,5);
                CREATE INDEX ix_appointment_slots_room_id ON appointment_slots (room_id);
                CREATE UNIQUE INDEX ix_appointment_statuses_code ON appointment_statuses (code);
                CREATE INDEX ix_appointment_status_history_appointment_id ON appointment_status_history (appointment_id);
                CREATE INDEX ix_appointment_status_history_new_status_id ON appointment_status_history (new_status_id);
                CREATE INDEX ix_appointment_status_history_old_status_id ON appointment_status_history (old_status_id);
                CREATE INDEX ix_appointment_status_history_performed_by ON appointment_status_history (performed_by);
                CREATE INDEX ix_clinic_rooms_department_id ON clinic_rooms (department_id);
                CREATE UNIQUE INDEX ix_department_heads_account_id ON department_heads (account_id);
                CREATE UNIQUE INDEX ix_department_heads_department_id ON department_heads (department_id);
                CREATE UNIQUE INDEX ix_doctors_account_id ON doctors (account_id);
                CREATE INDEX ix_doctors_department_id ON doctors (department_id);
                CREATE INDEX ix_doctor_schedules_doctor_id ON doctor_schedules (doctor_id);
                CREATE INDEX ix_doctor_schedules_room_id ON doctor_schedules (room_id);
                CREATE INDEX ix_duty_shifts_assigned_by ON duty_shifts (assigned_by);
                CREATE INDEX ix_duty_shifts_department_id ON duty_shifts (department_id);
                CREATE UNIQUE INDEX ix_insurance_cards_card_number ON insurance_cards (card_number);
                CREATE INDEX ix_insurance_cards_insurance_plan_id ON insurance_cards (insurance_plan_id);
                CREATE UNIQUE INDEX ix_invoices_appointment_id ON invoices (appointment_id);
                CREATE INDEX ix_invoices_insurance_card_id ON invoices (insurance_card_id);
                CREATE INDEX ix_invoices_insurance_plan_id ON invoices (insurance_plan_id);
                CREATE INDEX ix_invoices_patient_id ON invoices (patient_id);
                CREATE INDEX ix_invoice_items_invoice_id ON invoice_items (invoice_id);
                CREATE INDEX ix_lab_orders_medical_record_id ON lab_orders (medical_record_id);
                CREATE INDEX ix_lab_orders_service_id ON lab_orders (service_id);
                CREATE UNIQUE INDEX ix_lab_results_order_id ON lab_results (order_id);
                CREATE INDEX ix_lab_results_performed_by ON lab_results (performed_by);
                CREATE INDEX ix_medical_records_doctor_id ON medical_records (doctor_id);
                CREATE INDEX ix_notifications_appointment_id ON notifications (appointment_id);
                CREATE INDEX idx_notifications_account ON notifications (account_id, is_read);
                CREATE UNIQUE INDEX ix_nurses_account_id ON nurses (account_id);
                CREATE INDEX ix_nurses_department_id ON nurses (department_id);
                CREATE UNIQUE INDEX ix_patients_national_id ON patients (national_id);
                CREATE INDEX ix_payments_cashier_id ON payments (cashier_id);
                CREATE INDEX ix_prescriptions_medical_record_id ON prescriptions (medical_record_id);
                CREATE INDEX ix_prescription_items_medication_id ON prescription_items (medication_id);
                CREATE INDEX ix_prescription_items_prescription_id ON prescription_items (prescription_id);
                CREATE INDEX ix_queue_counters_room_id ON queue_counters (room_id);
                CREATE UNIQUE INDEX ix_roles_code ON roles (code);
                CREATE INDEX ix_schedule_exceptions_approved_by ON schedule_exceptions (approved_by);
                CREATE INDEX ix_schedule_exceptions_doctor_id ON schedule_exceptions (doctor_id);
                CREATE INDEX ix_schedule_exceptions_replacement_doctor_id ON schedule_exceptions (replacement_doctor_id);
                CREATE INDEX ix_services_service_type_id ON services (service_type_id);
                CREATE UNIQUE INDEX ix_service_types_code ON service_types (code);
                CREATE UNIQUE INDEX ix_vital_signs_appointment_id ON vital_signs (appointment_id);
                CREATE INDEX ix_vital_signs_recorded_by ON vital_signs (recorded_by);");
            // 8. Gate kiểm chứng — sai là rollback
            migrationBuilder.Sql(@"-- 7.1 Gate: mọi kiểm tra dưới đây sai là transaction rollback, không cho align dở dang
                DO $$
                DECLARE n integer;
                BEGIN
                    SELECT count(*) INTO n FROM appointment_statuses;
                    IF n <> 10 THEN RAISE EXCEPTION 'appointment_statuses phải có 10 dòng, đang có %', n; END IF;
                
                    SELECT count(*) INTO n FROM appointments a WHERE a.status_id NOT IN (SELECT id FROM appointment_statuses);
                    IF n <> 0 THEN RAISE EXCEPTION 'appointments còn % dòng status mồ côi', n; END IF;
                
                    SELECT count(*) INTO n FROM appointment_status_history
                        WHERE (old_status_id IS NOT NULL AND old_status_id NOT IN (SELECT id FROM appointment_statuses))
                           OR (new_status_id NOT IN (SELECT id FROM appointment_statuses));
                    IF n <> 0 THEN RAISE EXCEPTION 'appointment_status_history còn % dòng status mồ côi', n; END IF;
                
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='patients' AND column_name='account_id') THEN
                        RAISE EXCEPTION 'patients.account_id chưa bị xóa';
                    END IF;
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='patients' AND column_name='insurance_card_number') THEN
                        RAISE EXCEPTION 'patients.insurance_card_number chưa bị xóa';
                    END IF;
                
                    SELECT count(*) INTO n FROM information_schema.tables
                        WHERE table_schema='public' AND table_type='BASE TABLE' AND table_name <> '__EFMigrationsHistory';
                    IF n <> 33 THEN RAISE EXCEPTION 'CSDL phải có 33 bảng nghiệp vụ, đang có %', n; END IF;
                
                    SELECT count(*) INTO n FROM account_patient_profiles;
                    IF n <> 5 THEN RAISE EXCEPTION 'account_patient_profiles phải có 5 dòng di trú, đang có %', n; END IF;
                
                    SELECT count(*) INTO n FROM information_schema.columns
                        WHERE table_schema='public' AND data_type='timestamp with time zone';
                    IF n <> 0 THEN RAISE EXCEPTION 'còn % cột timestamp with time zone', n; END IF;
                
                    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE tablename='appointments' AND indexname='uq_patient_slot_active')
                       OR NOT EXISTS (SELECT 1 FROM pg_indexes WHERE tablename='appointments' AND indexname='idx_appointments_hold')
                       OR NOT EXISTS (SELECT 1 FROM pg_indexes WHERE tablename='account_patient_profiles' AND indexname='uq_link_self_account') THEN
                        RAISE EXCEPTION '3 partial index chưa tồn tại';
                    END IF;
                
                    SELECT count(*) INTO n FROM appointments WHERE booked_by IS NULL;
                    IF n <> 0 THEN RAISE EXCEPTION 'appointments còn % dòng chưa có booked_by', n; END IF;
                END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Chỉ đảo được phần cộng thêm (bảng/cột/index mới).
            // Renames, remap id appointment_statuses và việc xóa patients.account_id là một chiều:
            // phục hồi bằng pg_restore backup/hospital_his_20261103_225211.dump.
            migrationBuilder.Sql("DROP INDEX ix_access_logs_medical_record_id");
            migrationBuilder.Sql("DROP INDEX idx_access_logs_patient");
            migrationBuilder.Sql("DROP INDEX ix_accounts_email");
            migrationBuilder.Sql("DROP INDEX ix_accounts_role_id");
            migrationBuilder.Sql("DROP INDEX ix_accounts_username");
            migrationBuilder.Sql("DROP INDEX uq_link_self_account");
            migrationBuilder.Sql("DROP INDEX ix_account_patient_profiles_created_by");
            migrationBuilder.Sql("DROP INDEX uq_link_self_patient");
            migrationBuilder.Sql("DROP INDEX ix_account_patient_profiles_verified_by");
            migrationBuilder.Sql("DROP INDEX idx_link_patient");
            migrationBuilder.Sql("DROP INDEX ix_appointments_appointment_code");
            migrationBuilder.Sql("DROP INDEX ix_appointments_department_id");
            migrationBuilder.Sql("DROP INDEX ix_appointments_doctor_id");
            migrationBuilder.Sql("DROP INDEX idx_appointments_hold");
            migrationBuilder.Sql("DROP INDEX ix_appointments_parent_appointment_id");
            migrationBuilder.Sql("DROP INDEX ix_appointments_room_id");
            migrationBuilder.Sql("DROP INDEX idx_appointments_slot");
            migrationBuilder.Sql("DROP INDEX idx_appointments_booked_by");
            migrationBuilder.Sql("DROP INDEX uq_patient_slot_active");
            migrationBuilder.Sql("DROP INDEX ix_appointment_slots_room_id");
            migrationBuilder.Sql("DROP INDEX ix_appointment_statuses_code");
            migrationBuilder.Sql("DROP INDEX ix_appointment_status_history_appointment_id");
            migrationBuilder.Sql("DROP INDEX ix_appointment_status_history_new_status_id");
            migrationBuilder.Sql("DROP INDEX ix_appointment_status_history_old_status_id");
            migrationBuilder.Sql("DROP INDEX ix_appointment_status_history_performed_by");
            migrationBuilder.Sql("DROP INDEX ix_clinic_rooms_department_id");
            migrationBuilder.Sql("DROP INDEX ix_department_heads_account_id");
            migrationBuilder.Sql("DROP INDEX ix_department_heads_department_id");
            migrationBuilder.Sql("DROP INDEX ix_doctors_account_id");
            migrationBuilder.Sql("DROP INDEX ix_doctors_department_id");
            migrationBuilder.Sql("DROP INDEX ix_doctor_schedules_doctor_id");
            migrationBuilder.Sql("DROP INDEX ix_doctor_schedules_room_id");
            migrationBuilder.Sql("DROP INDEX ix_duty_shifts_assigned_by");
            migrationBuilder.Sql("DROP INDEX ix_duty_shifts_department_id");
            migrationBuilder.Sql("DROP INDEX ix_insurance_cards_card_number");
            migrationBuilder.Sql("DROP INDEX ix_insurance_cards_insurance_plan_id");
            migrationBuilder.Sql("DROP INDEX ix_invoices_appointment_id");
            migrationBuilder.Sql("DROP INDEX ix_invoices_insurance_card_id");
            migrationBuilder.Sql("DROP INDEX ix_invoices_insurance_plan_id");
            migrationBuilder.Sql("DROP INDEX ix_invoices_patient_id");
            migrationBuilder.Sql("DROP INDEX ix_invoice_items_invoice_id");
            migrationBuilder.Sql("DROP INDEX ix_lab_orders_medical_record_id");
            migrationBuilder.Sql("DROP INDEX ix_lab_orders_service_id");
            migrationBuilder.Sql("DROP INDEX ix_lab_results_order_id");
            migrationBuilder.Sql("DROP INDEX ix_lab_results_performed_by");
            migrationBuilder.Sql("DROP INDEX ix_medical_records_doctor_id");
            migrationBuilder.Sql("DROP INDEX ix_notifications_appointment_id");
            migrationBuilder.Sql("DROP INDEX idx_notifications_account");
            migrationBuilder.Sql("DROP INDEX ix_nurses_account_id");
            migrationBuilder.Sql("DROP INDEX ix_nurses_department_id");
            migrationBuilder.Sql("DROP INDEX ix_patients_national_id");
            migrationBuilder.Sql("DROP INDEX ix_payments_cashier_id");
            migrationBuilder.Sql("DROP INDEX ix_prescriptions_medical_record_id");
            migrationBuilder.Sql("DROP INDEX ix_prescription_items_medication_id");
            migrationBuilder.Sql("DROP INDEX ix_prescription_items_prescription_id");
            migrationBuilder.Sql("DROP INDEX ix_queue_counters_room_id");
            migrationBuilder.Sql("DROP INDEX ix_roles_code");
            migrationBuilder.Sql("DROP INDEX ix_schedule_exceptions_approved_by");
            migrationBuilder.Sql("DROP INDEX ix_schedule_exceptions_doctor_id");
            migrationBuilder.Sql("DROP INDEX ix_schedule_exceptions_replacement_doctor_id");
            migrationBuilder.Sql("DROP INDEX ix_services_service_type_id");
            migrationBuilder.Sql("DROP INDEX ix_service_types_code");
            migrationBuilder.Sql("DROP INDEX ix_vital_signs_appointment_id");
            migrationBuilder.Sql("DROP INDEX ix_vital_signs_recorded_by");

            migrationBuilder.Sql("ALTER TABLE access_logs DROP CONSTRAINT fk_access_logs_patients_patient_id");
            migrationBuilder.Sql("ALTER TABLE appointments DROP CONSTRAINT fk_appointments_accounts_booked_by");
            migrationBuilder.Sql("ALTER TABLE appointments DROP CONSTRAINT fk_appointments_departments_department_id");
            migrationBuilder.Sql("ALTER TABLE appointments DROP CONSTRAINT fk_appointments_doctors_doctor_id");

            migrationBuilder.Sql("ALTER TABLE access_logs DROP COLUMN patient_id");
            migrationBuilder.Sql("ALTER TABLE accounts DROP COLUMN email");
            migrationBuilder.Sql("ALTER TABLE appointments DROP COLUMN booked_by");
            migrationBuilder.Sql("ALTER TABLE appointments DROP COLUMN called_at");
            migrationBuilder.Sql("ALTER TABLE appointments DROP COLUMN checked_in_at");
            migrationBuilder.Sql("ALTER TABLE appointments DROP COLUMN department_id");
            migrationBuilder.Sql("ALTER TABLE appointments DROP COLUMN doctor_id");
            migrationBuilder.Sql("ALTER TABLE doctor_schedules DROP COLUMN max_patients");
            migrationBuilder.Sql("ALTER TABLE doctor_schedules DROP COLUMN online_slots");
            migrationBuilder.Sql("ALTER TABLE invoices DROP COLUMN coverage_pct_applied");
            migrationBuilder.Sql("ALTER TABLE payments DROP COLUMN receipt_email");
            migrationBuilder.Sql("ALTER TABLE schedule_exceptions DROP COLUMN status");

            migrationBuilder.Sql("ALTER TABLE doctor_schedules ALTER COLUMN day_of_week DROP NOT NULL");
            migrationBuilder.Sql("ALTER TABLE payments ALTER COLUMN payment_method DROP DEFAULT");

            migrationBuilder.Sql("DROP TABLE notifications");
            migrationBuilder.Sql("DROP TABLE queue_counters");
            migrationBuilder.Sql("DROP TABLE account_patient_profiles");
        }
    }
}
