-- Dữ liệu demo cho pipeline tích hợp, nạp lên CSDL Postgres vừa tạo từ docs/database/ci/schema.sql.
-- Schema.sql chỉ dựng khung + seed các bảng tra cứu có HasData (roles, service_types,
-- appointment_statuses); mọi bảng khác đều rỗng nên test integration sẽ "bỏ qua" thay vì kiểm chứng.
-- File này bổ sung đúng phần dữ liệu đó: danh mục, nhân sự, tài khoản, hồ sơ bệnh nhân, thẻ BHYT, lịch khám.
--
-- password_hash là chuỗi giả 60 ký tự theo hình dạng BCrypt, KHÔNG phải mật khẩu đã băm:
-- AuthService từ chối đăng nhập bằng nhánh "chưa được thiết lập mật khẩu hợp lệ".
-- CI không đăng nhập nên không cần hash thật; mật khẩu demo cho người thật vẫn nằm ngoài git.
--
-- Khóa chính khai báo GENERATED ALWAYS AS IDENTITY nên INSERT phải có OVERRIDING SYSTEM VALUE.
-- Chạy lại được nhiều lần: mọi INSERT dùng ON CONFLICT DO NOTHING và cuối file chỉnh lại sequence.
BEGIN;

INSERT INTO departments (id, name, description) OVERRIDING SYSTEM VALUE VALUES
    (1, 'Khoa Nội tổng hợp', 'Chẩn đoán và điều trị các bệnh nội khoa'),
    (2, 'Khoa Ngoại tổng hợp', 'Phẫu thuật và điều trị ngoại khoa'),
    (3, 'Khoa Nhi', 'Khám chữa bệnh và chăm sóc sức khỏe trẻ em'),
    (4, 'Khoa Tai Mũi Họng', 'Chẩn đoán và điều trị bệnh lý tai mũi họng'),
    (5, 'Khoa Xét nghiệm', 'Thực hiện các xét nghiệm y khoa')
ON CONFLICT (id) DO NOTHING;

INSERT INTO clinic_rooms (id, department_id, room_name, room_number) OVERRIDING SYSTEM VALUE VALUES
    (1, 1, 'Phòng khám Nội 1', 'P.101'),
    (2, 1, 'Phòng khám Nội 2', 'P.102'),
    (3, 2, 'Phòng khám Ngoại 1', 'P.201'),
    (4, 4, 'Phòng khám TMH', 'P.301')
ON CONFLICT (id) DO NOTHING;

-- Tài khoản: 1 quản trị, 2 bác sĩ, 3 y tá, 4 trưởng khoa, 5 thu ngân, 6 lễ tân, 7 bệnh nhân.
-- Id 9 để trống vì các test quầy dùng 7/8/10 theo đúng dữ liệu demo của nhóm.
INSERT INTO accounts (id, username, password_hash, role_id, is_active, failed_login_count, locked_until, created_at, updated_at, email) OVERRIDING SYSTEM VALUE VALUES
    (1,  'admin',            '$2a$12$PLACEHOLDERNOTAPASSWORDONLYFORCINOTLOGINS012345678901', 1, true, 0, NULL, now(), now(), NULL),
    (2,  'dr.levanc',        '$2a$12$PLACEHOLDERNOTAPASSWORDONLYFORCINOTLOGINS012345678901', 2, true, 0, NULL, now(), now(), NULL),
    (3,  'dr.phamthid',      '$2a$12$PLACEHOLDERNOTAPASSWORDONLYFORCINOTLOGINS012345678901', 2, true, 0, NULL, now(), now(), NULL),
    (4,  'nurse.hoange',     '$2a$12$PLACEHOLDERNOTAPASSWORDONLYFORCINOTLOGINS012345678901', 3, true, 0, NULL, now(), now(), NULL),
    (5,  'head.nguyenf',     '$2a$12$PLACEHOLDERNOTAPASSWORDONLYFORCINOTLOGINS012345678901', 6, true, 0, NULL, now(), now(), NULL),
    (6,  'cashier.trang',    '$2a$12$PLACEHOLDERNOTAPASSWORDONLYFORCINOTLOGINS012345678901', 5, true, 0, NULL, now(), now(), NULL),
    (7,  'patient.nguyena',  '$2a$12$PLACEHOLDERNOTAPASSWORDONLYFORCINOTLOGINS012345678901', 7, true, 0, NULL, now(), now(), NULL),
    (8,  'patient.tranb',    '$2a$12$PLACEHOLDERNOTAPASSWORDONLYFORCINOTLOGINS012345678901', 7, true, 0, NULL, now(), now(), NULL),
    (10, 'td.tranthivan',    '$2a$12$PLACEHOLDERNOTAPASSWORDONLYFORCINOTLOGINS012345678901', 4, true, 0, NULL, now(), now(), NULL)
ON CONFLICT (id) DO NOTHING;

