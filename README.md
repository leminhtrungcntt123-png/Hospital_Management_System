# HIS (Hospital Information System)
Hệ thống hỗ trợ đặt lịch khám và quản lý hồ sơ bệnh án (.NET 10, ASP.NET Core MVC, PostgreSQL).

## 1. Yêu cầu môi trường
- .NET SDK 10.0+
- PostgreSQL 16+ (cài cục bộ hoặc qua Docker)
- Docker (tùy chọn: dùng nếu muốn chạy `docker compose` hoặc chạy integration tests với Testcontainers)

## 2. Cách chạy ứng dụng cục bộ

### Bước 1: Khởi động cơ sở dữ liệu
Nếu bạn chưa có sẵn PostgreSQL trên máy, dùng lệnh:
```bash
cp .env.example .env
docker compose up -d
```
Nếu đã có PostgreSQL cài trực tiếp trên máy, chỉ cần tạo database tên `his_db`.

### Bước 2: Thiết lập User Secrets
Cấu hình chuỗi kết nối và mật khẩu tài khoản mặc định (Development):
```bash
dotnet user-secrets set "ConnectionStrings:HisDb" "Host=localhost;Port=5432;Database=his_db;Username=postgres;Password=YOUR_PASSWORD" --project src/HIS.Web
dotnet user-secrets set "Seed:Enabled" "true" --project src/HIS.Web
dotnet user-secrets set "Seed:DefaultPassword" "Admin@123456" --project src/HIS.Web
```

### Bước 3: Áp dụng Migration & Chạy ứng dụng
```bash
dotnet ef database update --project src/HIS.DAL --startup-project src/HIS.Web
dotnet run --project src/HIS.Web
```

## 3. Quy ước Git & Jira
- Nhánh: `feature/HIS-<số>-<mô-tả>`
- Commit: `HIS-<số>: <nội dung>`

