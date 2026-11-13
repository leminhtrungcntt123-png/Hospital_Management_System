-- ==============================================================================
-- SCRIPT NẠP DỮ LIỆU MẪU DEV MỞ RỘNG (POSTGRESQL SEED DATA)
-- Quy mô: 5 Khoa, 8 Phòng khám, 10 Bác sĩ, 3 Điều dưỡng, 26 Bệnh nhân,
--         Hàng đợi khám hôm nay, Bệnh án hoàn tất (ICD-10), Ca trực & Dịch vụ.
-- Mật khẩu chung cho toàn bộ tài khoản: Password123!
-- Hash BCrypt: $2a$11$ATCoGjEC4ZfNhR02DvGZeOu.E8VJPRtn6WOCN77.kH89OzoEVcd4C
-- ==============================================================================

BEGIN;

-- ------------------------------------------------------------------------------
-- 1. DANH MỤC PHÒNG BAN / KHOA CHUYÊN MÔN (5 Khoa)
-- ------------------------------------------------------------------------------
INSERT INTO departments (name, description) VALUES
    ('Khoa Nội tổng quát', 'Khám và điều trị các bệnh lý nội khoa người lớn'),
    ('Khoa Ngoại tổng quát', 'Phẫu thuật, thủ thuật và điều trị ngoại khoa'),
    ('Khoa Nhi', 'Khám, điều trị và theo dõi phát triển cho trẻ em'),
    ('Khoa Sản phụ khoa', 'Khám thai, theo dõi tiền sản và các bệnh phụ khoa'),
    ('Khoa Tim mạch', 'Khám, siêu âm Doppler và thăm dò chức năng tim mạch')
ON CONFLICT DO NOTHING;

-- ------------------------------------------------------------------------------
-- 2. TÀI KHOẢN HỆ THỐNG (accounts)
-- Mật khẩu chung: Password123!
-- Vai trò: 1=ADMIN, 2=DEPARTMENT_HEAD, 3=DOCTOR, 4=NURSE, 5=PATIENT
-- ------------------------------------------------------------------------------
INSERT INTO accounts (username, email, password_hash, role_id, is_active, created_at, updated_at) VALUES
    -- Admin & Trưởng khoa
    ('admin', 'admin@his.local', '$2a$11$ATCoGjEC4ZfNhR02DvGZeOu.E8VJPRtn6WOCN77.kH89OzoEVcd4C', 1, true, now(), now()),
    ('head_noi', 'head_noi@his.local', '$2a$11$ATCoGjEC4ZfNhR02DvGZeOu.E8VJPRtn6WOCN77.kH89OzoEVcd4C', 2, true, now(), now()),
    
    -- 9 Bác sĩ chuyên khoa (cùng với head_noi là đủ 10 bác sĩ)
    ('bs_nam', 'nam@his.local', '$2a$11$ATCoGjEC4ZfNhR02DvGZeOu.E8VJPRtn6WOCN77.kH89OzoEVcd4C', 3, true, now(), now()),
    ('bs_huy', 'huy@his.local', '$2a$11$ATCoGjEC4ZfNhR02DvGZeOu.E8VJPRtn6WOCN77.kH89OzoEVcd4C', 3, true, now(), now()),
    ('bs_thu', 'thu@his.local', '$2a$11$ATCoGjEC4ZfNhR02DvGZeOu.E8VJPRtn6WOCN77.kH89OzoEVcd4C', 3, true, now(), now()),
    ('bs_tuan', 'tuan@his.local', '$2a$11$ATCoGjEC4ZfNhR02DvGZeOu.E8VJPRtn6WOCN77.kH89OzoEVcd4C', 3, true, now(), now()),
    ('bs_phuong', 'phuong@his.local', '$2a$11$ATCoGjEC4ZfNhR02DvGZeOu.E8VJPRtn6WOCN77.kH89OzoEVcd4C', 3, true, now(), now()),
    ('bs_hung', 'hung@his.local', '$2a$11$ATCoGjEC4ZfNhR02DvGZeOu.E8VJPRtn6WOCN77.kH89OzoEVcd4C', 3, true, now(), now()),
    ('bs_linh', 'linh@his.local', '$2a$11$ATCoGjEC4ZfNhR02DvGZeOu.E8VJPRtn6WOCN77.kH89OzoEVcd4C', 3, true, now(), now()),
    ('bs_bao', 'bao@his.local', '$2a$11$ATCoGjEC4ZfNhR02DvGZeOu.E8VJPRtn6WOCN77.kH89OzoEVcd4C', 3, true, now(), now()),
    ('bs_vinh', 'vinh@his.local', '$2a$11$ATCoGjEC4ZfNhR02DvGZeOu.E8VJPRtn6WOCN77.kH89OzoEVcd4C', 3, true, now(), now()),

    -- 3 Điều dưỡng các khoa
    ('nurse_lan', 'lan@his.local', '$2a$11$ATCoGjEC4ZfNhR02DvGZeOu.E8VJPRtn6WOCN77.kH89OzoEVcd4C', 4, true, now(), now()),
    ('nurse_mai', 'mai@his.local', '$2a$11$ATCoGjEC4ZfNhR02DvGZeOu.E8VJPRtn6WOCN77.kH89OzoEVcd4C', 4, true, now(), now()),
    ('nurse_hoa', 'hoa@his.local', '$2a$11$ATCoGjEC4ZfNhR02DvGZeOu.E8VJPRtn6WOCN77.kH89OzoEVcd4C', 4, true, now(), now())