INSERT INTO doctors (id, account_id, department_id, full_name, specialty, license_number, avg_consultation_minutes) OVERRIDING SYSTEM VALUE VALUES
    (1, 2, 1, 'BS. Lê Văn Cường', 'Nội tim mạch', 'CCHN-001234', 15),
    (2, 3, 4, 'BS. Phạm Thị Dung', 'Tai Mũi Họng', 'CCHN-005678', 15)
ON CONFLICT (id) DO NOTHING;

INSERT INTO nurses (id, account_id, department_id, full_name) OVERRIDING SYSTEM VALUE VALUES
    (1, 4, 1, 'Y tá Hoàng Thị Em'),
    (2, 10, 1, 'Điều dưỡng Trần Thị Vân')
ON CONFLICT (id) DO NOTHING;

INSERT INTO department_heads (id, account_id, department_id, full_name) OVERRIDING SYSTEM VALUE VALUES
    (1, 5, 1, 'BS. Nguyễn Văn Phúc')
ON CONFLICT (id) DO NOTHING;

-- Bệnh nhân: id 1 và 2 phải giữ đúng họ tên vì RehearsalReadPathsTests tìm theo tên
-- và theo mã "BN2" (mã bệnh nhân suy ra từ id, xem ReceptionService.TryParsePatientCode).
INSERT INTO patients (id, full_name, date_of_birth, gender, national_id, phone_number, address, created_at) OVERRIDING SYSTEM VALUE VALUES
    (1, 'Nguyễn Văn An', '1990-05-15', 'MALE',   '001090001234', '0901234567', '123 Nguyễn Trãi, Quận 1, TP.HCM', now()),
    (2, 'Trần Thị Bình', '1985-08-20', 'FEMALE', '001185005678', '0912345678', '456 Lê Lợi, Quận 3, TP.HCM', now()),
    (3, 'Lê Văn Chiến',  '1978-03-02', 'MALE',   '001178003333', '0913000003', '78 Hùng Vương, Ninh Kiều, Cần Thơ', now()),
    (4, 'Đỗ Thu Hà',     '2009-12-19', 'FEMALE', NULL,           '0909090919', NULL, now()),
    (5, 'Vương Thị Lan', '1956-07-11', 'FEMALE', '001156005555', '0913000005', '90 Trần Phú, Hải Châu, Đà Nẵng', now()),
    (6, 'Bùi Đức Nam',   '2019-02-14', 'MALE',   '001219006666', '0913000006', '120 Định Công, Hoàng Mai, Hà Nội', now())
ON CONFLICT (id) DO NOTHING;

-- Liên kết tài khoản <-> hồ sơ. SELF chỉ một mỗi tài khoản (uq_link_self_account),
-- can_view_records = true cho SELF, false cho hồ sơ người khác chưa được bệnh nhân đồng ý.
-- Không liên kết tài khoản 8 với bệnh nhân 1: PatientProfileFlowTests cần một lượt chờ hôm nay
-- (HIS-SEED-CONFIRMED của bệnh nhân 1) chưa liên kết để kiểm cờ "chờ duyệt liên kết".
INSERT INTO account_patient_profiles (account_id, patient_id, can_view_records, created_at, created_by, relationship, status, verified_at, verified_by) VALUES
    (7, 1, true,  now(), 1,    'SELF',  'ACTIVE', now(), 1),
    (8, 2, true,  now(), 1,    'SELF',  'ACTIVE', now(), 1),
    (8, 3, false, now(), 8,    'OTHER', 'ACTIVE', now(), 10),
    (7, 5, false, now(), 7,    'PARENT','ACTIVE', now(), 10),
    (7, 6, false, now(), 7,    'CHILD', 'ACTIVE', now(), 10)
ON CONFLICT (account_id, patient_id) DO NOTHING;

INSERT INTO insurance_plans (id, name, coverage_pct, referral_type) OVERRIDING SYSTEM VALUE VALUES
    (1, 'BHYT Đúng tuyến 80%',   80.00,  'IN_NETWORK'),
    (2, 'BHYT Đúng tuyến 100%',  100.00, 'IN_NETWORK'),
    (3, 'BHYT Trái tuyến 40%',   40.00,  'OUT_OF_NETWORK')
ON CONFLICT (id) DO NOTHING;

-- Hạn thẻ tính theo ngày hôm nay để test thanh toán không bị "hết hạn" khi CI chạy ở ngày khác.
INSERT INTO insurance_cards (id, patient_id, card_number, insurance_plan_id, registered_facility, is_registered_here, valid_from, valid_to, is_active, created_at) OVERRIDING SYSTEM VALUE VALUES
    (1, 1, 'DN4010123456789', 1, 'Bệnh viện HIS', true,  (current_date - interval '1 year')::date,
        (current_date + interval '1 year')::date, true, now()),
    (2, 2, 'DN4010987654321', 2, 'Bệnh viện HIS', true,  (current_date - interval '1 year')::date,
        (current_date + interval '1 year')::date, true, now())
