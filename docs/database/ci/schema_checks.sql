-- Gate kiểm chứng align theo init.md mục 5 + mô hình đánh số A/T theo vị trí.
-- Chạy trên DB được chỉ định: backup/db.sh <dbname> docs/database/ci/schema_checks.sql
-- CI chạy file này sau khi nạp docs/database/ci/schema.sql; cả 18 cột ok phải TRUE.
-- File in ra 18 dòng rồi DỪNG BẰNG LỖI nếu còn dòng ok = false: chỉ cần ON_ERROR_STOP,
-- không phải đếm kết quả bằng shell (xem DO $$ ở cuối file).
CREATE TEMPORARY VIEW gate AS
SELECT '1. so bang (khong ke __EFMigrationsHistory)' AS check,
       count(*)::text AS result,
       (count(*) = 32) AS ok
FROM information_schema.tables
WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND table_name <> '__EFMigrationsHistory'
UNION ALL
SELECT '2. appointment_statuses dung 10 id spec',
       string_agg(id || '=' || code, ',' ORDER BY id),
       count(*) = 10 AND bool_and(id = CASE code WHEN 'PENDING' THEN 1 WHEN 'CONFIRMED' THEN 2 WHEN 'CHECKED_IN' THEN 3
            WHEN 'READY_FOR_DOCTOR' THEN 4 WHEN 'IN_PROGRESS' THEN 5 WHEN 'COMPLETED' THEN 6 WHEN 'CANCELLED' THEN 7
            WHEN 'NO_SHOW' THEN 8 WHEN 'EXPIRED' THEN 9 WHEN 'RESCHEDULE_REQUIRED' THEN 10 END)
FROM appointment_statuses
UNION ALL
SELECT '3. patients khong con account_id / insurance_card_number',
       count(*)::text, count(*) = 0
FROM information_schema.columns
WHERE table_name = 'patients' AND column_name IN ('account_id', 'insurance_card_number')
UNION ALL
SELECT '4. link ho so',
       string_agg(status || '/' || relationship, ',' ORDER BY account_id) || ' = ' || count(*) || ' dong',
       count(*) >= 5
FROM account_patient_profiles
UNION ALL
SELECT '5. moi lich hen co booked_by',
       'thieu=' || count(*)::text, count(*) = 0
FROM appointments WHERE booked_by IS NULL
UNION ALL
SELECT '6. timestamp with time zone',
       'con=' || count(*)::text, count(*) = 0
