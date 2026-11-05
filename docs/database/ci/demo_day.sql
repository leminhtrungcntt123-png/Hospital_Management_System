-- Nạp lịch hẹn "trong hôm nay" để chạy thật các đường đọc của quầy/dashboard trên schema mới.
-- Nạp sau docs/database/ci/demo_master.sql; mọi mốc ngày dùng current_date nên chạy được ở mọi ngày.
-- Chạy local: backup/db.sh his_rehearsal docs/database/ci/demo_day.sql   (hoặc hospital_his để demo)
-- Dọn/nap lại được nhiều lần; đối tượng trả về baseline nằm ở backup/unseed_demo.sql.
-- Fixture dựng theo cơ chế đánh số A/T: mỗi khung 60 phút có capacity = 60 / avg_consultation_minutes,
-- online_quota = round(capacity * 60%), vị trí 1..online_quota là suất A (web),
-- online_quota+1..capacity là suất T (tại quầy), số = tổng capacity các khung trước đó + vị trí.
BEGIN;

-- Dọn fixture cũ để chạy lại được nhiều lần. Các bảng con không có ON DELETE CASCADE.
-- Fixture nào đang được một lượt thật trỏ tới qua parent_appointment_id (chuyển tiếp tại quầy)
-- thì giữ nguyên, vì mối liên hệ đó là dữ liệu demo chứ không phải giàn giáo.
CREATE TEMPORARY TABLE seed_old ON COMMIT DROP AS
SELECT a.id
FROM appointments a
WHERE a.appointment_code LIKE 'HIS-SEED-%'
  AND NOT EXISTS (SELECT 1 FROM appointments c
                   WHERE c.parent_appointment_id = a.id
                     AND c.appointment_code NOT LIKE 'HIS-SEED-%');

DELETE FROM appointment_status_history h WHERE h.appointment_id IN (SELECT id FROM seed_old);
DELETE FROM notifications n WHERE n.appointment_id IN (SELECT id FROM seed_old);
DELETE FROM appointments WHERE id IN (SELECT id FROM seed_old);

-- Khung giờ ca sáng cho phòng 1 / bác sĩ 1: hôm nay và ngày mai, 4 khung 60 phút.
-- Chỉ chèn khung còn thiếu để không đụng khung thật đã có lịch hẹn.
INSERT INTO appointment_slots (doctor_id, room_id, start_time, end_time, capacity, online_quota,
                               booked_count, online_booked, status, version)
SELECT 1, 1, v.ts, v.ts + interval '60 min', cap.n, cap.q, 0, 0, 'AVAILABLE', 0
FROM (VALUES (date_trunc('day', now()) + interval '7 hours 30 min'),
             (date_trunc('day', now()) + interval '8 hours 30 min'),
             (date_trunc('day', now()) + interval '9 hours 30 min'),
             (date_trunc('day', now()) + interval '10 hours 30 min'),
             (date_trunc('day', now()) + interval '1 day 7 hours 30 min'),
             (date_trunc('day', now()) + interval '1 day 8 hours 30 min'),
             (date_trunc('day', now()) + interval '1 day 9 hours 30 min'),
             (date_trunc('day', now()) + interval '1 day 10 hours 30 min')) AS v(ts)
CROSS JOIN LATERAL (SELECT 60 / greatest(d.avg_consultation_minutes, 1) AS n,
                           round((60 / greatest(d.avg_consultation_minutes, 1)) * 0.6)::int AS q
                    FROM doctors d WHERE d.id = 1) AS cap
WHERE NOT EXISTS (SELECT 1 FROM appointment_slots s WHERE s.doctor_id = 1 AND s.start_time = v.ts)
  -- Ngày nào đã có khung theo model mới (capacity > 1, kể cả của bác sĩ thay thế) thì không chèn thêm,
  -- nếu không sẽ sinh hai dãy khung song song cho cùng một buổi sáng.
  AND NOT EXISTS (SELECT 1 FROM appointment_slots x
                   WHERE x.room_id = 1 AND x.start_time::date = v.ts::date AND x.capacity > 1);

-- Offset = tổng capacity các khung cùng ca, cùng phòng, cùng ngày và bắt đầu sớm hơn.
CREATE TEMPORARY TABLE seed_slot ON COMMIT DROP AS
SELECT s.id,
       s.room_id,
       s.doctor_id,
       s.start_time,
       s.capacity,
       s.online_quota,
       (SELECT coalesce(sum(o.capacity), 0)
          FROM appointment_slots o
         WHERE o.room_id = s.room_id
           AND o.start_time::date = s.start_time::date
           AND o.start_time < s.start_time
           AND (o.start_time::time < time '12:00') = (s.start_time::time < time '12:00')) AS capacity_before
FROM appointment_slots s
WHERE s.room_id = 1
  AND s.start_time::date IN (current_date, current_date + 1)
  AND s.start_time::time >= time '07:00'
  AND s.start_time::time < time '12:00';

