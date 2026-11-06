using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HIS.Web.Data;

public static class DevDataSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");

        var enabled = config.GetValue<bool>("Seed:Enabled");
        if (!enabled)
        {
            return;
        }

        var defaultPassword = config.GetValue<string>("Seed:DefaultPassword") ?? "Password123!";
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(defaultPassword);

            // 1. TÀI KHOẢN ADMIN
            var adminAcc = await db.Accounts.FirstOrDefaultAsync(a => a.Username == "admin");
            if (adminAcc == null)
            {
                adminAcc = new Account
                {
                    Username = "admin",
                    Email = "admin@his.local",
                    PasswordHash = passwordHash,
                    RoleId = (int)RoleId.Admin,
                    IsActive = true
                };
                db.Accounts.Add(adminAcc);
                await db.SaveChangesAsync();
            }

            // 2. DANH MỤC KHOA PHÒNG
            var deptNoi = await db.Departments.FirstOrDefaultAsync(d => d.Name.Contains("Nội"));
            if (deptNoi == null)
            {
                deptNoi = new Department { Name = "Khoa Nội tổng quát", Description = "Khám và điều trị nội khoa" };
                db.Departments.Add(deptNoi);
                await db.SaveChangesAsync();
            }

            var deptNgoai = await db.Departments.FirstOrDefaultAsync(d => d.Name.Contains("Ngoại"));
            if (deptNgoai == null)
            {
                deptNgoai = new Department { Name = "Khoa Ngoại tổng quát", Description = "Khám và phẫu thuật ngoại khoa" };
                db.Departments.Add(deptNgoai);
                await db.SaveChangesAsync();
            }

            // Trưởng khoa
            var headAcc1 = await db.Accounts.FirstOrDefaultAsync(a => a.Username == "head_noi");
            if (headAcc1 == null)
            {
                headAcc1 = new Account { Username = "head_noi", Email = "head_noi@his.local", PasswordHash = passwordHash, RoleId = (int)RoleId.DepartmentHead };
                db.Accounts.Add(headAcc1);
                await db.SaveChangesAsync();

                db.DepartmentHeads.Add(new DepartmentHead { AccountId = headAcc1.Id, DepartmentId = deptNoi.Id, FullName = "Phạm Thu Hằng" });
                await db.SaveChangesAsync();
            }

            var headDoctor = await db.Doctors.FirstOrDefaultAsync(d => d.AccountId == headAcc1.Id);
            if (headDoctor == null)
            {
                headDoctor = new Doctor { AccountId = headAcc1.Id, DepartmentId = deptNoi.Id, FullName = "Phạm Thu Hằng", Specialty = "Nội khoa chuyên sâu", LicenseNumber = "CCHN-000" };
                db.Doctors.Add(headDoctor);
                await db.SaveChangesAsync();
            }

            // Phòng khám
            var room1 = await db.ClinicRooms.FirstOrDefaultAsync(r => r.RoomNumber == "P101");
            if (room1 == null)
            {
                room1 = new ClinicRoom { DepartmentId = deptNoi.Id, RoomName = "Phòng khám Nội 1", RoomNumber = "P101" };
                var room2 = new ClinicRoom { DepartmentId = deptNoi.Id, RoomName = "Phòng khám Nội 2", RoomNumber = "P102" };
                var room3 = new ClinicRoom { DepartmentId = deptNgoai.Id, RoomName = "Phòng khám Ngoại 1", RoomNumber = "P201" };
                db.ClinicRooms.AddRange(room1, room2, room3);
                await db.SaveChangesAsync();
            }

            var room102 = await db.ClinicRooms.FirstOrDefaultAsync(r => r.RoomNumber == "P102") ?? room1;
            var room201 = await db.ClinicRooms.FirstOrDefaultAsync(r => r.RoomNumber == "P201") ?? room1;

            // 3. BÁC SĨ & ĐIỀU DƯỠNG
            var docAcc1 = await db.Accounts.FirstOrDefaultAsync(a => a.Username == "bs_nam");
            if (docAcc1 == null)
            {
                docAcc1 = new Account { Username = "bs_nam", Email = "nam@his.local", PasswordHash = passwordHash, RoleId = (int)RoleId.Doctor };
                db.Accounts.Add(docAcc1);
                await db.SaveChangesAsync();
            }

            var doc1 = await db.Doctors.FirstOrDefaultAsync(d => d.AccountId == docAcc1.Id);
            if (doc1 == null)
            {
                doc1 = new Doctor { AccountId = docAcc1.Id, DepartmentId = deptNoi.Id, FullName = "BS. Nguyễn Hoàng Nam", Specialty = "Nội tổng quát", LicenseNumber = "CCHN-001" };
                db.Doctors.Add(doc1);
                await db.SaveChangesAsync();
            }

            var docAcc2 = await db.Accounts.FirstOrDefaultAsync(a => a.Username == "bs_huy");
            if (docAcc2 == null)
            {
                docAcc2 = new Account { Username = "bs_huy", Email = "huy@his.local", PasswordHash = passwordHash, RoleId = (int)RoleId.Doctor };
                db.Accounts.Add(docAcc2);
                await db.SaveChangesAsync();
            }

            var doc2 = await db.Doctors.FirstOrDefaultAsync(d => d.AccountId == docAcc2.Id);
            if (doc2 == null)
            {
                doc2 = new Doctor { AccountId = docAcc2.Id, DepartmentId = deptNoi.Id, FullName = "BS. Lê Quang Huy", Specialty = "Nội tim mạch", LicenseNumber = "CCHN-002" };
                db.Doctors.Add(doc2);
                await db.SaveChangesAsync();
            }

            var docAcc3 = await db.Accounts.FirstOrDefaultAsync(a => a.Username == "bs_thu");
            if (docAcc3 == null)
            {
                docAcc3 = new Account { Username = "bs_thu", Email = "thu@his.local", PasswordHash = passwordHash, RoleId = (int)RoleId.Doctor };
                db.Accounts.Add(docAcc3);
                await db.SaveChangesAsync();
            }

            var doc3 = await db.Doctors.FirstOrDefaultAsync(d => d.AccountId == docAcc3.Id);
            if (doc3 == null)
            {
                doc3 = new Doctor { AccountId = docAcc3.Id, DepartmentId = deptNoi.Id, FullName = "BS. Trần Minh Thư", Specialty = "Tim mạch", LicenseNumber = "CCHN-003" };
                db.Doctors.Add(doc3);
                await db.SaveChangesAsync();
            }

            var nurseAcc = await db.Accounts.FirstOrDefaultAsync(a => a.Username == "nurse_lan");
            if (nurseAcc == null)
            {
                nurseAcc = new Account { Username = "nurse_lan", Email = "lan@his.local", PasswordHash = passwordHash, RoleId = (int)RoleId.Nurse };
                db.Accounts.Add(nurseAcc);
                await db.SaveChangesAsync();
            }

            var nurse = await db.Nurses.FirstOrDefaultAsync(n => n.AccountId == nurseAcc.Id);
            if (nurse == null)
            {
                nurse = new Nurse { AccountId = nurseAcc.Id, DepartmentId = deptNoi.Id, FullName = "Điều dưỡng Hoàng Lan" };
                db.Nurses.Add(nurse);
                await db.SaveChangesAsync();
            }

            // 4. BỆNH NHÂN MẪU CHUẨN
            var pBao = await db.Patients.FirstOrDefaultAsync(p => p.FullName == "Trần Quốc Bảo");
            if (pBao == null)
            {
                pBao = new Patient { FullName = "Trần Quốc Bảo", DateOfBirth = new DateOnly(1984, 5, 10), Gender = Gender.Male, PhoneNumber = "0912001100", NationalId = "001084000001", Address = "Hà Nội" };
                db.Patients.Add(pBao);
            }

            var pTuan = await db.Patients.FirstOrDefaultAsync(p => p.FullName == "Phạm Minh Tuấn");
            if (pTuan == null)
            {
                pTuan = new Patient { FullName = "Phạm Minh Tuấn", DateOfBirth = new DateOnly(1988, 8, 20), Gender = Gender.Male, PhoneNumber = "0912002200", NationalId = "001088000002", Address = "TP. Hồ Chí Minh" };
                db.Patients.Add(pTuan);
            }

            var pHa = await db.Patients.FirstOrDefaultAsync(p => p.FullName == "Nguyễn Thanh Hà");
            if (pHa == null)
            {
                pHa = new Patient { FullName = "Nguyễn Thanh Hà", DateOfBirth = new DateOnly(1994, 6, 15), Gender = Gender.Female, PhoneNumber = "0901234567", NationalId = "001094000003", Address = "Quận 1, TP. Hồ Chí Minh" };
                db.Patients.Add(pHa);
            }

            var pMinh = await db.Patients.FirstOrDefaultAsync(p => p.FullName == "Nguyễn Văn Minh");
            if (pMinh == null)
            {
                pMinh = new Patient { FullName = "Nguyễn Văn Minh", DateOfBirth = new DateOnly(1962, 2, 16), Gender = Gender.Male, PhoneNumber = "0902223344", NationalId = "001062000004", Address = "Quận 3, TP. Hồ Chí Minh" };
                db.Patients.Add(pMinh);
            }

            var pNgoc = await db.Patients.FirstOrDefaultAsync(p => p.FullName == "Lê Thị Ngọc");
            if (pNgoc == null)
            {
                pNgoc = new Patient { FullName = "Lê Thị Ngọc", DateOfBirth = new DateOnly(1978, 9, 12), Gender = Gender.Female, PhoneNumber = "0923456789", NationalId = "001078000005", Address = "Quận 5, TP. Hồ Chí Minh" };
                db.Patients.Add(pNgoc);
            }

            var pKhang = await db.Patients.FirstOrDefaultAsync(p => p.FullName == "Đỗ Gia Khang");
            if (pKhang == null)
            {
                pKhang = new Patient { FullName = "Đỗ Gia Khang", DateOfBirth = new DateOnly(1982, 12, 3), Gender = Gender.Male, PhoneNumber = "0978901234", NationalId = "001082000006", Address = "Quận 10, TP. Hồ Chí Minh" };
                db.Patients.Add(pKhang);
            }
            await db.SaveChangesAsync();

            // 5. CẤU HÌNH LỊCH KHÁM TUẦN (DoctorSchedule)
            var scheduleStartDate = new DateOnly(2026, 1, 1);
            if (!await db.DoctorSchedules.AnyAsync(s => s.DoctorId == doc1.Id))
            {
                for (int day = 1; day <= 5; day++)
                {
                    db.DoctorSchedules.Add(new DoctorSchedule
                    {
                        DoctorId = doc1.Id,
                        RoomId = room1.Id,
                        DayOfWeek = (DayOfWeek)day,
                        Shift = ExamShift.Morning,
                        StartTime = new TimeOnly(7, 30),
                        EndTime = new TimeOnly(11, 30),
                        SlotDurationMinutes = 15,
                        MaxPatients = 40,
                        OnlineSlots = 28,
                        EffectiveFrom = scheduleStartDate
                    });
                    db.DoctorSchedules.Add(new DoctorSchedule
                    {
                        DoctorId = doc1.Id,
                        RoomId = room1.Id,
                        DayOfWeek = (DayOfWeek)day,
                        Shift = ExamShift.Afternoon,
                        StartTime = new TimeOnly(13, 0),
                        EndTime = new TimeOnly(16, 30),
                        SlotDurationMinutes = 15,
                        MaxPatients = 40,
                        OnlineSlots = 28,
                        EffectiveFrom = scheduleStartDate
                    });
                }
                await db.SaveChangesAsync();
            }

            if (!await db.DoctorSchedules.AnyAsync(s => s.DoctorId == doc2.Id))
            {
                for (int day = 1; day <= 5; day++)
                {
                    db.DoctorSchedules.Add(new DoctorSchedule
                    {
                        DoctorId = doc2.Id,
                        RoomId = room102.Id,
                        DayOfWeek = (DayOfWeek)day,
                        Shift = ExamShift.Morning,
                        StartTime = new TimeOnly(7, 30),
                        EndTime = new TimeOnly(11, 30),
                        SlotDurationMinutes = 15,
                        MaxPatients = 35,
                        OnlineSlots = 25,
                        EffectiveFrom = scheduleStartDate
                    });
                    db.DoctorSchedules.Add(new DoctorSchedule
                    {
                        DoctorId = doc2.Id,
                        RoomId = room102.Id,
                        DayOfWeek = (DayOfWeek)day,
                        Shift = ExamShift.Afternoon,
                        StartTime = new TimeOnly(13, 0),
                        EndTime = new TimeOnly(16, 30),
                        SlotDurationMinutes = 15,
                        MaxPatients = 35,
                        OnlineSlots = 25,
                        EffectiveFrom = scheduleStartDate
                    });
                }
                await db.SaveChangesAsync();
            }

            if (doc3 != null && !await db.DoctorSchedules.AnyAsync(s => s.DoctorId == doc3.Id))
            {
                for (int day = 1; day <= 5; day++)
                {
                    db.DoctorSchedules.Add(new DoctorSchedule
                    {
                        DoctorId = doc3.Id,
                        RoomId = room201.Id,
                        DayOfWeek = (DayOfWeek)day,
                        Shift = ExamShift.Morning,
                        StartTime = new TimeOnly(7, 30),
                        EndTime = new TimeOnly(11, 30),
                        SlotDurationMinutes = 15,
                        MaxPatients = 30,
                        OnlineSlots = 20,
                        EffectiveFrom = scheduleStartDate
                    });
                    db.DoctorSchedules.Add(new DoctorSchedule
                    {
                        DoctorId = doc3.Id,
                        RoomId = room201.Id,
                        DayOfWeek = (DayOfWeek)day,
                        Shift = ExamShift.Afternoon,
                        StartTime = new TimeOnly(13, 0),
                        EndTime = new TimeOnly(16, 30),
                        SlotDurationMinutes = 15,
                        MaxPatients = 30,
                        OnlineSlots = 20,
                        EffectiveFrom = scheduleStartDate
                    });
                }
                await db.SaveChangesAsync();
            }

            // 6. LƯỢT KHÁM HÀNG ĐỢI HÔM NAY (Cho Bác sĩ Nguyễn Hoàng Nam)
            var today = DateOnly.FromDateTime(DateTime.Today);
            var todayMorning = today.ToDateTime(new TimeOnly(8, 30));

            if (!await db.Appointments.AnyAsync(a => a.AppointmentCode == "BN0006" || a.AppointmentCode == "BN0007"))
            {
                var app1 = new Appointment
                {
                    AppointmentCode = "BN0006",
                    PatientId = pBao.Id,
                    DoctorId = doc1.Id,
                    DepartmentId = deptNoi.Id,
                    RoomId = room1.Id,
                    StatusId = (int)AppointmentStatusId.ReadyForDoctor,
                    BookingChannel = BookingChannel.Web,
                    VisitType = VisitType.Scheduled,
                    QueueNumber = "O01",
                    Reason = "Đau tức ngực, khó thở nhẹ khi gắng sức",
                    CreatedAt = todayMorning
                };

                var app2 = new Appointment
                {
                    AppointmentCode = "BN0007",
                    PatientId = pTuan.Id,
                    DoctorId = doc1.Id,
                    DepartmentId = deptNoi.Id,
                    RoomId = room1.Id,
                    StatusId = (int)AppointmentStatusId.CheckedIn,
                    BookingChannel = BookingChannel.OnSite,
                    VisitType = VisitType.WalkIn,
                    QueueNumber = "W02",
                    Reason = "Khám tổng quát định kỳ",
                    CreatedAt = todayMorning.AddMinutes(30)
                };

                db.Appointments.AddRange(app1, app2);
                await db.SaveChangesAsync();
            }

            // 7. BỆNH ÁN HOÀN TẤT TRƯỚC ĐÓ (MedicalRecord)
            if (!await db.MedicalRecords.AnyAsync())
            {
                var oldApp1 = new Appointment
                {
                    AppointmentCode = "BN0010",
                    PatientId = pKhang.Id,
                    DoctorId = doc2.Id,
                    DepartmentId = deptNoi.Id,
                    RoomId = room102.Id,
                    StatusId = (int)AppointmentStatusId.Completed,
                    BookingChannel = BookingChannel.Web,
                    VisitType = VisitType.Scheduled,
                    QueueNumber = "O05",
                    Reason = "Tái khám huyết áp",
                    CreatedAt = DateTime.UtcNow.AddDays(-2)
                };

                var oldApp2 = new Appointment
                {
                    AppointmentCode = "BN0001",
                    PatientId = pHa.Id,
                    DoctorId = doc1.Id,
                    DepartmentId = deptNoi.Id,
                    RoomId = room1.Id,
                    StatusId = (int)AppointmentStatusId.Completed,
                    BookingChannel = BookingChannel.Web,
                    VisitType = VisitType.Scheduled,
                    QueueNumber = "O03",
                    Reason = "Khám sức khỏe",
                    CreatedAt = DateTime.UtcNow.AddDays(-10)
                };

                db.Appointments.AddRange(oldApp1, oldApp2);
                await db.SaveChangesAsync();

                db.MedicalRecords.AddRange(
                    new MedicalRecord
                    {
                        AppointmentId = oldApp1.Id,
                        DoctorId = doc2.Id,
                        Symptoms = "Huyết áp dao động, hơi chóng mặt",
                        ClinicalExamination = "Tim đều, phổi trong, HA 135/85 mmHg",
                        PrimaryDiagnosisCode = "I10",
                        PrimaryDiagnosisDesc = "Tăng huyết áp nguyên phát",
                        Conclusion = "Đã hoàn tất khám theo yêu cầu",
                        CreatedAt = DateTime.UtcNow.AddDays(-2)
                    },
                    new MedicalRecord
                    {
                        AppointmentId = oldApp2.Id,
                        DoctorId = doc1.Id,
                        Symptoms = "Không có triệu chứng bất thường",
                        ClinicalExamination = "Thể trạng tốt, các chỉ số sinh hiệu trong giới hạn bình thường",
                        PrimaryDiagnosisCode = "Z00.0",
                        PrimaryDiagnosisDesc = "Khám sức khỏe tổng quát",
                        Conclusion = "Khám sức khỏe định kỳ",
                        CreatedAt = DateTime.UtcNow.AddDays(-10)
                    }
                );
                await db.SaveChangesAsync();
            }

            // 9. CA TRỰC NHÂN SỰ (DutyShift)
            if (!await db.DutyShifts.AnyAsync())
            {
                db.DutyShifts.Add(new DutyShift
                {
                    AccountId = docAcc1.Id,
                    DepartmentId = deptNoi.Id,
                    StartTime = today.ToDateTime(new TimeOnly(18, 0)),
                    EndTime = today.AddDays(1).ToDateTime(new TimeOnly(7, 0)),
                    Note = "Trực cấp cứu nội khoa sảnh B",
                    AssignedBy = headAcc1.Id,
                    CreatedAt = DateTime.UtcNow
                });

                db.DutyShifts.Add(new DutyShift
                {
                    AccountId = nurseAcc.Id,
                    DepartmentId = deptNoi.Id,
                    StartTime = today.ToDateTime(new TimeOnly(18, 0)),
                    EndTime = today.AddDays(1).ToDateTime(new TimeOnly(7, 0)),
                    Note = "Trực điều dưỡng theo dõi bệnh phòng",
                    AssignedBy = headAcc1.Id,
                    CreatedAt = DateTime.UtcNow
                });

                await db.SaveChangesAsync();
            }

            // 10. ĐỀ NGHỊ NGHỈ / ĐỔI CA (ScheduleException)
            if (doc3 != null)
            {
                var exThu = await db.ScheduleExceptions.FirstOrDefaultAsync(e => e.DoctorId == doc3.Id && e.Date == new DateOnly(2026, 11, 5));
                if (exThu == null)
                {
                    db.ScheduleExceptions.Add(new ScheduleException
                    {
                        DoctorId = doc3.Id,
                        Date = new DateOnly(2026, 11, 5),
                        Shift = ExamShift.Morning,
                        Type = ScheduleExceptionType.Training,
                        Status = ScheduleExceptionStatus.Pending,
                        Reason = "Tham gia đào tạo chuyên môn",
                        ReplacementDoctorId = doc1.Id,
                        CreatedAt = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync();
                }
            }

            logger.LogInformation("DevDataSeeder: Đã nạp đầy đủ dữ liệu mẫu thực tế cho Bác sĩ, Bệnh nhân, Hàng chờ, Bệnh án và Chỉ định xét nghiệm.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DevDataSeeder error: {Message}", ex.Message);
        }
    }
}