ON CONFLICT (username) DO NOTHING;

-- ------------------------------------------------------------------------------
-- 3. TRƯỞNG KHOA (department_heads)
-- ------------------------------------------------------------------------------
INSERT INTO department_heads (account_id, department_id, full_name)
SELECT a.id, d.id, 'BS.CKII Phạm Thu Hằng'
FROM accounts a, departments d
WHERE a.username = 'head_noi' AND d.name = 'Khoa Nội tổng quát'
ON CONFLICT (account_id) DO NOTHING;

-- ------------------------------------------------------------------------------
-- 4. BÁC SĨ (doctors - 10 Bác sĩ trải khắp 5 Khoa)
-- ------------------------------------------------------------------------------
INSERT INTO doctors (account_id, department_id, full_name, specialty, license_number, avg_consultation_minutes)
-- 1. Khoa Nội
SELECT a.id, d.id, 'BS.CKII Phạm Thu Hằng', 'Nội khoa chuyên sâu', 'CCHN-000', 15
FROM accounts a, departments d WHERE a.username = 'head_noi' AND d.name = 'Khoa Nội tổng quát'
ON CONFLICT (account_id) DO NOTHING;

INSERT INTO doctors (account_id, department_id, full_name, specialty, license_number, avg_consultation_minutes)
SELECT a.id, d.id, 'BS. Nguyễn Hoàng Nam', 'Nội tiêu hóa & Đa khoa', 'CCHN-001', 15
FROM accounts a, departments d WHERE a.username = 'bs_nam' AND d.name = 'Khoa Nội tổng quát'
ON CONFLICT (account_id) DO NOTHING;

INSERT INTO doctors (account_id, department_id, full_name, specialty, license_number, avg_consultation_minutes)
SELECT a.id, d.id, 'BS. Lê Quang Huy', 'Nội tiết & Đái tháo đường', 'CCHN-002', 15
FROM accounts a, departments d WHERE a.username = 'bs_huy' AND d.name = 'Khoa Nội tổng quát'
ON CONFLICT (account_id) DO NOTHING;

-- 2. Khoa Ngoại
INSERT INTO doctors (account_id, department_id, full_name, specialty, license_number, avg_consultation_minutes)
SELECT a.id, d.id, 'ThS.BS Trần Minh Thư', 'Ngoại tiêu hóa & Gan mật', 'CCHN-003', 15
FROM accounts a, departments d WHERE a.username = 'bs_thu' AND d.name = 'Khoa Ngoại tổng quát'
ON CONFLICT (account_id) DO NOTHING;

INSERT INTO doctors (account_id, department_id, full_name, specialty, license_number, avg_consultation_minutes)
SELECT a.id, d.id, 'BS. Vũ Quốc Tuấn', 'Chấn thương & Phẫu thuật tổng quát', 'CCHN-004', 15
FROM accounts a, departments d WHERE a.username = 'bs_tuan' AND d.name = 'Khoa Ngoại tổng quát'
ON CONFLICT (account_id) DO NOTHING;

-- 3. Khoa Nhi
INSERT INTO doctors (account_id, department_id, full_name, specialty, license_number, avg_consultation_minutes)
SELECT a.id, d.id, 'BS.CKI Đặng Mai Phương', 'Nhi tổng quát & Dinh dưỡng', 'CCHN-005', 15
FROM accounts a, departments d WHERE a.username = 'bs_phuong' AND d.name = 'Khoa Nhi'
ON CONFLICT (account_id) DO NOTHING;