-- 1) Hôm nay, khung đầu tiên, suất A vị trí 1 -> tab Chờ
INSERT INTO appointments (appointment_code, patient_id, slot_id, room_id, status_id, booking_channel, visit_type,
                          priority_level, queue_number, slot_position, reason, created_at, updated_at,
                          booked_by, doctor_id, department_id)
SELECT 'HIS-SEED-CONFIRMED', 1, k.id, k.room_id, 2, 'WEB', 'SCHEDULED', 0,
       'A' || lpad((k.capacity_before + 1)::text, 2, '0'), 1, 'Khám ho', now(), now(), 7, k.doctor_id, r.department_id
FROM seed_slot k JOIN clinic_rooms r ON r.id = k.room_id
WHERE k.start_time::date = current_date
  AND NOT EXISTS (SELECT 1 FROM appointments a WHERE a.appointment_code = 'HIS-SEED-CONFIRMED')
ORDER BY k.start_time LIMIT 1;

-- 2) Hôm nay, khung đầu tiên, suất T vị trí online_quota+1 -> tab Đã tiếp nhận
INSERT INTO appointments (appointment_code, patient_id, slot_id, room_id, status_id, booking_channel, visit_type,
                          priority_level, queue_number, slot_position, reason, created_at, updated_at,
                          checked_in_at, booked_by, doctor_id, department_id)
SELECT 'HIS-SEED-CHECKEDIN', 2, k.id, k.room_id, 3, 'ON_SITE', 'SCHEDULED', 0,
       'T' || lpad((k.capacity_before + k.online_quota + 1)::text, 2, '0'), k.online_quota + 1,
       'Tái khám', now(), now(), now(), 8, k.doctor_id, r.department_id
FROM seed_slot k JOIN clinic_rooms r ON r.id = k.room_id
WHERE k.start_time::date = current_date
  AND NOT EXISTS (SELECT 1 FROM appointments a WHERE a.appointment_code = 'HIS-SEED-CHECKEDIN')
ORDER BY k.start_time LIMIT 1;

-- 3) Hôm nay, đã hủy (không có slot, tính theo created_at) -> tab Đã hủy
INSERT INTO appointments (appointment_code, patient_id, slot_id, room_id, status_id, booking_channel, visit_type,
                          priority_level, reason, created_at, updated_at, booked_by, department_id)
SELECT 'HIS-SEED-CANCELLED', 6, NULL, 1, 7, 'ON_SITE', 'WALK_IN', 0, 'Bệnh nhân xin hủy',
       now(), now(), 1, 1
WHERE NOT EXISTS (SELECT 1 FROM appointments a WHERE a.appointment_code = 'HIS-SEED-CANCELLED');

-- 4) Ngày mai, khung đầu tiên, suất A vị trí 1 -> bộ đếm "Sắp tới"
INSERT INTO appointments (appointment_code, patient_id, slot_id, room_id, status_id, booking_channel, visit_type,
                          priority_level, queue_number, slot_position, reason, created_at, updated_at,
                          booked_by, doctor_id, department_id)
SELECT 'HIS-SEED-FUTURE', 4, k.id, k.room_id, 2, 'WEB', 'SCHEDULED', 0,
       'A' || lpad((k.capacity_before + 1)::text, 2, '0'), 1, 'Đặt trước', now(), now(), 1, k.doctor_id, r.department_id
FROM seed_slot k JOIN clinic_rooms r ON r.id = k.room_id
WHERE k.start_time::date = current_date + 1
ORDER BY k.start_time LIMIT 1;

-- booked_count / online_booked phản ánh đúng số lượt đang giữ chỗ (cùng vị từ với uq_patient_slot_active)
UPDATE appointment_slots s
SET booked_count = active.n,
    online_booked = active.web,
    status = CASE WHEN active.n >= s.capacity THEN 'FULL' ELSE 'AVAILABLE' END
FROM (SELECT s2.id,
             (SELECT count(*) FROM appointments a
               WHERE a.slot_id = s2.id AND a.status_id = ANY (ARRAY[1,2,3,4,5])) AS n,
             (SELECT count(*) FROM appointments a
               WHERE a.slot_id = s2.id AND a.status_id = ANY (ARRAY[1,2,3,4,5])
                 AND a.booking_channel = 'WEB'
                 AND coalesce(a.slot_position, s2.online_quota + 1) <= s2.online_quota) AS web
      FROM appointment_slots s2 WHERE s2.start_time::date >= current_date) AS active
WHERE s.id = active.id;

COMMIT;

SELECT a.appointment_code, st.code AS trang_thai, a.booking_channel AS kenh, a.queue_number AS so,
       a.slot_position AS vi_tri, to_char(s.start_time, 'MM-DD HH24:MI') AS khung
FROM appointments a
JOIN appointment_statuses st ON st.id = a.status_id
LEFT JOIN appointment_slots s ON s.id = a.slot_id
WHERE a.appointment_code LIKE 'HIS-SEED-%'
ORDER BY a.appointment_code;
