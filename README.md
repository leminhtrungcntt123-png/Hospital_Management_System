# HIS - Hospital Information System

Hệ thống hỗ trợ đặt lịch khám và quản lý hồ sơ bệnh án (Đồ án môn học: Lập trình trực quan & Lập trình Web).

## Cấu trúc Solution

- **HIS.sln**: Solution chứa tất cả các dự án.
- **src/HIS.Domain**: Định nghĩa các Entity nghiệp vụ (PascalCase, độc lập).
- **src/HIS.DAL**: Data Access Layer, Entity Framework Core 10, `AppDbContext`, Npgsql PostgreSQL (snake_case convention).
- **src/HIS.BLL**: Business Logic Layer, triển khai các Service dùng chung giữa WinForms và WebApi/Web.
- **src/HIS.WinForms**: Ứng dụng Desktop (Windows Forms App, .NET 10, Windows-only).
- **src/HIS.WebApi**: RESTful API (ASP.NET Core Web API, .NET 10, Swagger).
- **src/HIS.Web**: Portal bệnh nhân — đặt lịch, lịch khám (ASP.NET Core MVC, .NET 10).
- **tests/HIS.Tests**: xUnit + Moq. Unit test không cần DB; integration test cần PostgreSQL thật (xem mục Kiểm thử).

## Tài liệu

| File | Dùng để |
| --- | --- |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | sơ đồ project thật, quy tắc BẤT BIẾN giữa các tầng, DB/migration, test, CI |
| [docs/DECISIONS.md](docs/DECISIONS.md) | vì sao code lệch `init.md` ở một số chỗ — **đọc trước khi "sửa cho đúng spec"** |
| [docs/database/erd.md](docs/database/erd.md) | ERD 32 bảng + ràng buộc CHECK/index, sinh từ DB thật |
| [docs/database/schema-reference.sql](docs/database/schema-reference.sql) | DDL tham chiếu theo spec nhóm |

## Yêu cầu môi trường
- .NET 10 SDK
- PostgreSQL 16+ (hoặc Docker chạy Postgres)
- `dotnet-ef` 10.x cho migration: `dotnet tool install --global dotnet-ef --version 10.0.13`

## Hướng dẫn cài đặt & chạy Migration

1. Cập nhật chuỗi kết nối trong `appsettings.json` của `HIS.WinForms` hoặc `HIS.WebApi` (hoặc cấu hình User Secrets).
2. Chạy Migration cập nhật Database:
```bash
dotnet ef database update --project src/HIS.DAL --startup-project src/HIS.WinForms
```

## Kiểm thử

```bash
# Unit test (không cần database)
dotnet test tests/HIS.Tests/HIS.Tests.csproj --filter "Category!=Integration"

# Integration test (cần PostgreSQL thật; thiếu biến môi trường thì test TỰ BỎ QUA nên đừng chạy lướt)
export HIS_REHEARSAL_CONNECTION="Host=localhost;Port=5432;Database=his_rehearsal;Username=postgres;Password=< mật khẩu local >"
dotnet test tests/HIS.Tests/HIS.Tests.csproj --filter "Category=Integration"
```

### Tạo database rehearsal từ đầu

CẢNH báo: `dotnet ef database update` trên CSDL **trống** sẽ lỗi ở
`20261103165420_AlignToGroupSpec`, vì migration đó viết tay để PATCH CSDL `hospital_his` sẵn có của nhóm
(rename constraint, remap id, di trú dữ liệu) chứ không phải để dựng CSDL mới. Với CSDL mới, nạp
`docs/database/ci/schema.sql` (ảnh chụp schema sinh từ model, xem chú thích đầu file để sinh lại):

```bash
createdb his_rehearsal
psql -d his_rehearsal -v ON_ERROR_STOP=1 -f docs/database/ci/schema.sql        # khung + dữ liệu HasData
psql -d his_rehearsal -v ON_ERROR_STOP=1 -f docs/database/ci/demo_master.sql   # danh mục, tài khoản, hồ sơ
psql -d his_rehearsal -v ON_ERROR_STOP=1 -f docs/database/ci/demo_day.sql      # khung giờ & lịch hẹn hôm nay
psql -d his_rehearsal -v ON_ERROR_STOP=1 -f docs/database/ci/schema_checks.sql  # gate 16 kiểm tra cấu trúc
```

Trên Windows, `backup/db.sh <dbname> <file.sql>` chạy đúng các lệnh psql trên mà không in mật khẩu ra log,
còn `backup/ci_local.sh` lặp lại toàn bộ job `integration` của CI trên máy local.

### CI (.github/workflows/ci.yml)

Hai job chạy song song:

| Job | Runner | Nội dung |
| --- | --- | --- |
| `build` | windows-latest | restore + build cả solution (HIS.WinForms cần Windows) + unit test (`Category!=Integration`) |
| `integration` | ubuntu-latest + service `postgres:16` | nạp schema.sql, demo_master.sql, demo_day.sql → gate 16 kiểm tra → integration test (`Category=Integration`) → `dotnet ef migrations has-pending-model-changes` |

Job `integration` không bỏ qua im lặng: thiếu PostgreSQL thì step đỏ, và bước kiểm tra cuối báo lỗi khi
sửa entity/configuration mà quên tạo migration.

## Quy ước làm việc

- Nhánh: `feature/HIS-<số>-<mô-tả>`, `fix/HIS-<số>-<mô-tả>`; commit: `HIS-<số>: <nội dung>` khớp Jira.
- Không commit `appsettings.json` chứa mật khẩu thật, `backup/` (dump DB, script phụ trợ local) và các tệp `.dump`.
- `docs/database/ci/` là tệp được commit: schema ảnh chụp, seed demo, gate kiểm tra cấu trúc.