INSERT INTO doctors (account_id, department_id, full_name, specialty, license_number, avg_consultation_minutes)
SELECT a.id, d.id, 'BS. Bùi Văn Hùng', 'Hô hấp & Dị ứng Nhi', 'CCHN-006', 15
FROM accounts a, departments d WHERE a.username = 'bs_hung' AND d.name = 'Khoa Nhi'
ON CONFLICT (account_id) DO NOTHING;

-- 4. Khoa Sản phụ khoa
INSERT INTO doctors (account_id, department_id, full_name, specialty, license_number, avg_consultation_minutes)
SELECT a.id, d.id, 'ThS.BS Hoàng Thùy Linh', 'Sản khoa & Tiền sản', 'CCHN-007', 15
FROM accounts a, departments d WHERE a.username = 'bs_linh' AND d.name = 'Khoa Sản phụ khoa'
ON CONFLICT (account_id) DO NOTHING;

INSERT INTO doctors (account_id, department_id, full_name, specialty, license_number, avg_consultation_minutes)
SELECT a.id, d.id, 'BS. Ngô Gia Bảo', 'Phụ khoa & Tầm soát ung thư', 'CCHN-008', 15
FROM accounts a, departments d WHERE a.username = 'bs_bao' AND d.name = 'Khoa Sản phụ khoa'
ON CONFLICT (account_id) DO NOTHING;

-- 5. Khoa Tim mạch
INSERT INTO doctors (account_id, department_id, full_name, specialty, license_number, avg_consultation_minutes)
SELECT a.id, d.id, 'TS.BS Trịnh Công Vinh', 'Tim mạch can thiệp & Tăng huyết áp', 'CCHN-009', 15
FROM accounts a, departments d WHERE a.username = 'bs_vinh' AND d.name = 'Khoa Tim mạch'
ON CONFLICT (account_id) DO NOTHING;

-- ------------------------------------------------------------------------------
-- 5. ĐIỀU DƯỠNG (nurses)
-- ------------------------------------------------------------------------------
INSERT INTO nurses (account_id, department_id, full_name)
SELECT a.id, d.id, 'Điều dưỡng Hoàng Lan' FROM accounts a, departments d WHERE a.username = 'nurse_lan' AND d.name = 'Khoa Nội tổng quát'
UNION ALL
SELECT a.id, d.id, 'Điều dưỡng Nguyễn Tuyết Mai' FROM accounts a, departments d WHERE a.username = 'nurse_mai' AND d.name = 'Khoa Nhi'
UNION ALL
SELECT a.id, d.id, 'Điều dưỡng Trần Quỳnh Hoa' FROM accounts a, departments d WHERE a.username = 'nurse_hoa' AND d.name = 'Khoa Sản phụ khoa'
ON CONFLICT (account_id) DO NOTHING;

-- ------------------------------------------------------------------------------
-- 6. BUỒNG / PHÒNG KHÁM (clinic_rooms - 8 Phòng)
-- ------------------------------------------------------------------------------
INSERT INTO clinic_rooms (department_id, room_name, room_number)
SELECT d.id, 'Phòng khám Nội 1', 'P101' FROM departments d WHERE d.name = 'Khoa Nội tổng quát'
UNION ALL
SELECT d.id, 'Phòng khám Nội 2', 'P102' FROM departments d WHERE d.name = 'Khoa Nội tổng quát'
UNION ALL
SELECT d.id, 'Phòng khám Ngoại 1', 'P201' FROM departments d WHERE d.name = 'Khoa Ngoại tổng quát'
UNION ALL
SELECT d.id, 'Phòng tiểu phẫu Ngoại', 'P202' FROM departments d WHERE d.name = 'Khoa Ngoại tổng quát'
UNION ALL
SELECT d.id, 'Phòng khám Nhi 1', 'P301' FROM departments d WHERE d.name = 'Khoa Nhi'
UNION ALL
SELECT d.id, 'Phòng khám & Tiêm chủng Nhi 2', 'P302' FROM departments d WHERE d.name = 'Khoa Nhi'
UNION ALL
SELECT d.id, 'Phòng khám Sản phụ khoa', 'P401' FROM departments d WHERE d.name = 'Khoa Sản phụ khoa'
UNION ALL
SELECT d.id, 'Phòng khám Tim mạch & Điện tim', 'P501' FROM departments d WHERE d.name = 'Khoa Tim mạch'
ON CONFLICT DO NOTHING;

