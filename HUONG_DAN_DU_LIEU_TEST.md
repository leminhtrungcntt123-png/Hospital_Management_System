# HƯỚNG DẪN DỮ LIỆU DÙNG CHUNG ĐỂ TEST HỆ THỐNG (HIS)

Tài liệu này giúp các thành viên trong nhóm thống nhất nguồn dữ liệu test chuẩn mô phỏng bệnh viện đa khoa thực tế: 10 chuyên khoa, 16 bác sĩ, 30+ bệnh nhân, đầy đủ lịch khám, hàng chờ và không lo mất mật khẩu kết nối cơ sở dữ liệu khi `git pull`.

---

## 1. Cấu hình chuỗi kết nối PostgreSQL riêng cho máy cá nhân (Chỉ làm 1 lần)

Nếu mật khẩu PostgreSQL trên máy của bạn khác với `123456789` (mật khẩu mặc định trong `appsettings.json`):

1. Vào thư mục `src/HIS.Web/`.
2. Tạo file `appsettings.local.json` (hoặc copy từ `appsettings.local.json.template` và đổi tên).
3. Điền mật khẩu PostgreSQL của máy bạn:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Host=localhost;Port=5432;Database=hospital_his;Username=postgres;Password=MAT_KHAU_CUA_BAN"
     }
   }
   ```
> 💡 **Lưu ý:** File `*.local.json` đã được cấu hình trong `.gitignore`, do đó **hoàn toàn không bao giờ bị đẩy lên Git** và **không bao giờ bị ghi đè** khi bạn chạy lệnh `git pull`.

---

## 2. Dữ liệu tự động sinh khi chạy ứng dụng

Mỗi khi bạn chạy `HIS.Web` ở môi trường `Development` (bấm **F5** trong Visual Studio hoặc gõ `dotnet run --project src/HIS.Web`):
1. Hệ thống tự động kiểm tra và chạy `Migrate` database.
2. Hệ thống tự động kiểm tra dữ liệu:
   - Nếu chưa có dữ liệu: Tự động nạp toàn bộ danh mục 10 khoa, 16 bác sĩ, 30+ bệnh nhân và lịch trực.
   - Nếu đã có dữ liệu: Tự động kiểm tra và **sinh bổ sung khung giờ khám và lịch trực cho ngày HÔM NAY** và 3 ngày tới (giúp bạn luôn có lịch trống để test tiếp nhận và đặt lịch).

---

## 3. Danh mục 10 Chuyên khoa & 16 Bác sĩ khám bệnh

Tất cả các tài khoản nhân sự và bác sĩ đều dùng chung một mật khẩu duy nhất là: **`Demo@123`**

| Khoa chuyên môn | Phòng khám | Bác sĩ phụ trách | Tên đăng nhập | Chuyên khoa sâu |
| :--- | :--- | :--- | :--- | :--- |
| **Khoa Tim mạch** | P101 | BS.CKI Trần Thu Trang | `bs.tranthutrang` | Tim mạch lâm sàng |
| **Khoa Tim mạch** | P102 | PGS.TS.BS Nguyễn Văn An | `bs.nguyenvanan` | Tim mạch can thiệp |
| **Khoa Nội tổng hợp** | P201 | ThS.BS Lê Minh Châu | `bs.leminhchau` | Tiêu hóa - Gan mật |
| **Khoa Nội tổng hợp** | P202 | BS.CKI Trương Mỹ Hạnh | `bs.truongmyhanh` | Nội tiết & Đái tháo đường |
| **Khoa Nội tổng hợp** | P201 | BS.CKI Mai Văn Phúc | `bs.maivanphuc` | Nội hô hấp |
| **Khoa Nhi** | P301 | BS.CKI Phạm Thùy Dương | `bs.phamthuyduong` | Nhi tổng quát |
| **Khoa Nhi** | P302 | ThS.BS Vũ Hải Yến | `bs.vuhaiyen` | Nhi sơ sinh & Dinh dưỡng |
| **Khoa Ngoại tổng quát** | P401 | BS.CKII Đỗ Văn Hùng | `bs.dovanhung` | Ngoại tiêu hóa |
| **Khoa Ngoại tổng quát** | P402 | BS.CKI Chu Quốc Nam | `bs.chuquocnam` | Ngoại chấn thương |
| **Khoa Sản - Phụ khoa** | P501 | BS.CKI Nguyễn Thị Lan | `bs.nguyenthilan` | Sản phụ khoa |
| **Khoa Sản - Phụ khoa** | P502 | ThS.BS Hoàng Đức Minh | `bs.hoangducminh` | Sản khoa & Hiếm muộn |
| **Khoa Tai Mũi Họng** | P601 | BS.CKII Bùi Quang Huy | `bs.buiquanghuy` | Tai Mũi Họng |
| **Khoa Mắt (Nhãn khoa)**| P701 | BS.CKI Ngô Thanh Tùng | `bs.ngothanhtung` | Nhãn khoa & Khúc xạ |
| **Khoa Da liễu** | P801 | ThS.BS Đặng Thu Hương | `bs.dangthuhuong` | Da liễu & Thẩm mỹ da |
| **Khoa Răng Hàm Mặt** | P901 | BS.CKI Võ Quốc Dũng | `bs.voquocdung` | Nha khoa tổng quát |
| **Khoa Cơ Xương Khớp** | P1001 | BS.CKII Phan Thành Long | `bs.phanthanhlong` | Cơ Xương Khớp |

---

## 4. Tài khoản Nhân sự & Tiếp đón (Mật khẩu: `Demo@123`)

* **Quản trị hệ thống:** `admin` / `Demo@123`
* **Nhân viên Tiếp đón Quầy:** `receptionist` hoặc `td.tranthivan` / `Demo@123` (truy cập `/Staff/Reception`)
* **Điều dưỡng:** `nurse`, `dd.lethihuong`, `dd.phamquangminh` / `Demo@123`

---

## 5. Dữ liệu bệnh nhân mẫu để test tra cứu & tiếp nhận tại Quầy

Khi ở màn hình Tiếp nhận (`/Staff/Reception`) -> Chọn tab **"Đăng ký vãng lai"**:

### Nhóm 1: Bệnh nhân Người lớn ĐÃ CÓ HỒ SƠ (Test tra cứu nhanh bằng CCCD)
* Nhập `001190012345` -> Tự động điền: **Nguyễn Minh Anh** (Nam, 1990, SĐT: `0912345678`, BHYT: `DN4010123456789`).
* Nhập `001185009876` -> Tự động điền: **Trần Thu Hà** (Nữ, 1985, SĐT: `0987654321`, BHYT: `DN4010987654321`).
* Nhập `001155001234` -> Tự động điền: **Lê Hoàng Nam** (Nam, 1955, BHYT Hưu trí: `HT3010012345678`).
* Nhập `001152011223` -> Tự động điền: **Vũ Đức Long** (Nam, 1952, BHYT: `HT3010112233344`).
* Nhập `001148021234` -> Tự động điền: **Dương Thị Tuyết** (Nữ, 1948).
* Nhập `001192002345` -> Tự động điền: **Đỗ Thanh Bình** (Nam, 1992).
* Nhập `001196007890` -> Tự động điền: **Bùi Thị Mai** (Nữ, 1996).

### Nhóm 2: Bệnh nhân Người lớn CHƯA CÓ HỒ SƠ
* Nhập số CCCD 12 số bất kỳ chưa từng có trong hệ thống (ví dụ: `079201888999`) -> Hệ thống hiện popup SweetAlert2 báo: *"Số CCCD này hiện chưa có hồ sơ trong hệ thống"* và cho phép nhập hồ sơ mới.

### Nhóm 3: Bệnh nhân Trẻ em (< 15 tuổi)
* Bấm chọn nút **"Trẻ em (< 15 tuổi)"**.
* Ô CCCD của trẻ tự động ẩn đi.
* Nhập thông tin trẻ và thông tin Người giám hộ (Họ tên bố/mẹ, Mối quan hệ, SĐT và **Email phụ huynh**).
* *Bệnh nhân trẻ em mẫu đã có trong hệ thống:*
  * Bé **Trần Bảo Nam** (5 tuổi - Mẹ: Trần Thu Hà - `0987654321`).
  * Bé **Trần Mai Chi** (2 tuổi - Mẹ: Trần Thu Hà - `0987654321`).
  * Bé **Phạm Ngọc Lan** (7 tuổi - Mẹ: Phạm Thị Mai - `0938111222`).
  * Bé **Đặng Quang Huy** (9 tuổi - Bố: Đặng Văn Long - `0909555666`).
  * Bé **Trương Gia Bảo** (5 tuổi - Mẹ: Lê Thị Bích - `0912555888`).

---

## 6. Dữ liệu Hàng chờ & Lịch hẹn mẫu có sẵn hôm nay

Khi bạn mở trang Tiếp đón (`/Staff/Reception`):
* **Tab "Lịch hẹn từ Web":** Có sẵn **10 lịch hẹn đặt trước** từ Web (chờ tiếp đón vào viện) với đa dạng chuyên khoa và lý do khám (Tim mạch, Sản, Nhi, Cơ xương khớp, Mắt...).
* **Tab "Hàng chờ đã tiếp nhận":** Có sẵn **12 bệnh nhân đang chờ** với số thứ tự từ **`A001` đến `A012`**, trải đều trước các phòng khám (P101, P201, P301, P401, P501...).
* **Phòng khám:** Có sẵn 4 ca đang khám (`IN_PROGRESS`) và 4 ca đã hoàn tất (`COMPLETED`).

---

## 7. Cách làm mới (Reset) lại toàn bộ dữ liệu test

Nếu trong lúc test dữ liệu bị rối và bạn muốn đưa toàn bộ database về lại trạng thái chuẩn ban đầu của nhóm:
* **Cách 1 (Qua PowerShell / cURL trên máy dev):**
  ```powershell
  Invoke-RestMethod -Uri "https://localhost:7024/dev/seed-fresh" -Method Post -Headers @{"X-Confirm-Reset"="reset-dev-data"} -SkipCertificateCheck
  ```
  hoặc với cURL:
  ```bash
  curl -k -X POST "https://localhost:7024/dev/seed-fresh" -H "X-Confirm-Reset: reset-dev-data"
  ```
* **Cách 2 (Khởi động với cờ lệnh):**
  ```bash
  dotnet run --project src/HIS.Web -- --seed-fresh
  ```