ON CONFLICT (id) DO NOTHING;

INSERT INTO services (id, name, service_type_id, price, is_active) OVERRIDING SYSTEM VALUE VALUES
    (1, 'Khám bệnh Chuyên khoa',                        1, 150000.00, true),
    (2, 'Xét nghiệm Tế bào máu ngoại vi (CTM)',         2,  80000.00, true),
    (3, 'Xét nghiệm Glucose máu',                       2,  45000.00, true),
    (4, 'Chụp X-quang Ngực thẳng',                      3, 120000.00, true)
ON CONFLICT (id) DO NOTHING;

INSERT INTO medications (id, name, active_ingredient, strength, unit, price, is_active) OVERRIDING SYSTEM VALUE VALUES
    (1, 'Paracetamol 500mg',  'Paracetamol',                   '500mg', 'Viên',  1500.00, true),
    (2, 'Amoxicillin 500mg',  'Amoxicillin',                   '500mg', 'Viên',  3500.00, true),
    (3, 'Ibuprofen 400mg',    'Ibuprofen',                     '400mg', 'Viên',  2500.00, true),
    (4, 'Augmentin 625mg',    'Amoxicillin + Clavulanic acid', '625mg', 'Viên', 15000.00, true)
ON CONFLICT (id) DO NOTHING;

-- Lịch khám: sáng 07:30-11:30, chiều 13:00-16:00, khung 60 phút, 60% suất bán online.
-- day_of_week 1=Thứ Hai theo thứ tự trong Domain, effective_from lùi 30 ngày để lịch có hiệu lực ngay.
INSERT INTO doctor_schedules (id, doctor_id, room_id, day_of_week, shift, start_time, end_time, slot_duration_minutes, effective_from, effective_to, is_active, online_ratio_pct) OVERRIDING SYSTEM VALUE VALUES
    (1, 1, 1, 1, 'MORNING',   time '07:30', time '11:30', 60, (current_date - interval '30 days')::date, NULL, true, 60),
    (2, 1, 1, 2, 'MORNING',   time '07:30', time '11:30', 60, (current_date - interval '30 days')::date, NULL, true, 60),
    (3, 1, 1, 3, 'MORNING',   time '07:30', time '11:30', 60, (current_date - interval '30 days')::date, NULL, true, 60),
    (4, 1, 1, 4, 'MORNING',   time '07:30', time '11:30', 60, (current_date - interval '30 days')::date, NULL, true, 60),
    (5, 1, 1, 5, 'MORNING',   time '07:30', time '11:30', 60, (current_date - interval '30 days')::date, NULL, true, 60),
    (6, 2, 4, 1, 'AFTERNOON', time '13:00', time '16:00', 60, (current_date - interval '30 days')::date, NULL, true, 60),
    (7, 2, 4, 3, 'AFTERNOON', time '13:00', time '16:00', 60, (current_date - interval '30 days')::date, NULL, true, 60),
    (8, 2, 4, 5, 'AFTERNOON', time '13:00', time '16:00', 60, (current_date - interval '30 days')::date, NULL, true, 60)
ON CONFLICT (id) DO NOTHING;

-- EF migration dùng GENERATED BY DEFAULT AS IDENTITY nên id do CI chèn vẫn đi từ 1;
-- kéo sequence lên sau max(id) để các lượt/hồ sơ do test tạo không trùng khóa chính.
SELECT setval(pg_get_serial_sequence('departments', 'id'), (SELECT max(id) FROM departments), true);
SELECT setval(pg_get_serial_sequence('clinic_rooms', 'id'), (SELECT max(id) FROM clinic_rooms), true);
SELECT setval(pg_get_serial_sequence('accounts', 'id'), (SELECT max(id) FROM accounts), true);
SELECT setval(pg_get_serial_sequence('doctors', 'id'), (SELECT max(id) FROM doctors), true);
SELECT setval(pg_get_serial_sequence('nurses', 'id'), (SELECT max(id) FROM nurses), true);
SELECT setval(pg_get_serial_sequence('department_heads', 'id'), (SELECT max(id) FROM department_heads), true);
SELECT setval(pg_get_serial_sequence('patients', 'id'), (SELECT max(id) FROM patients), true);
SELECT setval(pg_get_serial_sequence('insurance_plans', 'id'), (SELECT max(id) FROM insurance_plans), true);
SELECT setval(pg_get_serial_sequence('insurance_cards', 'id'), (SELECT max(id) FROM insurance_cards), true);
SELECT setval(pg_get_serial_sequence('services', 'id'), (SELECT max(id) FROM services), true);
SELECT setval(pg_get_serial_sequence('medications', 'id'), (SELECT max(id) FROM medications), true);
SELECT setval(pg_get_serial_sequence('doctor_schedules', 'id'), (SELECT max(id) FROM doctor_schedules), true);

COMMIT;