-- ------------------------------------------------------------------------------
-- 7. BỆNH NHÂN (patients - 26 Bệnh nhân đủ độ tuổi, giới tính)
-- ------------------------------------------------------------------------------
INSERT INTO patients (full_name, date_of_birth, gender, phone_number, national_id, address) VALUES
    ('Trần Quốc Bảo', '1984-05-10', 'MALE', '0912001100', '001084000001', 'Quận Cầu Giấy, Hà Nội'),
    ('Phạm Minh Tuấn', '1988-08-20', 'MALE', '0912002200', '001088000002', 'Quận 1, TP. Hồ Chí Minh'),
    ('Nguyễn Thanh Hà', '1994-06-15', 'FEMALE', '0901234567', '001094000003', 'Quận 3, TP. Hồ Chí Minh'),
    ('Nguyễn Văn Minh', '1962-02-16', 'MALE', '0902223344', '001062000004', 'Quận Ba Đình, Hà Nội'),
    ('Lê Thị Ngọc', '1978-09-12', 'FEMALE', '0923456789', '001078000005', 'Quận 5, TP. Hồ Chí Minh'),
    ('Đỗ Gia Khang', '1982-12-03', 'MALE', '0978901234', '001082000006', 'Quận 10, TP. Hồ Chí Minh'),
    ('Hoàng Văn Thắng', '1975-04-12', 'MALE', '0913112233', '001075000007', 'Quận Đống Đa, Hà Nội'),
    ('Vũ Thị Bích', '1990-11-25', 'FEMALE', '0984556677', '001090000008', 'Quận Thanh Xuân, Hà Nội'),
    ('Đặng Quốc Anh', '2000-01-18', 'MALE', '0936778899', '001100000009', 'Quận Hai Bà Trưng, Hà Nội'),
    ('Bùi Diệu Linh', '1996-08-05', 'FEMALE', '0961223344', '001096000010', 'Quận Hoàn Kiếm, Hà Nội'),
    ('Ngô Văn Long', '1958-03-30', 'MALE', '0973445566', '001058000011', 'Quận Tây Hồ, Hà Nội'),
    ('Trịnh Thị Mai', '1985-07-14', 'FEMALE', '0945667788', '001085000012', 'Quận Long Biên, Hà Nội'),
    ('Phan Thành Nam', '2018-09-20', 'MALE', '0918990011', '001218000013', 'Quận 7, TP. Hồ Chí Minh'),
    ('Lý Gia Hân', '2021-05-10', 'FEMALE', '0932114455', '001221000014', 'Quận Bình Thạnh, TP. Hồ Chí Minh'),
    ('Trương Hoàng Khôi', '2019-12-02', 'MALE', '0987223311', '001219000015', 'Quận Tân Bình, TP. Hồ Chí Minh'),
    ('Đinh Thục Quyên', '1992-02-28', 'FEMALE', '0909334455', '001092000016', 'Quận Phú Nhuận, TP. Hồ Chí Minh'),
    ('Võ Mỹ Duyên', '1995-10-15', 'FEMALE', '0977665544', '001095000017', 'Quận Gò Vấp, TP. Hồ Chí Minh'),
    ('Huỳnh Tấn Phát', '1969-06-22', 'MALE', '0914887766', '001069000018', 'TP. Thủ Đức, TP. Hồ Chí Minh'),
    ('Đoàn Ngọc Ánh', '1981-03-08', 'FEMALE', '0938554433', '001081000019', 'Quận 8, TP. Hồ Chí Minh'),
    ('Cao Đức Trọng', '1973-12-19', 'MALE', '0966443322', '001073000020', 'TP. Biên Hòa, Đồng Nai'),
    ('Tạ Kim Ngân', '1998-07-29', 'FEMALE', '0983112299', '001098000021', 'TP. Vũng Tàu, Bà Rịa - Vũng Tàu'),
    ('Dương Đình Trí', '1987-04-03', 'MALE', '0908776655', '001087000022', 'TP. Dĩ An, Bình Dương'),
    ('Lương Thu Thảo', '2002-09-17', 'FEMALE', '0944221100', '001102000023', 'Quận 12, TP. Hồ Chí Minh'),
    ('Chu Bá Thông', '1965-08-21', 'MALE', '0919334488', '001065000024', 'Quận Ngô Quyền, Hải Phòng'),
    ('Mai Phương Thúy', '1989-10-04', 'FEMALE', '0972556633', '001089000025', 'Quận Hải Châu, Đà Nẵng'),
    ('Nghiêm Xuân Mạnh', '1993-12-11', 'MALE', '0933887722', '001093000026', 'Quận Ninh Kiều, Cần Thơ')
