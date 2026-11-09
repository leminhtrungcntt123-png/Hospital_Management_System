SET client_encoding = 'UTF8';
INSERT INTO services (name, service_type_id, price, is_active)
SELECT v.name, v.service_type_id, v.price, true
FROM (VALUES
    ('Siêu âm tuyến giáp', 3, 120000.00),
    ('Tế bào học dịch các tổn thương dạng nang', 2, 165000.00),
    ('Định lượng TSH (Thyroid Stimulating hormone) [Máu]', 2, 87000.00),
    ('Định lượng FT4 (Free Thyroxine) [Máu]', 2, 124000.00)
) AS v(name, service_type_id, price)
WHERE NOT EXISTS (SELECT 1 FROM services s WHERE s.name = v.name);

