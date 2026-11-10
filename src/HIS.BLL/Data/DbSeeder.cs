using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Data;

/// <summary>
/// Trình nạp dữ liệu mẫu chuẩn mô phỏng bệnh viện đa khoa thực tế.
/// Dữ liệu phong phú gồm: 10 chuyên khoa, 20+ phòng khám, 16 bác sĩ chuyên khoa,
/// 30+ bệnh nhân (người lớn, trẻ em, người cao tuổi), thuốc thiết yếu, dịch vụ kỹ thuật,
/// và hàng chục lượt khám mẫu ở các trạng thái trong ngày hôm nay và các ngày tới.
/// </summary>
public static class DbSeeder
{
    public const string DefaultPassword = "Demo@123";
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    private static DateTime GetNow() => DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);

    public static async Task SeedAsync(AppDbContext context, bool forceFresh = false)
    {
        await _seedLock.WaitAsync();
        try
        {
            // 1. Áp dụng Migration hoặc Baseline an toàn (tránh lỗi xung đột cột đã có sẵn)
            if (context.Database.IsRelational())
            {
                await SafeMigrateOrBaselineAsync(context);
            }

            // 2. Nếu không yêu cầu dọn sạch, kiểm tra xem đã có dữ liệu nền đầy đủ chưa
            if (!forceFresh)
            {
                var isFullySeeded = await context.Accounts.AnyAsync(a => a.Username == "admin")
                    && await context.AppointmentSlots.AnyAsync()
                    && await context.Appointments.AnyAsync();

                if (isFullySeeded)
                {
                    await EnsureTodaySlotsAndSchedulesAsync(context);
                    return;
                }
            }

            // 3. Nếu trên relational DB (PostgreSQL), bọc toàn bộ chu kỳ seed/reset trong một Transaction
            // để đảm bảo tính nguyên tử (atomic): nếu có lỗi giữa chừng, CSDL sẽ rollback sạch sẽ.
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
            if (context.Database.IsRelational())
            {
                transaction = await context.Database.BeginTransactionAsync();
            }

            try
            {
                // Nếu forceFresh: dọn dẹp dữ liệu động cũ
                if (forceFresh)
                {
                    await ClearDynamicDataAsync(context);
                }

                // 4. Master Data
                await SeedRolesAsync(context);
                await SeedAppointmentStatusesAsync(context);
                await SeedDepartmentsAndRoomsAsync(context);
                await SeedInsurancePlansAsync(context);
                await SeedServicesAsync(context);
                await SeedMedicationsAsync(context);

                // 5. Nhân sự & Tài khoản
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword, workFactor: 10);
                await SeedStaffAndDoctorsAsync(context, passwordHash);

                // 6. Bệnh nhân mẫu (Người lớn, Trẻ em, Người già, Thẻ BHYT)
                await SeedPatientsAsync(context, passwordHash);

                // 7. Lịch trực Bác sĩ (Tất cả 7 ngày trong tuần)
                await SeedDoctorSchedulesAsync(context);

                // 8. Khung giờ khám (Slots) cho Hôm nay và 3 ngày tới
                await SeedAppointmentSlotsAsync(context);

                // 9. Lượt khám mẫu phong phú mô phỏng bệnh viện đang hoạt động
                await SeedSampleAppointmentsAsync(context);

                if (transaction != null)
                {
                    await transaction.CommitAsync();
                }
            }
            catch
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync();
                }
                throw;
            }
            finally
            {
                if (transaction != null)
                {
                    await transaction.DisposeAsync();
                }
            }
        }
        finally
        {
            _seedLock.Release();
        }
    }

    private static async Task ClearDynamicDataAsync(AppDbContext context)
    {
        if (context.Database.IsRelational())
        {
            var sql = @"
                TRUNCATE TABLE 
                    prescription_items,
                    prescriptions,
                    lab_results,
                    lab_orders,
                    medical_records,
                    vital_signs,
                    invoice_items,
                    payments,
                    invoices,
                    notifications,
                    duty_shifts,
                    schedule_exceptions,
                    appointment_status_history,
                    appointments,
                    appointment_slots,
                    doctor_schedules,
                    account_patient_profiles,
                    insurance_cards,
                    patients,
                    department_heads,
                    doctors,
                    nurses,
                    accounts
                CASCADE;
            ";
            await context.Database.ExecuteSqlRawAsync(sql);
        }
        else
        {
            context.PrescriptionItems.RemoveRange(context.PrescriptionItems);
            context.Prescriptions.RemoveRange(context.Prescriptions);
            context.LabResults.RemoveRange(context.LabResults);
            context.LabOrders.RemoveRange(context.LabOrders);
            context.MedicalRecords.RemoveRange(context.MedicalRecords);
            context.VitalSigns.RemoveRange(context.VitalSigns);
            context.InvoiceItems.RemoveRange(context.InvoiceItems);
            context.Payments.RemoveRange(context.Payments);
            context.Invoices.RemoveRange(context.Invoices);
            context.Notifications.RemoveRange(context.Notifications);
            context.DutyShifts.RemoveRange(context.DutyShifts);
            context.ScheduleExceptions.RemoveRange(context.ScheduleExceptions);
            context.AppointmentStatusHistories.RemoveRange(context.AppointmentStatusHistories);
            context.Appointments.RemoveRange(context.Appointments);
            context.AppointmentSlots.RemoveRange(context.AppointmentSlots);
            context.DoctorSchedules.RemoveRange(context.DoctorSchedules);
            context.AccountPatientProfiles.RemoveRange(context.AccountPatientProfiles);
            context.InsuranceCards.RemoveRange(context.InsuranceCards);
            context.Patients.RemoveRange(context.Patients);
            context.DepartmentHeads.RemoveRange(context.DepartmentHeads);
            context.Doctors.RemoveRange(context.Doctors);
            context.Nurses.RemoveRange(context.Nurses);
            context.Accounts.RemoveRange(context.Accounts);
            await context.SaveChangesAsync();
        }
    }

    private static async Task SeedRolesAsync(AppDbContext context)
    {
        var roles = new[]
        {
            new Role { Id = 1, Code = "ADMIN", Name = "Quản trị hệ thống", Description = "Quản trị toàn bộ hệ thống" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ", Description = "Bác sĩ khám và chỉ định điều trị" },
            new Role { Id = 3, Code = "NURSE", Name = "Điều dưỡng", Description = "Điều dưỡng phòng khám" },
            new Role { Id = 4, Code = "RECEPTIONIST", Name = "Tiếp đón", Description = "Nhân viên tiếp đón và điều phối hàng chờ" },
            new Role { Id = 5, Code = "CASHIER", Name = "Thu ngân", Description = "Nhân viên thu viện phí" },
            new Role { Id = 6, Code = "DEPT_HEAD", Name = "Trưởng khoa", Description = "Trưởng khoa chuyên môn" },
            new Role { Id = 7, Code = "PATIENT", Name = "Bệnh nhân", Description = "Tài khoản bệnh nhân / người đặt lịch" }
        };

        foreach (var r in roles)
        {
            if (!await context.Roles.AnyAsync(x => x.Code == r.Code))
            {
                context.Roles.Add(r);
            }
        }
        await context.SaveChangesAsync();
    }

    private static async Task SeedAppointmentStatusesAsync(AppDbContext context)
    {
        var statuses = new[]
        {
            new AppointmentStatus { Id = 1, Code = "PENDING", Name = "Chờ xác nhận", SortOrder = 1, IsTerminal = false },
            new AppointmentStatus { Id = 2, Code = "CONFIRMED", Name = "Đã xác nhận", SortOrder = 2, IsTerminal = false },
            new AppointmentStatus { Id = 3, Code = "CHECKED_IN", Name = "Đã tiếp nhận", SortOrder = 3, IsTerminal = false },
            new AppointmentStatus { Id = 4, Code = "READY_FOR_DOCTOR", Name = "Chờ vào khám", SortOrder = 4, IsTerminal = false },
            new AppointmentStatus { Id = 5, Code = "IN_PROGRESS", Name = "Đang khám", SortOrder = 5, IsTerminal = false },
            new AppointmentStatus { Id = 6, Code = "COMPLETED", Name = "Đã hoàn tất", SortOrder = 6, IsTerminal = true },
            new AppointmentStatus { Id = 7, Code = "CANCELLED", Name = "Đã hủy", SortOrder = 7, IsTerminal = true },
            new AppointmentStatus { Id = 8, Code = "NO_SHOW", Name = "Vắng mặt", SortOrder = 8, IsTerminal = true },
            new AppointmentStatus { Id = 9, Code = "EXPIRED", Name = "Hết hạn", SortOrder = 9, IsTerminal = true },
            new AppointmentStatus { Id = 10, Code = "RESCHEDULE_REQUIRED", Name = "Cần dời lịch", SortOrder = 10, IsTerminal = false }
        };

        foreach (var s in statuses)
        {
            if (!await context.AppointmentStatuses.AnyAsync(x => x.Code == s.Code))
            {
                context.AppointmentStatuses.Add(s);
            }
        }
        await context.SaveChangesAsync();
    }

    private static async Task SeedDepartmentsAndRoomsAsync(AppDbContext context)
    {
        var depts = new[]
        {
            new {
                Name = "Khoa Tim mạch",
                Desc = "Chẩn đoán và can thiệp điều trị bệnh lý tim mạch, tăng huyết áp, rối loạn nhịp",
                Rooms = new[] { ("Phòng khám Tim mạch 101", "P101"), ("Phòng khám Tim mạch 102", "P102"), ("Phòng Siêu âm tim 103", "P103") }
            },
            new {
                Name = "Khoa Nội tổng hợp",
                Desc = "Khám và điều trị các bệnh lý nội khoa, tiêu hóa, hô hấp, nội tiết và chuyển hóa",
                Rooms = new[] { ("Phòng khám Nội tổng hợp 201", "P201"), ("Phòng khám Nội tiết - Đái tháo đường 202", "P202") }
            },
            new {
                Name = "Khoa Nhi",
                Desc = "Chăm sóc, theo dõi phát triển và điều trị chuyên sâu bệnh lý trẻ sơ sinh và trẻ nhỏ",
                Rooms = new[] { ("Phòng khám Nhi 301", "P301"), ("Phòng khám Nhi sơ sinh 302", "P302"), ("Phòng Tiêm chủng & Dinh dưỡng 303", "P303") }
            },
            new {
                Name = "Khoa Ngoại tổng quát",
                Desc = "Khám, tư vấn và phẫu thuật các bệnh lý ngoại khoa ổ bụng, tiêu hóa, gan mật",
                Rooms = new[] { ("Phòng khám Ngoại 401", "P401"), ("Phòng Tiểu phẫu - Xử trí vết thương 402", "P402") }
            },
            new {
                Name = "Khoa Sản - Phụ khoa",
                Desc = "Khám thai định kỳ, sàng lọc trước sinh, chăm sóc thai kỳ và điều trị phụ khoa",
                Rooms = new[] { ("Phòng khám Sản 501", "P501"), ("Phòng khám Phụ khoa 502", "P502") }
            },
            new {
                Name = "Khoa Tai Mũi Họng",
                Desc = "Khám, nội soi và điều trị các bệnh lý tai mũi họng người lớn và trẻ em",
                Rooms = new[] { ("Phòng khám Tai Mũi Họng 601", "P601"), ("Phòng Nội soi TMH 602", "P602") }
            },
            new {
                Name = "Khoa Mắt (Nhãn khoa)",
                Desc = "Đo khúc xạ, khám và điều trị các bệnh lý kết giác mạc, đục thủy tinh thể, glaucoma",
                Rooms = new[] { ("Phòng khám Mắt 701", "P701"), ("Phòng Đo khúc xạ & Thị lực 702", "P702") }
            },
            new {
                Name = "Khoa Da liễu",
                Desc = "Khám, tư vấn và chăm sóc điều trị các bệnh lý về da, dị ứng và thẩm mỹ da liễu",
                Rooms = new[] { ("Phòng khám Da liễu 801", "P801") }
            },
            new {
                Name = "Khoa Răng Hàm Mặt",
                Desc = "Khám, điều trị nha khoa tổng quát, phục hình răng và phẫu thuật răng hàm mặt",
                Rooms = new[] { ("Phòng khám Răng Hàm Mặt 901", "P901") }
            },
            new {
                Name = "Khoa Cơ Xương Khớp",
                Desc = "Chẩn đoán và điều trị thoái hóa khớp, thoát vị đĩa đệm, loãng xương và gout",
                Rooms = new[] { ("Phòng khám Cơ Xương Khớp 1001", "P1001") }
            }
        };

        foreach (var d in depts)
        {
            var dept = await context.Departments.FirstOrDefaultAsync(x => x.Name == d.Name);
            if (dept == null)
            {
                dept = new Department { Name = d.Name, Description = d.Desc };
                context.Departments.Add(dept);
                await context.SaveChangesAsync();
            }

            foreach (var r in d.Rooms)
            {
                if (!await context.ClinicRooms.AnyAsync(x => x.DepartmentId == dept.Id && x.RoomNumber == r.Item2))
                {
                    context.ClinicRooms.Add(new ClinicRoom
                    {
                        DepartmentId = dept.Id,
                        RoomName = r.Item1,
                        RoomNumber = r.Item2
                    });
                }
            }
        }
        await context.SaveChangesAsync();
    }

    private static async Task SeedInsurancePlansAsync(AppDbContext context)
    {
        if (!await context.InsurancePlans.AnyAsync())
        {
            context.InsurancePlans.AddRange(
                new InsurancePlan { Name = "BHYT Hưu trí / Hộ nghèo (Mức hưởng 100%)", CoveragePct = 100m },
                new InsurancePlan { Name = "BHYT Trẻ em dưới 6 tuổi (Mức hưởng 100%)", CoveragePct = 100m },
                new InsurancePlan { Name = "BHYT Doanh nghiệp / Người lao động (Mức hưởng 80%)", CoveragePct = 80m },
                new InsurancePlan { Name = "BHYT Hộ gia đình / Học sinh sinh viên (Mức hưởng 80%)", CoveragePct = 80m }
            );
            await context.SaveChangesAsync();
        }
    }

    private static async Task SeedServicesAsync(AppDbContext context)
    {
        if (!await context.ServiceTypes.AnyAsync())
        {
            context.ServiceTypes.AddRange(
                new ServiceType { Id = 1, Code = "EXAM", Name = "Khám bệnh chuyên khoa" },
                new ServiceType { Id = 2, Code = "LAB", Name = "Xét nghiệm y học" },
                new ServiceType { Id = 3, Code = "IMAGING", Name = "Chẩn đoán hình ảnh" },
                new ServiceType { Id = 4, Code = "FUNCTIONAL", Name = "Thăm dò chức năng & Thủ thuật" }
            );
            await context.SaveChangesAsync();
        }

        var services = new[]
        {
            // Khám
            ("Khám chuyên khoa thường", 1, 150000m),
            ("Khám chuyên gia / Phó Giáo sư", 1, 350000m),
            ("Khám cấp cứu ban ngày", 1, 200000m),
            // Chẩn đoán hình ảnh
            ("Siêu âm Doppler tim màu", 3, 250000m),
            ("Siêu âm ổ bụng tổng quát", 3, 180000m),
            ("Siêu âm thai 4D tầm soát dị tật", 3, 350000m),
            ("Siêu âm tuyến giáp", 3, 150000m),
            ("Chụp X-quang ngực thẳng kỹ thuật số", 3, 120000m),
            ("Chụp X-quang khớp gối (2 tư thế)", 3, 140000m),
            ("Chụp CT-Scanner sọ não không cản quang", 3, 950000m),
            ("Chụp MRI cột sống thắt lưng", 3, 1800000m),
            // Xét nghiệm
            ("Tổng phân tích tế bào máu ngoại vi (24 thông số)", 2, 95000m),
            ("Định lượng Glucose máu", 2, 45000m),
            ("Định lượng HbA1c (Máu)", 2, 135000m),
            ("Đo hoạt độ men gan (AST / ALT)", 2, 80000m),
            ("Định lượng Creatinin / Ure máu", 2, 75000m),
            ("Bộ mỡ máu (Cholesterol, Triglycerid, HDL-C, LDL-C)", 2, 160000m),
            ("Định lượng Acid Uric máu (Gout)", 2, 60000m),
            ("Tổng phân tích nước tiểu 10 thông số", 2, 55000m),
            // Thăm dò chức năng & Nội soi
            ("Điện tâm đồ (ECG) 12 chuyển đạo", 4, 80000m),
            ("Đo chức năng hô hấp", 4, 150000m),
            ("Nội soi Tai Mũi Họng ống mềm", 4, 250000m),
            ("Nội soi thực quản - dạ dày không đau", 4, 900000m),
            ("Đo khúc xạ mắt bằng máy tự động", 4, 60000m)
        };

        foreach (var s in services)
        {
            if (!await context.Services.AnyAsync(x => x.Name == s.Item1))
            {
                context.Services.Add(new Service
                {
                    Name = s.Item1,
                    ServiceTypeId = s.Item2,
                    Price = s.Item3,
                    IsActive = true
                });
            }
        }
        await context.SaveChangesAsync();
    }

    private static async Task SeedMedicationsAsync(AppDbContext context)
    {
        var meds = new[]
        {
            ("Paracetamol 500mg", "Paracetamol", "500mg", "Viên", 1500m),
            ("Augmentin 1g", "Amoxicillin + Clavulanic acid", "1000mg", "Viên", 18500m),
            ("Amlodipine 5mg", "Amlodipine", "5mg", "Viên", 2500m),
            ("Losartan 50mg", "Losartan potassium", "50mg", "Viên", 4200m),
            ("Metformin 500mg", "Metformin hydrochloride", "500mg", "Viên", 1800m),
            ("Omeprazole 20mg", "Omeprazole", "20mg", "Viên", 3200m),
            ("Gaviscon Suspension", "Natri alginate + Natri bicarbonat", "10ml", "Gói", 8500m),
            ("Cefixime 200mg", "Cefixime", "200mg", "Viên", 9000m),
            ("Ibuprofen 400mg", "Ibuprofen", "400mg", "Viên", 2200m),
            ("Loratadine 10mg", "Loratadine", "10mg", "Viên", 2000m),
            ("Alpha Choay", "Chymotrypsine", "4.2mg", "Viên", 3500m),
            ("Oresol 245", "Glucose + Điện giải", "4.1g", "Gói", 4000m),
            ("Vitamin C 500mg", "Acid ascorbic", "500mg", "Viên", 1200m),
            ("Berberin 100mg", "Berberin clorid", "100mg", "Viên", 1000m),
            ("Atorvastatin 20mg", "Atorvastatin", "20mg", "Viên", 7500m)
        };

        foreach (var m in meds)
        {
            if (!await context.Medications.AnyAsync(x => x.Name == m.Item1))
            {
                context.Medications.Add(new Medication
                {
                    Name = m.Item1,
                    ActiveIngredient = m.Item2,
                    Strength = m.Item3,
                    Unit = m.Item4,
                    Price = m.Item5,
                    IsActive = true
                });
            }
        }
        await context.SaveChangesAsync();
    }

    private static async Task SeedStaffAndDoctorsAsync(AppDbContext context, string passwordHash)
    {
        var now = GetNow();

        var adminRole = await context.Roles.FirstAsync(r => r.Code == "ADMIN");
        var doctorRole = await context.Roles.FirstAsync(r => r.Code == "DOCTOR");
        var nurseRole = await context.Roles.FirstAsync(r => r.Code == "NURSE");
        var receptionistRole = await context.Roles.FirstAsync(r => r.Code == "RECEPTIONIST");

        // 1. Quản trị viên
        if (!await context.Accounts.AnyAsync(a => a.Username == "admin"))
        {
            context.Accounts.Add(new Account
            {
                Username = "admin",
                Email = "admin@hospital.vn",
                PasswordHash = passwordHash,
                RoleId = adminRole.Id,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        // 2. Nhân viên tiếp đón & điều dưỡng
        var receptionStaff = new[]
        {
            ("receptionist", "reception@hospital.vn", receptionistRole.Id, "ĐD. Trần Thị Vân"),
            ("td.tranthivan", "van.reception@hospital.vn", receptionistRole.Id, "ĐD. Trần Thị Vân"),
            ("nurse", "nurse@hospital.vn", nurseRole.Id, "ĐD. Nguyễn Thị Mai"),
            ("dd.lethihuong", "huong.nurse@hospital.vn", nurseRole.Id, "ĐD. Lê Thị Hương"),
            ("dd.phamquangminh", "minh.nurse@hospital.vn", nurseRole.Id, "ĐD. Phạm Quang Minh")
        };

        foreach (var rs in receptionStaff)
        {
            if (!await context.Accounts.AnyAsync(a => a.Username == rs.Item1))
            {
                var acc = new Account
                {
                    Username = rs.Item1,
                    Email = rs.Item2,
                    PasswordHash = passwordHash,
                    RoleId = rs.Item3,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                context.Accounts.Add(acc);
                await context.SaveChangesAsync();

                var tmDept = await context.Departments.FirstOrDefaultAsync(d => d.Name == "Khoa Tim mạch");
                context.Nurses.Add(new Nurse
                {
                    AccountId = acc.Id,
                    FullName = rs.Item4,
                    DepartmentId = tmDept?.Id
                });
            }
        }
        await context.SaveChangesAsync();

        // 3. Danh sách 16 Bác sĩ đa khoa & chuyên khoa
        var doctorsInfo = new[]
        {
            ("bs.tranthutrang",  "trang.doctor@hospital.vn",  "Khoa Tim mạch",         "BS.CKI Trần Thu Trang",      "Tim mạch",                 "CKI-2020-001", 15),
            ("bs.nguyenvanan",   "an.doctor@hospital.vn",     "Khoa Tim mạch",         "PGS.TS.BS Nguyễn Văn An",    "Tim mạch can thiệp",       "CKII-2015-002", 20),
            ("bs.leminhchau",    "chau.doctor@hospital.vn",    "Khoa Nội tổng hợp",     "ThS.BS Lê Minh Châu",        "Nội tiêu hóa - Gan mật",   "CKI-2018-003", 15),
            ("bs.truongmyhanh",  "hanh.doctor@hospital.vn",    "Khoa Nội tổng hợp",     "BS.CKI Trương Mỹ Hạnh",      "Nội tiết & Tiểu đường",    "CKI-2019-011", 15),
            ("bs.phamthuyduong", "duong.doctor@hospital.vn",   "Khoa Nhi",              "BS.CKI Phạm Thùy Dương",     "Nhi tổng quát",            "CKI-2019-004", 15),
            ("bs.vuhaiyen",      "yen.doctor@hospital.vn",     "Khoa Nhi",              "ThS.BS Vũ Hải Yến",          "Nhi sơ sinh & Dinh dưỡng", "CKI-2021-012", 15),
            ("bs.dovanhung",     "hung.doctor@hospital.vn",    "Khoa Ngoại tổng quát",  "BS.CKII Đỗ Văn Hùng",        "Ngoại tiêu hóa - Gan mật", "CKII-2014-005", 20),
            ("bs.chuquocnam",    "nam.doctor@hospital.vn",     "Khoa Ngoại tổng quát",  "BS.CKI Chu Quốc Nam",        "Ngoại chấn thương",        "CKI-2018-013", 15),
            ("bs.nguyenthilan",  "lan.doctor@hospital.vn",     "Khoa Sản - Phụ khoa",   "BS.CKI Nguyễn Thị Lan",      "Sản phụ khoa",             "CKI-2017-006", 15),
            ("bs.hoangducminh",  "minh.doctor@hospital.vn",    "Khoa Sản - Phụ khoa",   "ThS.BS Hoàng Đức Minh",      "Sản khoa & Hiếm muộn",     "CKI-2020-014", 20),
            ("bs.buiquanghuy",   "huy.doctor@hospital.vn",     "Khoa Tai Mũi Họng",     "BS.CKII Bùi Quang Huy",      "Tai Mũi Họng",             "CKII-2016-007", 15),
            ("bs.ngothanhtung",  "tung.doctor@hospital.vn",    "Khoa Mắt (Nhãn khoa)",  "BS.CKI Ngô Thanh Tùng",      "Nhãn khoa & Khúc xạ",      "CKI-2019-008", 15),
            ("bs.dangthuhuong",  "huong.doctor@hospital.vn",   "Khoa Da liễu",          "ThS.BS Đặng Thu Hương",      "Da liễu & Thẩm mỹ",        "CKI-2020-009", 15),
            ("bs.voquocdung",    "dung.doctor@hospital.vn",    "Khoa Răng Hàm Mặt",     "BS.CKI Võ Quốc Dũng",        "Nha khoa tổng quát",       "CKI-2018-010", 20),
            ("bs.phanthanhlong", "long.doctor@hospital.vn",    "Khoa Cơ Xương Khớp",    "BS.CKII Phan Thành Long",    "Cơ Xương Khớp",            "CKII-2015-015", 20),
            ("bs.maivanphuc",    "phuc.doctor@hospital.vn",    "Khoa Nội tổng hợp",     "BS.CKI Mai Văn Phúc",        "Nội hô hấp",               "CKI-2019-016", 15)
        };

        foreach (var doc in doctorsInfo)
        {
            var acc = await context.Accounts.FirstOrDefaultAsync(a => a.Username == doc.Item1);
            if (acc == null)
            {
                acc = new Account
                {
                    Username = doc.Item1,
                    Email = doc.Item2,
                    PasswordHash = passwordHash,
                    RoleId = doctorRole.Id,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                context.Accounts.Add(acc);
                await context.SaveChangesAsync();
            }

            var dept = await context.Departments.FirstAsync(d => d.Name == doc.Item3);
            if (!await context.Doctors.AnyAsync(d => d.AccountId == acc.Id))
            {
                context.Doctors.Add(new Doctor
                {
                    AccountId = acc.Id,
                    DepartmentId = dept.Id,
                    FullName = doc.Item4,
                    Specialty = doc.Item5,
                    LicenseNumber = doc.Item6,
                    AvgConsultationMinutes = doc.Item7
                });
            }
        }
        await context.SaveChangesAsync();
    }

    private static async Task SeedPatientsAsync(AppDbContext context, string passwordHash)
    {
        var now = GetNow();
        var patientRole = await context.Roles.FirstAsync(r => r.Code == "PATIENT");
        var planStandard = await context.InsurancePlans.FirstOrDefaultAsync(p => p.CoveragePct == 80m) ?? new InsurancePlan { Name = "BHYT", CoveragePct = 80m };
        var planChild = await context.InsurancePlans.FirstOrDefaultAsync(p => p.CoveragePct == 100m) ?? planStandard;

        // Danh sách tài khoản bệnh nhân Web demo
        var webAccounts = new[]
        {
            ("patient01", "patient01@example.com"),
            ("patient02", "family@example.com"),
            ("patient03", "nguyenhongan@gmail.com")
        };

        foreach (var wa in webAccounts)
        {
            if (!await context.Accounts.AnyAsync(a => a.Username == wa.Item1))
            {
                context.Accounts.Add(new Account
                {
                    Username = wa.Item1,
                    Email = wa.Item2,
                    PasswordHash = passwordHash,
                    RoleId = patientRole.Id,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }
        await context.SaveChangesAsync();

        var accPatient01 = await context.Accounts.FirstAsync(a => a.Username == "patient01");
        var accPatient02 = await context.Accounts.FirstAsync(a => a.Username == "patient02");
        var accPatient03 = await context.Accounts.FirstAsync(a => a.Username == "patient03");

        // 30+ Bệnh nhân phong phú (Người lớn, Trẻ em, Người già)
        var patientsData = new[]
        {
            // [0..3]: Nhóm tài khoản Patient01 & Patient02 & Patient03
            ("Nguyễn Minh Anh",  new DateOnly(1990, 4, 12), Gender.Male,   "001190012345", "0912345678", "patient01@example.com", "12 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh", "DN4010123456789", accPatient01.Id, PatientRelationship.Self),
            ("Trần Thu Hà",      new DateOnly(1985, 9, 23), Gender.Female, "001185009876", "0987654321", "family@example.com",    "45 Lê Lợi, Quận 1, TP. Hồ Chí Minh",     "DN4010987654321", accPatient02.Id, PatientRelationship.Self),
            ("Trần Bảo Nam",     new DateOnly(2020, 3, 15), Gender.Male,   (string?)null,  "0987654321", "family@example.com",    "45 Lê Lợi, Quận 1, TP. Hồ Chí Minh",     "TE4010067890123", accPatient02.Id, PatientRelationship.Child),
            ("Trần Mai Chi",     new DateOnly(2023, 7, 10), Gender.Female, (string?)null,  "0987654321", "family@example.com",    "45 Lê Lợi, Quận 1, TP. Hồ Chí Minh",     "TE4010067890999", accPatient02.Id, PatientRelationship.Child),
            ("Nguyễn Hồng An",   new DateOnly(1995, 11, 5), Gender.Female, "001195007888", "0933555777", "nguyenhongan@gmail.com","78 Pasteur, Quận 1, TP. Hồ Chí Minh",    "DN4010088899911", accPatient03.Id, PatientRelationship.Self),

            // [Người lớn tuổi có BHYT hưu trí]
            ("Lê Hoàng Nam",     new DateOnly(1955, 1, 5),  Gender.Male,   "001155001234", "0905123456", "nam.le@example.com",    "88 Hai Bà Trưng, Quận 3, TP. Hồ Chí Minh", "HT3010012345678", (int?)null, PatientRelationship.Other),
            ("Vũ Đức Long",      new DateOnly(1952, 11, 30),Gender.Male,   "001152011223", "0903777888", "long.vu@example.com",   "156 Cách Mạng Tháng 8, Quận 10",         "HT3010112233344", (int?)null, PatientRelationship.Other),
            ("Dương Thị Tuyết",  new DateOnly(1948, 7, 4),  Gender.Female, "001148021234", "0917222333", "tuyet.duong@gmail.com", "302 Lê Văn Sỹ, Quận 3, TP. Hồ Chí Minh", "HT3010212345679", (int?)null, PatientRelationship.Other),
            ("Bùi Xuân Phúc",    new DateOnly(1950, 1, 18), Gender.Male,   "001150024567", "0914333222", "phuc.bui@gmail.com",   "51 Đinh Tiên Hoàng, Bình Thạnh",         "HT3010245678902", (int?)null, PatientRelationship.Other),
            ("Nguyễn Thị Bích",  new DateOnly(1958, 11, 11),Gender.Female, "001158015678", "0977111999", "bich.nguyen@yahoo.com","33 Nam Kỳ Khởi Nghĩa, Quận 1",          "HT3010156789013", (int?)null, PatientRelationship.Other),

            // [Người lớn đi làm & thanh niên]
            ("Đỗ Thanh Bình",    new DateOnly(1992, 2, 14), Gender.Male,   "001192002345", "0918222333", "binh.do@example.com",   "7 Phan Xích Long, Phú Nhuận",            "DN4010023455667", (int?)null, PatientRelationship.Other),
            ("Bùi Thị Mai",      new DateOnly(1996, 7, 8),  Gender.Female, "001196007890", "0945666777", "mai.bui@gmail.com",     "89 Nguyễn Trãi, Quận 5",                 "DN4010078901234", (int?)null, PatientRelationship.Other),
            ("Hoàng Văn Tú",     new DateOnly(1988, 3, 19), Gender.Male,   "001188008901", "0977444555", "tu.hoang@company.vn",   "31 Điện Biên Phủ, Bình Thạnh",           "DN4010089012345", (int?)null, PatientRelationship.Other),
            ("Ngô Thị Hồng",     new DateOnly(1994, 12, 2), Gender.Female, "001194009012", "0966333444", "hong.ngo@gmail.com",    "12 Nguyễn Kiệm, Gò Vấp",                 "DN4010090123456", (int?)null, PatientRelationship.Other),
            ("Trịnh Thị Hương",  new DateOnly(1999, 8, 9),  Gender.Female, "001199011234", "0938222444", "huong.trinh@gmail.com", "5 Lý Thường Kiệt, Tân Bình",             "DN4010112345678", (int?)null, PatientRelationship.Other),
            ("Phan Văn Khải",    new DateOnly(1975, 10, 10),Gender.Male,   "001175012345", "0913666777", "khai.phan@gmail.com",   "210 Trường Chinh, Tân Phú",              "DN4010123456780", (int?)null, PatientRelationship.Other),
            ("Lý Thị Kim",       new DateOnly(2001, 1, 25), Gender.Female, "001201013456", "0942888999", "kim.ly@student.edu.vn", "16 Hoàng Diệu, Quận 4",                 "HS4010134567891", (int?)null, PatientRelationship.Other),
            ("Cao Minh Quân",    new DateOnly(1983, 4, 3),  Gender.Male,   "001183014567", "0909333222", "quan.cao@techcorp.com", "102 Nguyễn Văn Linh, Quận 7",           "DN4010145678902", (int?)null, PatientRelationship.Other),
            ("Võ Thị Ánh",       new DateOnly(1987, 2, 28), Gender.Female, "001187017890", "0908777666", "anh.vo@gmail.com",      "68 Trần Hưng Đạo, Quận 5",               "DN4010178901235", (int?)null, PatientRelationship.Other),
            ("Đinh Công Thành",  new DateOnly(1993, 6, 15), Gender.Male,   "001193018901", "0939111444", "thanh.dinh@gmail.com",  "141 Nguyễn Thị Minh Khai, Quận 1",       "DN4010189012346", (int?)null, PatientRelationship.Other),
            ("Lâm Thanh Trúc",   new DateOnly(1997, 3, 7),  Gender.Female, "001197019012", "0968222111", "truc.lam@gmail.com",    "9 Ung Văn Khiêm, Bình Thạnh",            "DN4010190123457", (int?)null, PatientRelationship.Other),
            ("Trần Quốc Cường",  new DateOnly(1981, 5, 12), Gender.Male,   "001181022345", "0944777888", "cuong.tran@gmail.com",  "19 Bạch Đằng, Tân Bình",                 "DN4010223456780", (int?)null, PatientRelationship.Other),
            ("Nguyễn Hải Yến",   new DateOnly(2003, 10, 22),Gender.Female, "001203023456", "0909888777", "yen.nguyen@gmail.com",  "74 Phạm Văn Đồng, TP. Thủ Đức",          "HS4010234567891", (int?)null, PatientRelationship.Other),
            ("Lê Thu Trang",     new DateOnly(1978, 8, 14), Gender.Female, "001178001234", "0908112233", "trang.le@gmail.com",    "15 Pasteur, Quận 1",                     "DN4010780012345", (int?)null, PatientRelationship.Other),
            ("Hoàng Minh Trí",   new DateOnly(1970, 5, 20), Gender.Male,   "001170012345", "0918334455", "tri.hoang@gmail.com",   "28 Lê Duẩn, Quận 1",                     "DN4010700123456", (int?)null, PatientRelationship.Other),
            ("Phạm Quốc Bảo",    new DateOnly(2001, 12, 5), Gender.Male,   "001218006789", "0938556677", "bao.pham@gmail.com",    "42 Nguyễn Đình Chiểu, Quận 3",           "HS4010218006789", (int?)null, PatientRelationship.Other),

            // [Trẻ em khác < 15 tuổi]
            ("Phạm Ngọc Lan",    new DateOnly(2018, 6, 17), Gender.Female, (string?)null,  "0938111222", "lan.pham.mom@gmail.com","23 Võ Văn Tần, Quận 3",                  "TE4010067890456", (int?)null, PatientRelationship.Child),
            ("Đặng Quang Huy",   new DateOnly(2015, 5, 21), Gender.Male,   (string?)null,  "0909555666", "huy.dang.dad@gmail.com","78 Quang Trung, Gò Vấp",                 "TE4010101234567", (int?)null, PatientRelationship.Child),
            ("Trương Gia Bảo",   new DateOnly(2019, 9, 5),  Gender.Male,   (string?)null,  "0912555888", "giabao.mom@gmail.com",  "55 Dương Quảng Hàm, Gò Vấp",             "TE4010167890124", (int?)null, PatientRelationship.Child),
            ("Hồ Nhật Minh",     new DateOnly(2016, 12, 19),Gender.Male,   (string?)null,  "0903666555", "nhatminh.parent@gmail.com", "27 Nguyễn Xí, Bình Thạnh",          "TE4010201234568", (int?)null, PatientRelationship.Child)
        };

        foreach (var pData in patientsData)
        {
            var p = await context.Patients.FirstOrDefaultAsync(x =>
                (pData.Item4 != null && x.NationalId == pData.Item4) ||
                (pData.Item4 == null && x.FullName == pData.Item1 && x.DateOfBirth == pData.Item2));

            if (p == null)
            {
                p = new Patient
                {
                    FullName = pData.Item1,
                    DateOfBirth = pData.Item2,
                    Gender = pData.Item3,
                    NationalId = pData.Item4,
                    PhoneNumber = pData.Item5,
                    Email = pData.Item6,
                    Address = pData.Item7,
                    CreatedAt = now
                };
                context.Patients.Add(p);
                await context.SaveChangesAsync();

                // Liên kết tài khoản web (nếu có)
                if (pData.Item9.HasValue)
                {
                    context.AccountPatientProfiles.Add(new AccountPatientProfile
                    {
                        AccountId = pData.Item9.Value,
                        PatientId = p.Id,
                        Relationship = pData.Item10,
                        Status = LinkStatus.Active,
                        CanViewRecords = true,
                        CreatedAt = now
                    });
                }

                // Thẻ BHYT
                var targetPlan = (p.DateOfBirth > DateOnly.FromDateTime(DateTime.Today.AddYears(-6))) ? planChild : planStandard;
                if (!await context.InsuranceCards.AnyAsync(ic => ic.CardNumber == pData.Item8))
                {
                    context.InsuranceCards.Add(new InsuranceCard
                    {
                        PatientId = p.Id,
                        CardNumber = pData.Item8,
                        InsurancePlanId = targetPlan.Id,
                        ValidFrom = new DateOnly(2025, 1, 1),
                        ValidTo = new DateOnly(2027, 12, 31),
                        IsActive = true,
                        CreatedAt = now
                    });
                }
            }
        }
        await context.SaveChangesAsync();
    }

    private static ClinicRoom GetRoomForDoctor(Doctor doc, List<ClinicRoom> rooms, List<Doctor> allDoctors)
    {
        var deptRooms = rooms.Where(r => r.DepartmentId == doc.DepartmentId).OrderBy(r => r.RoomNumber).ToList();
        if (deptRooms.Count == 0) return rooms.First();
        var docsInDept = allDoctors.Where(d => d.DepartmentId == doc.DepartmentId).OrderBy(d => d.Id).ToList();
        int docIndex = docsInDept.FindIndex(d => d.Id == doc.Id);
        if (docIndex < 0) docIndex = 0;
        return deptRooms[docIndex % deptRooms.Count];
    }

    private static async Task SeedDoctorSchedulesAsync(AppDbContext context)
    {
        var doctors = await context.Doctors.Include(d => d.Department).ToListAsync();
        var rooms = await context.ClinicRooms.ToListAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var effectiveFrom = today.AddDays(-60);
        var effectiveTo = today.AddYears(1);

        foreach (var doc in doctors)
        {
            var room = GetRoomForDoctor(doc, rooms, doctors);

            for (int dow = 0; dow <= 6; dow++)
            {
                var dayOfWeek = (DayOfWeek)dow;

                // Ca sáng: 07:30 - 11:30
                if (!await context.DoctorSchedules.AnyAsync(s => s.DoctorId == doc.Id && s.DayOfWeek == dayOfWeek && s.Shift == ExamShift.Morning))
                {
                    context.DoctorSchedules.Add(new DoctorSchedule
                    {
                        DoctorId = doc.Id,
                        RoomId = room.Id,
                        DayOfWeek = dayOfWeek,
                        Shift = ExamShift.Morning,
                        StartTime = new TimeOnly(7, 30),
                        EndTime = new TimeOnly(11, 30),
                        SlotDurationMinutes = 30,
                        OnlineRatioPct = 60,
                        EffectiveFrom = effectiveFrom,
                        EffectiveTo = effectiveTo,
                        IsActive = true
                    });
                }

                // Ca chiều: 13:30 - 16:30
                if (!await context.DoctorSchedules.AnyAsync(s => s.DoctorId == doc.Id && s.DayOfWeek == dayOfWeek && s.Shift == ExamShift.Afternoon))
                {
                    context.DoctorSchedules.Add(new DoctorSchedule
                    {
                        DoctorId = doc.Id,
                        RoomId = room.Id,
                        DayOfWeek = dayOfWeek,
                        Shift = ExamShift.Afternoon,
                        StartTime = new TimeOnly(13, 30),
                        EndTime = new TimeOnly(16, 30),
                        SlotDurationMinutes = 30,
                        OnlineRatioPct = 60,
                        EffectiveFrom = effectiveFrom,
                        EffectiveTo = effectiveTo,
                        IsActive = true
                    });
                }
            }
        }
        await context.SaveChangesAsync();
    }

    private static async Task SeedAppointmentSlotsAsync(AppDbContext context)
    {
        var doctors = await context.Doctors.ToListAsync();
        var rooms = await context.ClinicRooms.ToListAsync();
        var today = DateTime.Today;

        for (int dayOffset = 0; dayOffset <= 3; dayOffset++)
        {
            var date = today.AddDays(dayOffset);

            var slotTimes = new[]
            {
                new TimeOnly(8, 0), new TimeOnly(8, 30), new TimeOnly(9, 0), new TimeOnly(9, 30),
                new TimeOnly(10, 0), new TimeOnly(10, 30),
                new TimeOnly(13, 30), new TimeOnly(14, 0), new TimeOnly(14, 30), new TimeOnly(15, 0),
                new TimeOnly(15, 30), new TimeOnly(16, 0)
            };

            foreach (var doc in doctors)
            {
                var room = GetRoomForDoctor(doc, rooms, doctors);

                foreach (var st in slotTimes)
                {
                    var startDateTime = DateTime.SpecifyKind(date.Add(st.ToTimeSpan()), DateTimeKind.Unspecified);
                    var endDateTime = DateTime.SpecifyKind(startDateTime.AddMinutes(30), DateTimeKind.Unspecified);

                    if (!await context.AppointmentSlots.AnyAsync(s => s.DoctorId == doc.Id && s.StartTime == startDateTime))
                    {
                        context.AppointmentSlots.Add(new AppointmentSlot
                        {
                            DoctorId = doc.Id,
                            RoomId = room.Id,
                            StartTime = startDateTime,
                            EndTime = endDateTime,
                            Capacity = 4,
                            OnlineQuota = 2,
                            BookedCount = 0,
                            OnlineBooked = 0,
                            Status = SlotStatus.Available,
                            Version = 0
                        });
                    }
                }
            }
        }
        await context.SaveChangesAsync();
    }

    private static async Task SeedSampleAppointmentsAsync(AppDbContext context)
    {
        var now = GetNow();
        var today = DateTime.Today;
        var codePrefix = "LK" + today.ToString("yyMMdd");

        var patients = await context.Patients.ToListAsync();
        var doctors = await context.Doctors.Include(d => d.Department).ToListAsync();

        var stPending = await context.AppointmentStatuses.FirstAsync(s => s.Code == "PENDING");
        var stConfirmed = await context.AppointmentStatuses.FirstAsync(s => s.Code == "CONFIRMED");
        var stCheckedIn = await context.AppointmentStatuses.FirstAsync(s => s.Code == "CHECKED_IN");
        var stInProgress = await context.AppointmentStatuses.FirstAsync(s => s.Code == "IN_PROGRESS");
        var stCompleted = await context.AppointmentStatuses.FirstAsync(s => s.Code == "COMPLETED");
        var stCancelled = await context.AppointmentStatuses.FirstAsync(s => s.Code == "CANCELLED");

        // Danh sách mẫu lượt khám trong ngày hôm nay: 30+ ca trải đều các trạng thái
        var appointmentsPlan = new[]
        {
            // === TAB 1: LỊCH HẸN TỪ WEB CHỜ TIẾP ĐÓN (CONFIRMED / PENDING) ===
            (1,  "001190012345", "CKI-2020-001", 8,  30, stConfirmed,  BookingChannel.Web,    VisitType.Scheduled, 1, (string?)null, "Đau thắt ngực khi gắng sức, hồi hộp", 1),
            (2,  "001155001234", "CKII-2015-002",9,  0,  stConfirmed,  BookingChannel.Web,    VisitType.Scheduled, 0, (string?)null, "Tái khám tăng huyết áp, kiểm tra thuốc", 1),
            (3,  "001195007888", "CKI-2017-006", 9,  30, stConfirmed,  BookingChannel.Web,    VisitType.Scheduled, 0, (string?)null, "Khám thai định kỳ 22 tuần, siêu âm 4D", 1),
            (4,  "001188008901", "CKI-2018-003", 9,  30, stPending,    BookingChannel.Web,    VisitType.Scheduled, 0, (string?)null, "Đau thượng vị, ợ hơi, khó tiêu", 2),
            (5,  "001152011223", "CKII-2015-015",10, 0,  stConfirmed,  BookingChannel.Web,    VisitType.Scheduled, 1, (string?)null, "Đau nhức khớp gối hai bên, cứng khớp buổi sáng", 1),
            (6,  "001199011234", "CKI-2019-008", 10, 0,  stPending,    BookingChannel.Web,    VisitType.Scheduled, 0, (string?)null, "Mỏi mắt, nhìn mờ, đo độ cận", 2),
            (7,  "001183014567", "CKII-2016-007",10, 30, stConfirmed,  BookingChannel.Web,    VisitType.Scheduled, 0, (string?)null, "Đau rát họng, nuốt vướng, khàn tiếng", 1),
            (8,  "001197019012", "CKI-2020-009", 10, 30, stConfirmed,  BookingChannel.Web,    VisitType.Scheduled, 0, (string?)null, "Dị ứng mẩn ngứa da vùng cánh tay", 1),
            (9,  "001175012345", "CKII-2014-005",14, 0,  stConfirmed,  BookingChannel.Web,    VisitType.Scheduled, 0, (string?)null, "Tư vấn phẫu thuật trĩ, đau rát hậu môn", 1),
            (10, "001181022345", "CKI-2018-010", 14, 30, stPending,    BookingChannel.Web,    VisitType.Scheduled, 0, (string?)null, "Nhổ răng khôn mọc lệch, đau nhức hàm", 2),

            // === TAB 2: HÀNG CHỜ ĐÃ TIẾP NHẬN (CHECKED_IN - SỐ THỨ TỰ A001..A012) ===
            (11, "001185009876", "CKI-2018-003", 8,  0,  stCheckedIn,  BookingChannel.OnSite, VisitType.WalkIn,    0, "A001", "Ho kéo dài, rát cổ họng, sốt nhẹ", (int?)null),
            (12, "Trần Bảo Nam", "CKI-2019-004", 8,  0,  stCheckedIn,  BookingChannel.OnSite, VisitType.WalkIn,    2, "A002", "Sốt cao 39 độ, ho quấy khóc [Mẹ: Trần Thu Hà - 0987654321]", (int?)null),
            (13, "001148021234", "CKII-2015-002",8,  30, stCheckedIn,  BookingChannel.OnSite, VisitType.WalkIn,    1, "A003", "Khó thở khi nằm, phù nhẹ mu bàn chân", (int?)null),
            (14, "Phạm Ngọc Lan","CKI-2021-012", 8,  30, stCheckedIn,  BookingChannel.OnSite, VisitType.WalkIn,    2, "A004", "Bé biếng ăn, sốt phát ban nhẹ [Mẹ: Phạm Thị Mai - 0938111222]", (int?)null),
            (15, "001192002345", "CKI-2020-001", 9,  0,  stCheckedIn,  BookingChannel.Web,    VisitType.Scheduled, 0, "A005", "Đau ngực trái từng cơn sau vận động", 1),
            (16, "001194009012", "CKI-2017-006", 9,  0,  stCheckedIn,  BookingChannel.OnSite, VisitType.WalkIn,    0, "A006", "Đau bụng dưới, chậm kinh 1 tuần", (int?)null),
            (17, "001187017890", "CKI-2019-011", 9,  30, stCheckedIn,  BookingChannel.OnSite, VisitType.WalkIn,    0, "A007", "Xét nghiệm đường huyết định kỳ, khát nước nhiều", (int?)null),
            (18, "Đặng Quang Huy","CKI-2019-004",9,  30, stCheckedIn,  BookingChannel.OnSite, VisitType.WalkIn,    2, "A008", "Viêm mũi dị ứng, hắt hơi liên tục [Bố: Đặng Văn Long - 0909555666]", (int?)null),
            (19, "001193018901", "CKII-2014-005",10, 0,  stCheckedIn,  BookingChannel.OnSite, VisitType.WalkIn,    0, "A009", "Đau bụng vùng hố chậu phải âm ỉ", (int?)null),
            (20, "001150024567", "CKII-2015-015",10, 0,  stCheckedIn,  BookingChannel.OnSite, VisitType.WalkIn,    1, "A010", "Đau thắt lưng lan xuống chân trái, tê bì ngón chân", (int?)null),
            (21, "Trương Gia Bảo","CKI-2021-012",10, 30, stCheckedIn,  BookingChannel.OnSite, VisitType.WalkIn,    2, "A011", "Khám sức khỏe tổng quát trẻ 5 tuổi", (int?)null),
            (22, "001201013456", "CKII-2016-007",10, 30, stCheckedIn,  BookingChannel.OnSite, VisitType.WalkIn,    0, "A012", "Ù tai, giảm thính lực tai phải sau khi bơi", (int?)null),

            // === ĐANG KHÁM (IN_PROGRESS - TRONG PHÒNG KHÁM VỚI BÁC SĨ) ===
            (23, "001196007890", "CKI-2020-001", 8,  0,  stInProgress, BookingChannel.OnSite, VisitType.WalkIn,    0, "A013", "Hồi hộp, nhịp tim nhanh 105ck/phút", (int?)null),
            (24, "Hồ Nhật Minh", "CKI-2019-004", 8,  30, stInProgress, BookingChannel.OnSite, VisitType.WalkIn,    2, "A014", "Viêm amidan hốc mủ, sốt cao", (int?)null),
            (25, "001158015678", "CKI-2018-003", 9,  0,  stInProgress, BookingChannel.Web,    VisitType.Scheduled, 1, "A015", "Tái khám viêm loét dạ dày HP dương tính", 1),
            (26, "001203023456", "CKI-2019-008", 9,  30, stInProgress, BookingChannel.OnSite, VisitType.WalkIn,    0, "A016", "Đỏ mắt, chảy nước mắt sống, nhiều ghèn", (int?)null),

            // === ĐÃ HOÀN TẤT (COMPLETED - ĐÃ CÓ KẾT QUẢ KHÁM) ===
            (27, "001178001234", "CKII-2015-002",8,  0,  stCompleted,  BookingChannel.Web,    VisitType.Scheduled, 0, "A017", "Đo điện tim, tư vấn điều trị ngoại trú", 1),
            (28, "Trần Mai Chi", "CKI-2021-012", 8,  0,  stCompleted,  BookingChannel.OnSite, VisitType.WalkIn,    2, "A018", "Tiêm chủng vắc-xin 6 trong 1 mũi 2", (int?)null),
            (29, "001190012345", "CKI-2018-010", 8,  30, stCompleted,  BookingChannel.OnSite, VisitType.WalkIn,    0, "A019", "Cạo vôi răng, đánh bóng hai hàm", (int?)null),
            (30, "001185009876", "CKI-2020-009", 8,  30, stCompleted,  BookingChannel.Web,    VisitType.Scheduled, 0, "A020", "Khám da liễu, kê đơn thuốc bôi viêm da tiếp xúc", 1),

            // === ĐÃ HỦY (CANCELLED) ===
            (31, "001170012345", "CKI-2020-001", 10, 0,  stCancelled,  BookingChannel.Web,    VisitType.Scheduled, 0, (string?)null, "Bận việc đột xuất xin hủy lịch", 1),
            (32, "001218006789", "CKI-2019-004", 10, 30, stCancelled,  BookingChannel.Web,    VisitType.Scheduled, 2, (string?)null, "Bệnh nhân chuyển viện tuyến trên", 1)
        };

        foreach (var item in appointmentsPlan)
        {
            var apptCode = codePrefix + item.Item1.ToString("D3");
            if (await context.Appointments.AnyAsync(a => a.AppointmentCode == apptCode))
            {
                continue;
            }

            // Tìm bệnh nhân theo CCCD hoặc Tên trẻ em
            var patient = patients.FirstOrDefault(p =>
                (!string.IsNullOrEmpty(item.Item2) && p.NationalId == item.Item2) ||
                (p.NationalId == null && p.FullName == item.Item2));

            // Tìm bác sĩ theo giấy phép
            var doc = doctors.FirstOrDefault(d => d.LicenseNumber == item.Item3);

            if (patient == null || doc == null)
            {
                Console.WriteLine($"[WARN DbSeeder] Bỏ qua lịch khám mẫu {apptCode}: không tìm thấy bệnh nhân ({item.Item2}) hoặc bác sĩ ({item.Item3})");
                continue;
            }

            // Tìm slot tương ứng trong ngày
            var slot = await context.AppointmentSlots.FirstOrDefaultAsync(s =>
                s.DoctorId == doc.Id &&
                s.StartTime.Date == today &&
                s.StartTime.Hour == item.Item4 &&
                s.StartTime.Minute == item.Item5);

            var createdTime = DateTime.SpecifyKind(today.AddHours(item.Item4).AddMinutes(item.Item5).AddHours(-1), DateTimeKind.Unspecified);
            if (createdTime > now) createdTime = now.AddMinutes(-10);

            var appt = new Appointment
            {
                AppointmentCode = apptCode,
                PatientId = patient.Id,
                DoctorId = doc.Id,
                DepartmentId = doc.DepartmentId,
                RoomId = slot?.RoomId,
                SlotId = slot?.Id,
                StatusId = item.Item6.Id,
                BookingChannel = item.Item7,
                VisitType = item.Item8,
                PriorityLevel = item.Item9,
                QueueNumber = item.Item10,
                Reason = item.Item11,
                SlotPosition = item.Item12,
                HasHealthInsurance = true,
                CheckedInAt = (item.Item6 == stCheckedIn || item.Item6 == stInProgress || item.Item6 == stCompleted) ? createdTime.AddMinutes(20) : null,
                CreatedAt = createdTime,
                UpdatedAt = createdTime
            };

            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            // Lịch sử trạng thái
            context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
            {
                AppointmentId = appt.Id,
                OldStatusId = null,
                NewStatusId = item.Item6.Id,
                ChangedAt = createdTime
            });

            // Tăng số lượng đã đặt của slot
            if (slot != null)
            {
                slot.BookedCount += 1;
                if (item.Item7 == BookingChannel.Web)
                {
                    slot.OnlineBooked += 1;
                }
            }
        }

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Đảm bảo hôm nay và 3 ngày tới luôn có Slot khám và Lịch trực đầy đủ.
    /// </summary>
    private static async Task EnsureTodaySlotsAndSchedulesAsync(AppDbContext context)
    {
        var today = DateTime.Today;
        var hasSlotsToday = await context.AppointmentSlots.AnyAsync(s => s.StartTime.Date == today);
        if (!hasSlotsToday)
        {
            await SeedAppointmentSlotsAsync(context);
        }

        var todayDateOnly = DateOnly.FromDateTime(today);
        var hasValidSchedule = await context.DoctorSchedules.AnyAsync(s => s.EffectiveFrom <= todayDateOnly && (!s.EffectiveTo.HasValue || s.EffectiveTo.Value >= todayDateOnly));
        if (!hasValidSchedule)
        {
            await SeedDoctorSchedulesAsync(context);
        }
    }

    /// <summary>
    /// Áp dụng migration hoặc baseline an toàn, tránh văng lỗi khi cột/bảng đã được tạo từ script trước đó.
    /// </summary>
    private static async Task SafeMigrateOrBaselineAsync(AppDbContext context)
    {
        try
        {
            // Kiểm tra và đồng bộ baseline trước
            await BaselineMigrationsHistoryAsync(context);

            var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count != 0)
            {
                await context.Database.MigrateAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SafeMigrateOrBaselineAsync] Lưu ý khi chạy migration tự động: {ex.Message}");
            try
            {
                await BaselineMigrationsHistoryAsync(context);
            }
            catch (Exception retryEx)
            {
                Console.WriteLine($"[SafeMigrateOrBaselineAsync] Baseline retry error: {retryEx.Message}");
            }
        }
    }

    private static async Task BaselineMigrationsHistoryAsync(AppDbContext context)
    {
        var sql = @"
CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY (migration_id)
);

DO $$
DECLARE
    history_empty boolean;
BEGIN
    SELECT NOT EXISTS (SELECT 1 FROM ""__EFMigrationsHistory"") INTO history_empty;

    -- Các migration cơ sở ban đầu chỉ chèn khi __EFMigrationsHistory hoàn toàn rỗng VÀ bảng accounts đã tồn tại sẵn
    -- (tức là database cũ được tạo bằng script SQL thủ công, không có EF quản lý từ đầu)
    IF history_empty AND EXISTS (
        SELECT 1 FROM information_schema.tables 
        WHERE table_schema = 'public' AND table_name = 'accounts'
    ) THEN
        INSERT INTO ""__EFMigrationsHistory"" (migration_id, product_version)
        VALUES
            ('20261015010838_InitialCreate', '10.0.13'),
            ('20261103165420_AlignToGroupSpec', '10.0.13'),
            ('20261103173448_SanityCheck', '10.0.13'),
            ('20261104144910_QueuePositionNumbering', '10.0.13')
        ON CONFLICT (migration_id) DO NOTHING;
    END IF;

    -- Nếu cột has_health_insurance đã tồn tại, đánh dấu migration là đã chạy
    IF EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'appointments' AND column_name = 'has_health_insurance'
    ) THEN
        INSERT INTO ""__EFMigrationsHistory"" (migration_id, product_version)
        VALUES ('20261105131631_AppointmentInsuranceDeclaration', '10.0.13')
        ON CONFLICT (migration_id) DO NOTHING;
    END IF;

    -- Nếu cột email của patients đã tồn tại, đánh dấu migration là đã chạy
    IF EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'patients' AND column_name = 'email'
    ) THEN
        INSERT INTO ""__EFMigrationsHistory"" (migration_id, product_version)
        VALUES ('20261105141229_PatientEmail', '10.0.13')
        ON CONFLICT (migration_id) DO NOTHING;
    END IF;
END $$;
";
        await context.Database.ExecuteSqlRawAsync(sql);
    }
}