ON CONFLICT (national_id) DO NOTHING;

-- ------------------------------------------------------------------------------
-- 8. LỊCH KHÁM TUẦN (doctor_schedules: Thứ 2 đến Thứ 6 cho cả 10 Bác sĩ)
-- ------------------------------------------------------------------------------
-- Hàm tiện ích chèn lịch sáng/chiều 5 ngày cho từng bác sĩ vào buồng khám tương ứng
INSERT INTO doctor_schedules (doctor_id, room_id, day_of_week, shift, start_time, end_time, slot_duration_minutes, max_patients, online_slots, effective_from, is_active)
SELECT 
    d.id, r.id, day, s.shift,
    CASE WHEN s.shift = 'MORNING' THEN '07:30'::time ELSE '13:00'::time END,
    CASE WHEN s.shift = 'MORNING' THEN '11:30'::time ELSE '16:30'::time END,
    15, 35, 25, '2026-01-01', true
FROM (
    VALUES 
        ('CCHN-000', 'P101'),
        ('CCHN-001', 'P101'),
        ('CCHN-002', 'P102'),
        ('CCHN-003', 'P201'),
        ('CCHN-004', 'P202'),
        ('CCHN-005', 'P301'),
        ('CCHN-006', 'P302'),
        ('CCHN-007', 'P401'),
        ('CCHN-008', 'P401'),
        ('CCHN-009', 'P501')
) AS map(cchn, room_no)
JOIN doctors d ON d.license_number = map.cchn
JOIN clinic_rooms r ON r.room_number = map.room_no
CROSS JOIN generate_series(1, 5) AS day
CROSS JOIN (VALUES ('MORNING'), ('AFTERNOON')) AS s(shift)
ON CONFLICT DO NOTHING;

-- ------------------------------------------------------------------------------
-- 9. HÀNG ĐỢI KHÁM HÔM NAY VÀ LỊCH HẸN (appointments)
-- Trạng thái: 3=CHECKED_IN, 4=READY_FOR_DOCTOR, 5=IN_PROGRESS, 6=COMPLETED
-- ------------------------------------------------------------------------------

-- Lượt khám hôm nay tại các khoa:
INSERT INTO appointments (
    appointment_code, patient_id, doctor_id, department_id, room_id, status_id,
    booking_channel, visit_type, queue_number, reason, created_at, updated_at
)
SELECT 
    data.code, p.id, doc.id, doc.department_id, r.id, data.status_id,
    data.channel, data.vtype, data.qnum, data.reason,
    CURRENT_DATE + data.ctime, CURRENT_DATE + data.ctime
FROM (
    VALUES
    -- Khoa Nội (P101 - BS Nam)
    ('BN0006', '001084000001', 'CCHN-001', 'P101', 4, 'WEB', 'SCHEDULED', 'O01', 'Đau tức ngực, khó thở nhẹ khi gắng sức', '08:15:00'::time),
    ('BN0007', '001088000002', 'CCHN-001', 'P101', 5, 'ON_SITE', 'WALK_IN', 'W02', 'Khám tổng quát, ho kéo dài 2 tuần', '08:30:00'::time),
    ('BN0011', '001075000007', 'CCHN-001', 'P101', 3, 'WEB', 'SCHEDULED', 'O03', 'Tái khám dạ dày, ợ chua nhiều', '08:45:00'::time),
    
    -- Khoa Nội (P102 - BS Huy)
    ('BN0012', '001090000008', 'CCHN-002', 'P102', 4, 'WEB', 'SCHEDULED', 'O01', 'Kiểm tra đường huyết định kỳ', '08:20:00'::time),
    ('BN0013', '001058000011', 'CCHN-002', 'P102', 3, 'ON_SITE', 'WALK_IN', 'W02', 'Mệt mỏi, sụt cân không rõ nguyên nhân', '09:00:00'::time),

    -- Khoa Ngoại (P201 - BS Thư)
    ('BN0014', '001100000009', 'CCHN-003', 'P201', 4, 'WEB', 'SCHEDULED', 'O01', 'Đau vùng hạ sườn phải âm ỉ', '08:30:00'::time),
    ('BN0015', '001085000012', 'CCHN-003', 'P201', 3, 'ON_SITE', 'WALK_IN', 'W02', 'Cần kiểm tra vết thương sau tiểu phẫu', '09:15:00'::time),

    -- Khoa Nhi (P301 - BS Phương)
    ('BN0016', '001218000013', 'CCHN-005', 'P301', 4, 'WEB', 'SCHEDULED', 'O01', 'Sốt nhẹ, biếng ăn và ho về đêm', '08:30:00'::time),
    ('BN0017', '001221000014', 'CCHN-005', 'P301', 3, 'ON_SITE', 'WALK_IN', 'W02', 'Khám sức khỏe tổng quát trẻ 3 tuổi', '09:10:00'::time),

    -- Khoa Sản phụ khoa (P401 - BS Linh)
    ('BN0018', '001092000016', 'CCHN-007', 'P401', 4, 'WEB', 'SCHEDULED', 'O01', 'Khám thai định kỳ tuần thứ 28', '08:40:00'::time),
    ('BN0019', '001095000017', 'CCHN-007', 'P401', 3, 'ON_SITE', 'WALK_IN', 'W02', 'Đau bụng dưới, rối loạn chu kỳ', '09:20:00'::time),

    -- Khoa Tim mạch (P501 - BS Vinh)
    ('BN0020', '001069000018', 'CCHN-009', 'P501', 4, 'WEB', 'SCHEDULED', 'O01', 'Huyết áp dao động, đau nhói ngực trái', '08:30:00'::time),
    ('BN0021', '001081000019', 'CCHN-009', 'P501', 3, 'ON_SITE', 'WALK_IN', 'W02', 'Hồi hộp, tim đập nhanh khi gắng sức', '09:05:00'::time)
) AS data(code, cccd, cchn, room_no, status_id, channel, vtype, qnum, reason, ctime)
JOIN patients p ON p.national_id = data.cccd
JOIN doctors doc ON doc.license_number = data.cchn
JOIN clinic_rooms r ON r.room_number = data.room_no
ON CONFLICT (appointment_code) DO NOTHING;