FROM information_schema.columns
WHERE table_schema = 'public' AND data_type = 'timestamp with time zone'
UNION ALL
SELECT '7. day_of_week 0..6 NOT NULL',
       (SELECT is_nullable FROM information_schema.columns WHERE table_name = 'doctor_schedules' AND column_name = 'day_of_week')
         || ' | ' || coalesce((SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ck_doctor_schedules_day_of_week'), 'khong co check'),
       (SELECT is_nullable = 'NO' FROM information_schema.columns WHERE table_name = 'doctor_schedules' AND column_name = 'day_of_week')
         AND (SELECT pg_get_constraintdef(oid) LIKE '%>= 0%AND%<= 6%' FROM pg_constraint WHERE conname = 'ck_doctor_schedules_day_of_week')
UNION ALL
SELECT '8. partial index',
       string_agg(indexname || ' :: ' || replace(indexdef, 'CREATE UNIQUE INDEX ', ''), ' ' ORDER BY indexname),
       count(*) = 6
FROM pg_indexes
WHERE indexname IN ('idx_appointments_hold', 'uq_link_self_account', 'uq_link_self_patient', 'uq_patient_slot_active',
                    'uq_slot_position', 'idx_history_cancel')
  AND indexdef LIKE '%WHERE%'
UNION ALL
SELECT '9. FK all Restrict', 'cascade=' || count(*)::text, count(*) = 0
FROM pg_constraint WHERE contype = 'f' AND confdeltype = 'c'
UNION ALL
SELECT '10. queue_counters da bo, notifications con',
       'queue_counters=' || (SELECT count(*)::text FROM information_schema.tables WHERE table_name = 'queue_counters')
         || ' / notifications=' || (SELECT count(*)::text FROM notifications),
       (SELECT count(*) FROM information_schema.tables WHERE table_name = 'queue_counters') = 0
         AND (SELECT count(*) FROM information_schema.columns WHERE table_name = 'queue_counters') = 0
UNION ALL
SELECT '11. cot moi cua mo hinh A/T',
       string_agg(table_name || '.' || column_name, ',' ORDER BY table_name, column_name),
       count(*) = 8
FROM information_schema.columns
WHERE (table_name, column_name) IN (
  ('doctor_schedules', 'online_ratio_pct'),
  ('appointment_slots', 'online_quota'),
  ('appointment_slots', 'online_booked'),
  ('appointments', 'slot_position'),
  ('appointments', 'overtaken_count'),
  ('appointment_status_history', 'is_late_cancel'),
  ('accounts', 'booking_blocked_until'),
  ('accounts', 'no_show_reset_at'))
UNION ALL
SELECT '12. cot cu da bo',
       string_agg(table_name || '.' || column_name, ',' ORDER BY table_name, column_name),
       count(*) = 0
FROM information_schema.columns
WHERE (table_name, column_name) IN (
  ('doctor_schedules', 'max_patients'),
  ('doctor_schedules', 'online_slots'))
UNION ALL
SELECT '13. check constraint moi',
       string_agg(conname || ' ' || pg_get_constraintdef(oid, true), ' | ' ORDER BY conname),
       count(*) = 5
FROM pg_constraint
WHERE conname IN ('ck_online_quota', 'ck_online_booked', 'ck_appointments_slot_position',
                  'ck_appointments_overtaken_count', 'ck_doctor_schedules_online_ratio')
UNION ALL
SELECT '14. priority_level 0..2',
       coalesce((SELECT pg_get_constraintdef(oid, true) FROM pg_constraint WHERE conname = 'ck_appointments_priority_level'), 'khong co check'),
       (SELECT pg_get_constraintdef(oid) LIKE '%>= 0%' FROM pg_constraint WHERE conname = 'ck_appointments_priority_level')
UNION ALL
SELECT '15. slot_duration_minutes = 60, online_ratio_pct = 60',
       'duration=' || string_agg(DISTINCT slot_duration_minutes::text, ',')
         || ' / ratio=' || string_agg(DISTINCT online_ratio_pct::text, ','),
       bool_and(slot_duration_minutes = 60 AND online_ratio_pct = 60)
FROM doctor_schedules
UNION ALL
SELECT '16. du lieu slot khong vi pham quota',
       'quota>cap=' || (SELECT count(*)::text FROM appointment_slots WHERE online_quota > capacity)
         || ' / booked>quota=' || (SELECT count(*)::text FROM appointment_slots WHERE online_booked > online_quota)
         || ' / booked>cap=' || (SELECT count(*)::text FROM appointment_slots WHERE booked_count > capacity),
       (SELECT count(*) FROM appointment_slots WHERE online_quota > capacity) = 0
         AND (SELECT count(*) FROM appointment_slots WHERE online_booked > online_quota) = 0
         AND (SELECT count(*) FROM appointment_slots WHERE booked_count > capacity) = 0
UNION ALL
SELECT '17. hai cot khai bao bao hiem in appointments',
       string_agg(column_name || '/' || is_nullable || '/' || coalesce(column_default, 'khong co default'), ',' ORDER BY column_name),
       count(*) = 2 AND bool_and(data_type = 'boolean' AND is_nullable = 'NO' AND column_default LIKE '%false%')
FROM information_schema.columns
WHERE table_name = 'appointments' AND column_name IN ('has_health_insurance', 'has_private_insurance')
UNION ALL
SELECT '18. cot patients.email dung kieu de nhan OTP',
       string_agg(column_name || '/' || data_type || '(' || character_maximum_length || ')/' || is_nullable, ',' ORDER BY column_name),
       count(*) = 1 AND bool_and(data_type = 'character varying' AND character_maximum_length = 200 AND is_nullable = 'YES')
FROM information_schema.columns
WHERE table_name = 'patients' AND column_name = 'email';

SELECT * FROM gate;

DO $$
DECLARE n integer;
BEGIN
    SELECT count(*) INTO n FROM gate WHERE NOT ok;
    IF n > 0 THEN
        RAISE EXCEPTION '% / 18 check cau truc khong dat - xem bang ket qua o tren', n;
    END IF;
    RAISE NOTICE 'gate cau truc: 18/18 dat';
END $$;