-- Các lượt khám đã hoàn tất trong quá khứ (để sinh bệnh án mẫu)
INSERT INTO appointments (
    appointment_code, patient_id, doctor_id, department_id, room_id, status_id,
    booking_channel, visit_type, queue_number, reason, created_at, updated_at
)
SELECT 
    data.code, p.id, doc.id, doc.department_id, r.id, 6,
    data.channel, 'SCHEDULED', data.qnum, data.reason,
    NOW() - data.days_ago, NOW() - data.days_ago
FROM (
    VALUES
    ('BN0010', '001082000006', 'CCHN-002', 'P102', 'WEB', 'O05', 'Tái khám huyết áp', INTERVAL '2 days'),
    ('BN0001', '001094000003', 'CCHN-001', 'P101', 'WEB', 'O03', 'Khám sức khỏe tổng quát', INTERVAL '10 days'),
    ('BN0002', '001062000004', 'CCHN-009', 'P501', 'WEB', 'O02', 'Đau thắt ngực khi leo cầu thang', INTERVAL '5 days'),
    ('BN0003', '001219000015', 'CCHN-006', 'P302', 'ON_SITE', 'W01', 'Ho đờm, khò khè kéo dài', INTERVAL '3 days'),
    ('BN0004', '001096000010', 'CCHN-007', 'P401', 'WEB', 'O04', 'Khám thai 12 tuần đo độ mờ da gáy', INTERVAL '7 days'),
    ('BN0005', '001073000020', 'CCHN-004', 'P202', 'ON_SITE', 'W03', 'Chấn thương phần mềm khớp gối trái', INTERVAL '4 days')
) AS data(code, cccd, cchn, room_no, channel, qnum, reason, days_ago)
JOIN patients p ON p.national_id = data.cccd
JOIN doctors doc ON doc.license_number = data.cchn
JOIN clinic_rooms r ON r.room_number = data.room_no
ON CONFLICT (appointment_code) DO NOTHING;

-- ------------------------------------------------------------------------------
-- 10. HỒ SƠ BỆNH ÁN (medical_records - Mã bệnh ICD-10 chuẩn xác)
-- ------------------------------------------------------------------------------
INSERT INTO medical_records (
    appointment_id, doctor_id, symptoms, clinical_examination,
    primary_diagnosis_code, primary_diagnosis_desc, conclusion, created_at, updated_at
)
SELECT 
    app.id, app.doctor_id, data.symptoms, data.exam,
    data.icd, data.desc, data.conclusion, app.created_at, app.updated_at
FROM (
    VALUES
    ('BN0010', 'Huyết áp dao động, hơi chóng mặt vào buổi sáng', 'Tim đều, phổi trong, HA 135/85 mmHg', 'I10', 'Tăng huyết áp nguyên phát', 'Uống thuốc đều, giảm muối, tái khám 1 tháng'),
    ('BN0001', 'Không có triệu chứng bất thường, ăn ngủ tốt', 'Thể trạng tốt, các chỉ số sinh hiệu trong giới hạn bình thường', 'Z00.0', 'Khám sức khỏe tổng quát', 'Sức khỏe tốt, duy trì tập luyện thể thao'),
    ('BN0002', 'Đau tức ngực trái khi gắng sức, lan ra vai trái', 'Mạch 82 l/p, HA 140/90 mmHg, T1 T2 rõ, không tiếng thổi', 'I20.9', 'Cơn đau thắt ngực không đặc hiệu', 'Chỉ định đo điện tim và siêu âm tim Doppler màu'),
    ('BN0003', 'Sốt nhẹ 38 độ C, ho đờm vàng, sổ mũi, quấy khóc', 'Họng đỏ nhẹ, phổi nghe có ran ẩm rải rác 2 phế trường', 'J20.9', 'Viêm phế quản cấp', 'Kê kháng sinh uống, siro ho thảo dược, bù điện giải'),
    ('BN0004', 'Thai phụ khỏe, không nghén, không xuất huyết', 'Bề cao tử cung phù hợp tuổi thai, tim thai 155 l/p', 'Z34.0', 'Giám sát thai kỳ bình thường', 'Siêu âm 12 tuần bình thường, hẹn khám tuần 16'),
    ('BN0005', 'Khớp gối trái sưng nề sau va chạm đá bóng, đi lại đau', 'Gối trái tràn dịch nhẹ, ngăn kéo trước (-), X-quang không gãy xương', 'S83.6', 'Bong gân và căng cơ khớp gối', 'Băng chun cố định, chườm lạnh, dùng thuốc giảm đau kháng viêm')
) AS data(code, symptoms, exam, icd, "desc", conclusion)
JOIN appointments app ON app.appointment_code = data.code
ON CONFLICT (appointment_id) DO NOTHING;

-- ------------------------------------------------------------------------------
-- 11. PHÂN CÔNG CA TRỰC NGOÀI GIỜ (duty_shifts)
-- ------------------------------------------------------------------------------
INSERT INTO duty_shifts (account_id, department_id, start_time, end_time, note, assigned_by, created_at)
SELECT 
    a.id, d.id,
    CURRENT_DATE + TIME '18:00:00', (CURRENT_DATE + 1) + TIME '07:00:00',
    data.note, head.id, NOW()
FROM (
    VALUES
    ('bs_nam', 'Khoa Nội tổng quát', 'Trực cấp cứu nội khoa và phòng lưu bệnh'),
    ('nurse_lan', 'Khoa Nội tổng quát', 'Trực điều dưỡng theo dõi sinh hiệu bệnh phòng'),
    ('bs_tuan', 'Khoa Ngoại tổng quát', 'Trực xử lý chấn thương và cấp cứu ngoại'),
    ('bs_phuong', 'Khoa Nhi', 'Trực cấp cứu nhi sảnh Nhi khoa'),
    ('nurse_mai', 'Khoa Nhi', 'Trực chăm sóc bệnh nhi lưu đêm')
) AS data(u_name, dept_name, note)
JOIN accounts a ON a.username = data.u_name
JOIN departments d ON d.name = data.dept_name
JOIN accounts head ON head.username = 'head_noi'
ON CONFLICT DO NOTHING;

-- ------------------------------------------------------------------------------
-- 12. ĐỀ NGHỊ NGHỈ / ĐỔI CA (schedule_exceptions)
-- ------------------------------------------------------------------------------
INSERT INTO schedule_exceptions (doctor_id, date, shift, type, status, reason, replacement_doctor_id, created_at)
SELECT 
    d_req.id, CURRENT_DATE + 1, 'MORNING', 'TRAINING', 'PENDING',
    'Tham gia hội nghị đào tạo Tim mạch can thiệp', d_rep.id, NOW()
FROM doctors d_req, doctors d_rep
WHERE d_req.license_number = 'CCHN-009' AND d_rep.license_number = 'CCHN-001'
ON CONFLICT DO NOTHING;

INSERT INTO schedule_exceptions (doctor_id, date, shift, type, status, reason, replacement_doctor_id, created_at)
SELECT 
    d_req.id, CURRENT_DATE + 2, 'AFTERNOON', 'LEAVE', 'PENDING',
    'Nghỉ phép việc gia đình', d_rep.id, NOW()
FROM doctors d_req, doctors d_rep
WHERE d_req.license_number = 'CCHN-004' AND d_rep.license_number = 'CCHN-003'
ON CONFLICT DO NOTHING;

-- ------------------------------------------------------------------------------
-- 13. DANH MỤC DỊCH VỤ Y TẾ & BẢNG GIÁ (services)
-- 1=Khám, 2=Xét nghiệm, 3=CĐHA
-- ------------------------------------------------------------------------------
INSERT INTO services (service_type_id, name, price, is_active) VALUES
    (1, 'Khám bệnh chuyên khoa Nội', 150000, true),
    (1, 'Khám bệnh chuyên khoa Ngoại', 150000, true),
    (1, 'Khám bệnh chuyên khoa Nhi', 160000, true),
    (1, 'Khám chuyên khoa Sản phụ khoa', 180000, true),
    (1, 'Khám chuyên sâu Tim mạch', 200000, true),
    (2, 'Tổng phân tích tế bào máu ngoại vi (24 thông số)', 85000, true),
    (2, 'Sinh hóa máu cơ bản (Glucose, Ure, Creatinin, AST, ALT)', 150000, true),
    (2, 'Định lượng HbA1c (Đánh giá đường huyết)', 130000, true),
    (2, 'Xét nghiệm nước tiểu 10 thông số', 50000, true),
    (3, 'Chụp X-Quang tim phổi thẳng kỹ thuật số', 180000, true),
    (3, 'Siêu âm Doppler tim và mạch máu màu', 350000, true),
    (3, 'Siêu âm thai màu 4D / 5D khảo sát hình thái', 400000, true),
    (3, 'Siêu âm ổ bụng tổng quát màu', 200000, true),
    (3, 'Điện tâm đồ (ECG) 12 chuyển đạo', 90000, true)
ON CONFLICT DO NOTHING;

-- ------------------------------------------------------------------------------
-- 14. DANH MỤC THUỐC MẪU (medications)
-- ------------------------------------------------------------------------------
INSERT INTO medications (name, active_ingredient, strength, unit, price, is_active) VALUES
    ('Panadol Extra', 'Paracetamol, Caffeine', '500mg/65mg', 'Viên', 2500, true),
    ('Amoxicillin 500mg', 'Amoxicillin', '500mg', 'Viên', 3000, true),
    ('Augmentin 1g', 'Amoxicillin, Acid Clavulanic', '1000mg', 'Gói', 18500, true),
    ('Nexium 40mg', 'Esomeprazole', '40mg', 'Viên', 24000, true),
    ('Amlor 5mg', 'Amlodipine', '5mg', 'Viên', 8200, true),
    ('Glucophage 850mg', 'Metformin', '850mg', 'Viên', 4500, true),
    ('Hapacol 250', 'Paracetamol', '250mg', 'Gói', 3500, true),
    ('Klamentin 250/31.25', 'Amoxicillin, Clavulanate', '250mg', 'Gói', 11000, true),
    ('Prospan', 'Cao lá thường xuân khô', '100ml', 'Chai', 85000, true),
    ('Betaloc ZOK 25mg', 'Metoprolol', '25mg', 'Viên', 6800, true),
    ('Lipitor 20mg', 'Atorvastatin', '20mg', 'Viên', 21000, true),
    ('Elevit bầu', 'Đa vi chất & Acid Folic', 'Hộp 30v', 'Viên', 12000, true)
ON CONFLICT DO NOTHING;

-- ------------------------------------------------------------------------------
-- 15. NHẬT KÝ TRUY VẾT MẪU (access_logs)
-- ------------------------------------------------------------------------------
INSERT INTO access_logs (account_id, action, occurred_at, ip_address)
SELECT a.id, 'AdminLogin', NOW() - INTERVAL '4 hours', '127.0.0.1' FROM accounts a WHERE a.username = 'admin'
UNION ALL
SELECT a.id, 'ViewDepartmentCatalog', NOW() - INTERVAL '3 hours', '127.0.0.1' FROM accounts a WHERE a.username = 'admin'
UNION ALL
SELECT a.id, 'VIEW_RECORD', NOW() - INTERVAL '2 hours', '192.168.10.15' FROM accounts a WHERE a.username = 'head_noi'
UNION ALL
SELECT a.id, 'CREATE_RECORD', NOW() - INTERVAL '1 hour', '192.168.10.22' FROM accounts a WHERE a.username = 'bs_nam'
UNION ALL
SELECT a.id, 'CHECK_IN_WEB', NOW() - INTERVAL '45 minutes', '192.168.10.8' FROM accounts a WHERE a.username = 'nurse_lan';

COMMIT;
