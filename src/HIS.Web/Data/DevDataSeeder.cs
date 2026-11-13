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
                doc1 = new Doctor
                {
                    AccountId = docAcc1.Id,
                    DepartmentId = deptNoi.Id,
                    FullName = "BS. Nguyễn Hoàng Nam",
                    Specialty = "Nội tổng quát",
                    AcademicTitle = "ThS.BS",
                    LicenseNumber = "CCHN-001",
                    YearsOfExperience = 10,
                    IsFeatured = true,
                    IsPublic = true,
                    DisplayOrder = 1,
                    ShortBio = "Bác sĩ chuyên khoa Nội tổng quát, giàu kinh nghiệm trong chẩn đoán và điều trị bệnh lý người lớn.",
                    EmploymentType = EmploymentType.FullTime,
                    MinShiftsPerWeek = 6,
                    MaxShiftsPerWeek = 10
                };
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
                doc2 = new Doctor
                {
                    AccountId = docAcc2.Id,
                    DepartmentId = deptNoi.Id,
                    FullName = "BS. Lê Quang Huy",
                    Specialty = "Nội tim mạch",
                    AcademicTitle = "BS.CKII",
                    LicenseNumber = "CCHN-002",
                    YearsOfExperience = 15,
                    IsFeatured = true,
                    IsPublic = true,
                    DisplayOrder = 2,
                    ShortBio = "Chuyên gia tim mạch can thiệp, tư vấn và điều trị tăng huyết áp, bệnh mạch vành.",
                    EmploymentType = EmploymentType.FullTime,
                    MinShiftsPerWeek = 6,
                    MaxShiftsPerWeek = 10
                };
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
                doc3 = new Doctor
                {
                    AccountId = docAcc3.Id,
                    DepartmentId = deptNoi.Id,
                    FullName = "BS. Trần Minh Thư",
                    Specialty = "Tim mạch",
                    AcademicTitle = "TS.BS",
                    LicenseNumber = "CCHN-003",
                    YearsOfExperience = 12,
                    IsFeatured = true,
                    IsPublic = true,
                    DisplayOrder = 3,
                    ShortBio = "Tiến sĩ Y khoa, nghiên cứu và điều trị chuyên sâu bệnh lý mạch máu và rối loạn nhịp.",
                    EmploymentType = EmploymentType.FullTime,
                    MinShiftsPerWeek = 6,
                    MaxShiftsPerWeek = 10
                };
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

            // 5. CẤU HÌNH KỲ ĐĂNG KÝ (SchedulingPeriod) & LỊCH KHÁM (DoctorSchedule)
            var currentDay = DateOnly.FromDateTime(DateTime.Today);
            var daysFromMonday = ((int)currentDay.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var periodMonday = currentDay.AddDays(-daysFromMonday);
            var periodSunday = periodMonday.AddDays(6);

            var period = await db.SchedulingPeriods.FirstOrDefaultAsync(p => p.StartDate <= currentDay && p.EndDate >= currentDay);
            if (period == null)
            {
                period = new SchedulingPeriod
                {
                    Name = $"Kỳ làm việc tuần {periodMonday:dd/MM} - {periodSunday:dd/MM/yyyy}",
                    StartDate = periodMonday,
                    EndDate = periodSunday,
                    BiddingOpensAt = DateTime.UtcNow.AddDays(-2),
                    BiddingClosesAt = DateTime.UtcNow.AddDays(5),
                    Status = SchedulingPeriodStatus.Open,
                    CreatedBy = headAcc1.Id,
                    CreatedAt = DateTime.UtcNow
                };
                db.SchedulingPeriods.Add(period);
                await db.SaveChangesAsync();
            }

            if (!await db.DoctorSchedules.AnyAsync(s => s.DoctorId == doc1.Id && s.WorkDate >= periodMonday && s.WorkDate <= periodSunday))
            {
                for (int i = 0; i < 5; i++)
                {
                    var wDate = periodMonday.AddDays(i);
                    db.DoctorSchedules.Add(new DoctorSchedule
                    {
                        DoctorId = doc1.Id,
                        PeriodId = period.Id,
                        DepartmentId = deptNoi.Id,
                        RoomId = room1.Id,
                        WorkDate = wDate,
                        Shift = ExamShift.Morning,
                        StartTime = new TimeOnly(7, 30),
                        EndTime = new TimeOnly(11, 30),
                        SlotDurationMinutes = 30,
                        MaxPatients = 40,
                        OnlineSlots = 28,
                        Status = DoctorScheduleStatus.Approved,
                        ReviewedBy = headAcc1.Id,
                        ReviewedAt = DateTime.UtcNow
                    });
                    db.DoctorSchedules.Add(new DoctorSchedule
                    {
                        DoctorId = doc1.Id,
                        PeriodId = period.Id,
                        DepartmentId = deptNoi.Id,
                        RoomId = room1.Id,
                        WorkDate = wDate,
                        Shift = ExamShift.Afternoon,
                        StartTime = new TimeOnly(13, 0),
                        EndTime = new TimeOnly(16, 30),
                        SlotDurationMinutes = 30,
                        MaxPatients = 40,
                        OnlineSlots = 28,
                        Status = DoctorScheduleStatus.Approved,
                        ReviewedBy = headAcc1.Id,
                        ReviewedAt = DateTime.UtcNow
                    });
                }
                await db.SaveChangesAsync();
            }

            if (!await db.DoctorSchedules.AnyAsync(s => s.DoctorId == doc2.Id && s.WorkDate >= periodMonday && s.WorkDate <= periodSunday))
            {
                for (int i = 0; i < 4; i++)
                {
                    var wDate = periodMonday.AddDays(i);
                    db.DoctorSchedules.Add(new DoctorSchedule
                    {
                        DoctorId = doc2.Id,
                        PeriodId = period.Id,
                        DepartmentId = deptNoi.Id,
                        RoomId = room102.Id,
                        WorkDate = wDate,
                        Shift = ExamShift.Morning,
                        StartTime = new TimeOnly(7, 30),
                        EndTime = new TimeOnly(11, 30),
                        SlotDurationMinutes = 30,
                        MaxPatients = 35,
                        OnlineSlots = 25,
                        Status = DoctorScheduleStatus.Approved,
                        ReviewedBy = headAcc1.Id,
                        ReviewedAt = DateTime.UtcNow
                    });
                    db.DoctorSchedules.Add(new DoctorSchedule
                    {
                        DoctorId = doc2.Id,
                        PeriodId = period.Id,
                        DepartmentId = deptNoi.Id,
                        RoomId = room102.Id,
                        WorkDate = wDate,
                        Shift = ExamShift.Afternoon,
                        StartTime = new TimeOnly(13, 0),
                        EndTime = new TimeOnly(16, 30),
                        SlotDurationMinutes = 30,
                        MaxPatients = 35,
                        OnlineSlots = 25,
                        Status = DoctorScheduleStatus.Approved,
                        ReviewedBy = headAcc1.Id,
                        ReviewedAt = DateTime.UtcNow
                    });
                }
                // Thứ 6: Đăng ký Bidding ca Sáng (để test Shift Bidding)
                var friday = periodMonday.AddDays(4);
                db.DoctorSchedules.Add(new DoctorSchedule
                {
                    DoctorId = doc2.Id,
                    PeriodId = period.Id,
                    DepartmentId = deptNoi.Id,
                    RoomId = null,
                    WorkDate = friday,
                    Shift = ExamShift.Morning,
                    StartTime = new TimeOnly(7, 30),
                    EndTime = new TimeOnly(11, 30),
                    SlotDurationMinutes = 30,
                    MaxPatients = 35,
                    OnlineSlots = 25,
                    Status = DoctorScheduleStatus.Bidding,
                    DoctorNote = "Đăng ký ca sáng thứ 6"
                });
                await db.SaveChangesAsync();
            }

            if (doc3 != null && !await db.DoctorSchedules.AnyAsync(s => s.DoctorId == doc3.Id && s.WorkDate >= periodMonday && s.WorkDate <= periodSunday))
            {
                for (int i = 0; i < 5; i++)
                {
                    var wDate = periodMonday.AddDays(i);
                    db.DoctorSchedules.Add(new DoctorSchedule
                    {
                        DoctorId = doc3.Id,
                        PeriodId = period.Id,
                        DepartmentId = deptNgoai.Id,
                        RoomId = room201.Id,
                        WorkDate = wDate,
                        Shift = ExamShift.Morning,
                        StartTime = new TimeOnly(7, 30),
                        EndTime = new TimeOnly(11, 30),
                        SlotDurationMinutes = 30,
                        MaxPatients = 30,
                        OnlineSlots = 20,
                        Status = DoctorScheduleStatus.Approved,
                        ReviewedBy = headAcc1.Id,
                        ReviewedAt = DateTime.UtcNow
                    });
                    db.DoctorSchedules.Add(new DoctorSchedule
                    {
                        DoctorId = doc3.Id,
                        PeriodId = period.Id,
                        DepartmentId = deptNgoai.Id,
                        RoomId = room201.Id,
                        WorkDate = wDate,
                        Shift = ExamShift.Afternoon,
                        StartTime = new TimeOnly(13, 0),
                        EndTime = new TimeOnly(16, 30),
                        SlotDurationMinutes = 30,
                        MaxPatients = 30,
                        OnlineSlots = 20,
                        Status = DoctorScheduleStatus.Approved,
                        ReviewedBy = headAcc1.Id,
                        ReviewedAt = DateTime.UtcNow
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
                    CreatedAt = DateTime.Now.AddDays(-2)
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
                    CreatedAt = DateTime.Now.AddDays(-10)
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
                        FollowUpDate = currentDay.AddDays(2),
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
                        CreatedAt = DateTime.Now.AddDays(-10)
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
                    CreatedAt = DateTime.Now
                });

                db.DutyShifts.Add(new DutyShift
                {
                    AccountId = nurseAcc.Id,
                    DepartmentId = deptNoi.Id,
                    StartTime = today.ToDateTime(new TimeOnly(18, 0)),
                    EndTime = today.AddDays(1).ToDateTime(new TimeOnly(7, 0)),
                    Note = "Trực điều dưỡng theo dõi bệnh phòng",
                    AssignedBy = headAcc1.Id,
                    CreatedAt = DateTime.Now
                });

                await db.SaveChangesAsync();
            }

            // 10. ĐỀ NGHỊ NGHỈ / ĐỔI CA (ScheduleException)
            if (doc3 != null)
            {
                var nextWeekday = today.AddDays(1);
                while (nextWeekday.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    nextWeekday = nextWeekday.AddDays(1);
                }

                var exThu = await db.ScheduleExceptions.FirstOrDefaultAsync(e => e.DoctorId == doc3.Id && e.Date == nextWeekday);
                if (exThu == null)
                {
                    db.ScheduleExceptions.Add(new ScheduleException
                    {
                        DoctorId = doc3.Id,
                        Date = nextWeekday,
                        Shift = ExamShift.Morning,
                        Type = ScheduleExceptionType.Training,
                        Status = ScheduleExceptionStatus.Pending,
                        Reason = "Tham gia đào tạo chuyên môn",
                        ReplacementDoctorId = doc1.Id,
                        CreatedAt = DateTime.Now
                    });
                    await db.SaveChangesAsync();
                }
            }

            // 11. TÀI KHOẢN KẾ TOÁN / THU NGÂN (Role Nurse = 4 để giữ nguyên schema develop)
            var cashierAcc = await db.Accounts.FirstOrDefaultAsync(a => a.Username == "ketoan");
            if (cashierAcc == null)
            {
                cashierAcc = new Account
                {
                    Username = "ketoan",
                    Email = "ketoan@his.local",
                    PasswordHash = passwordHash,
                    RoleId = (int)RoleId.Nurse,
                    IsActive = true
                };
                db.Accounts.Add(cashierAcc);
                await db.SaveChangesAsync();
            }

            // 12. DANH MỤC DỊCH VỤ KỸ THUẬT & CÔNG KHÁM (Services)
            if (!await db.Services.AnyAsync(s => s.Name == "Tổng phân tích tế bào máu ngoại vi"))
            {
                var sExam = await db.Services.FirstOrDefaultAsync(s => s.ServiceTypeId == (int)ServiceTypeId.Examination);
                if (sExam == null)
                {
                    sExam = new Service { Name = "Khám bệnh chuyên khoa Nội", ServiceTypeId = (int)ServiceTypeId.Examination, Price = 150000m, IsActive = true };
                    db.Services.Add(sExam);
                }
                var sBlood = new Service { Name = "Tổng phân tích tế bào máu ngoại vi", ServiceTypeId = (int)ServiceTypeId.LabTest, Price = 120000m, IsActive = true };
                var sXray = new Service { Name = "Chụp X-quang ngực thẳng kỹ thuật số", ServiceTypeId = (int)ServiceTypeId.Imaging, Price = 180000m, IsActive = true };
                var sUltra = new Service { Name = "Siêu âm ổ bụng tổng quát", ServiceTypeId = (int)ServiceTypeId.Imaging, Price = 220000m, IsActive = true };
                var sVip = new Service { Name = "Khám chuyên gia theo yêu cầu (Tự túc)", ServiceTypeId = (int)ServiceTypeId.Examination, Price = 300000m, IsActive = true };

                db.Services.AddRange(sBlood, sXray, sUltra, sVip);
                await db.SaveChangesAsync();
            }

            // 13. DANH MỤC THUỐC (Medications)
            if (!await db.Medications.AnyAsync())
            {
                db.Medications.AddRange(
                    new Medication { Name = "Paracetamol 500mg", ActiveIngredient = "Paracetamol", Strength = "500mg", Unit = "Viên", Price = 1500m, IsActive = true },
                    new Medication { Name = "Amoxicillin 500mg", ActiveIngredient = "Amoxicillin", Strength = "500mg", Unit = "Viên", Price = 3500m, IsActive = true },
                    new Medication { Name = "Amlodipine 5mg", ActiveIngredient = "Amlodipine", Strength = "5mg", Unit = "Viên", Price = 4000m, IsActive = true },
                    new Medication { Name = "Vitamin C 500mg", ActiveIngredient = "Acid Ascorbic", Strength = "500mg", Unit = "Viên", Price = 1000m, IsActive = true },
                    new Medication { Name = "Viên bổ gan thảo dược (Tự túc)", ActiveIngredient = "Cao Actiso", Strength = "200mg", Unit = "Viên", Price = 25000m, IsActive = true }
                );
                await db.SaveChangesAsync();
            }

            // 14. DANH MỤC GÓI BHYT (InsurancePlans)
            var plan80 = await db.InsurancePlans.FirstOrDefaultAsync(p => p.CoveragePct == 80m);
            if (plan80 == null)
            {
                plan80 = new InsurancePlan { Name = "BHYT Đúng tuyến - Hưởng 80%", CoveragePct = 80m, ReferralType = ReferralType.InNetwork };
                var plan100 = new InsurancePlan { Name = "BHYT Đúng tuyến đặc biệt - Hưởng 100%", CoveragePct = 100m, ReferralType = ReferralType.InNetwork };
                var plan0 = new InsurancePlan { Name = "BHYT Trái tuyến ngoại trú - 0%", CoveragePct = 0m, ReferralType = ReferralType.OutOfNetwork };
                db.InsurancePlans.AddRange(plan80, plan100, plan0);
                await db.SaveChangesAsync();
            }

            // 15. THẺ BHYT MẪU (InsuranceCards)
            if (!await db.InsuranceCards.AnyAsync())
            {
                if (pBao != null && plan80 != null)
                {
                    db.InsuranceCards.Add(new InsuranceCard
                    {
                        PatientId = pBao.Id,
                        CardNumber = "DN4797938328123",
                        InsurancePlanId = plan80.Id,
                        RegisteredFacility = "Bệnh viện Đa khoa MediCare",
                        IsRegisteredHere = true,
                        ValidFrom = new DateOnly(2024, 1, 1),
                        ValidTo = new DateOnly(2028, 12, 31),
                        IsActive = true
                    });
                }
                if (pMinh != null && plan80 != null)
                {
                    var plan100 = await db.InsurancePlans.FirstOrDefaultAsync(p => p.CoveragePct == 100m) ?? plan80;
                    db.InsuranceCards.Add(new InsuranceCard
                    {
                        PatientId = pMinh.Id,
                        CardNumber = "HT2797911223344",
                        InsurancePlanId = plan100.Id,
                        RegisteredFacility = "Bệnh viện Đa khoa MediCare",
                        IsRegisteredHere = true,
                        ValidFrom = new DateOnly(2024, 1, 1),
                        ValidTo = new DateOnly(2028, 12, 31),
                        IsActive = true
                    });
                }
                await db.SaveChangesAsync();
            }

            // 16. DỮ LIỆU ĐỒNG BỘ CHO THU VIỆN PHÍ, TẠM ỨNG & SỔ THU NGÂN
            var pChi = await db.Patients.FirstOrDefaultAsync(p => p.FullName == "Lê Minh Chí");
            if (pChi == null)
            {
                pChi = new Patient { FullName = "Lê Minh Chí", DateOfBirth = new DateOnly(1995, 2, 14), Gender = Gender.Male, PhoneNumber = "0890201234", NationalId = "079085001234", Address = "Quận 1, TP. Hồ Chí Minh" };
                db.Patients.Add(pChi);
                await db.SaveChangesAsync();
            }

            // Thẻ BHYT cho Lê Minh Chí
            if (pChi != null && plan80 != null && !await db.InsuranceCards.AnyAsync(c => c.PatientId == pChi.Id))
            {
                db.InsuranceCards.Add(new InsuranceCard
                {
                    PatientId = pChi.Id,
                    CardNumber = "DN4797938328001",
                    InsurancePlanId = plan80.Id,
                    RegisteredFacility = "Bệnh viện Đa khoa MediCare",
                    IsRegisteredHere = true,
                    ValidFrom = new DateOnly(2024, 1, 1),
                    ValidTo = new DateOnly(2028, 12, 31),
                    IsActive = true
                });
                await db.SaveChangesAsync();
            }

            // Đảm bảo có ca thu ngân đang mở (lưu trong access_logs theo schema develop)
            int cashierId = cashierAcc?.Id ?? 1;
            var hasOpenShift = await db.AccessLogs.AnyAsync(l => l.EntityName == "cashier_shifts" && l.AccountId == cashierId && l.Action == "SHIFT_OPEN");
            if (!hasOpenShift)
            {
                var shiftLog = new AccessLog
                {
                    AccountId = cashierId,
                    Action = "SHIFT_OPEN",
                    EntityName = "cashier_shifts",
                    IsSuccess = true,
                    Severity = "INFO",
                    OccurredAt = DateTime.Now.AddHours(-2),
                    Details = "{\"OpeningCash\":1000000,\"Note\":\"Ca làm việc thu ngân mẫu\"}"
                };
                db.AccessLogs.Add(shiftLog);
                await db.SaveChangesAsync();
                shiftLog.EntityId = checked((int)shiftLog.Id);
                await db.SaveChangesAsync();
            }

            if (db.Database.IsRelational())
            {
                await db.Payments
                    .Where(p => p.ReceiptEmail != null && p.ReceiptEmail.StartsWith("#U=") && p.PaymentType != PaymentType.Deposit)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.PaymentType, PaymentType.Deposit));
            }

            if (!await db.Payments.AnyAsync(p => p.PaymentType == PaymentType.Deposit) && pChi != null)
            {
                async Task<Invoice?> EnsureSeedInvoiceAsync(int patId, DateTime at)
                {
                    var app = await db.Appointments
                        .Include(a => a.Invoice)
                        .Where(a => a.PatientId == patId && (a.Invoice == null || a.Invoice.Status != InvoiceStatus.Paid))
                        .OrderByDescending(a => a.CreatedAt)
                        .FirstOrDefaultAsync();
                    if (app == null)
                    {
                        var anyDoc = await db.Doctors.AsNoTracking().FirstOrDefaultAsync();
                        var anyDept = await db.Departments.AsNoTracking().FirstOrDefaultAsync();
                        if (anyDoc == null || anyDept == null) return null;
                        app = new Appointment
                        {
                            PatientId = patId,
                            DoctorId = anyDoc.Id,
                            DepartmentId = anyDept.Id,
                            AppointmentCode = $"DEP-SEED-{patId}",
                            StatusId = (int)AppointmentStatusId.CheckedIn,
                            CreatedAt = at
                        };
                        db.Appointments.Add(app);
                        await db.SaveChangesAsync();
                    }

                    if (app.Invoice == null)
                    {
                        var inv = new Invoice
                        {
                            AppointmentId = app.Id,
                            PatientId = patId,
                            TotalAmount = 0m,
                            InsuranceCoveredAmount = 0m,
                            PatientAmount = 0m,
                            Status = InvoiceStatus.Unpaid,
                            CreatedAt = at
                        };
                        db.Invoices.Add(inv);
                        await db.SaveChangesAsync();
                        return inv;
                    }

                    return app.Invoice;
                }

                await using var depositTx = await db.Database.BeginTransactionAsync();

                var invChi = await EnsureSeedInvoiceAsync(pChi.Id, DateTime.Now.AddMinutes(-90));
                if (invChi != null)
                {
                    db.Payments.Add(new Payment
                    {
                        InvoiceId = invChi.Id,
                        Amount = 2000000m,
                        PaymentType = PaymentType.Deposit,
                        PaymentMethod = PaymentMethod.Cash,
                        CashierId = cashierId,
                        ReceiptEmail = "#U=0;R=0#",
                        Note = "#BY=BS.CKII Phạm Thu Hằng;PUR=Tạm ứng nhập viện Ngoại khoa#Thu tạm ứng mẫu",
                        PaidAt = DateTime.Now.AddMinutes(-90)
                    });
                }

                if (pBao != null)
                {
                    var invBao = await EnsureSeedInvoiceAsync(pBao.Id, DateTime.Now.AddMinutes(-60));
                    if (invBao != null)
                    {
                        db.Payments.Add(new Payment
                        {
                            InvoiceId = invBao.Id,
                            Amount = 300000m,
                            PaymentType = PaymentType.Deposit,
                            PaymentMethod = PaymentMethod.BankTransfer,
                            CashierId = cashierId,
                            ReceiptEmail = "#U=0;R=0;REF=FT2431889210#",
                            Note = "#BY=BS.CKII Phạm Thu Hằng;PUR=Tạm ứng xét nghiệm cận lâm sàng#Thu tạm ứng qua VietQR",
                            PaidAt = DateTime.Now.AddMinutes(-60)
                        });
                    }
                }

                if (pNgoc != null)
                {
                    var invNgoc = await EnsureSeedInvoiceAsync(pNgoc.Id, DateTime.Now.AddMinutes(-45));
                    if (invNgoc != null)
                    {
                        db.Payments.Add(new Payment
                        {
                            InvoiceId = invNgoc.Id,
                            Amount = 1200000m,
                            PaymentType = PaymentType.Deposit,
                            PaymentMethod = PaymentMethod.Cash,
                            CashierId = cashierId,
                            ReceiptEmail = "#U=0;R=0#",
                            Note = "#BY=TS.BS Lê Hoàng Nam;PUR=Tạm ứng phẫu thuật nội soi#Thu tạm ứng mẫu",
                            PaidAt = DateTime.Now.AddMinutes(-45)
                        });
                    }
                }

                await db.SaveChangesAsync();
                if (db.Database.IsRelational())
                {
                    await db.Payments
                        .Where(p => p.ReceiptEmail != null && p.ReceiptEmail.StartsWith("#U=") && p.PaymentType != PaymentType.Deposit)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.PaymentType, PaymentType.Deposit));
                }
                await depositTx.CommitAsync();
            }

            // 17. NHẬT KÝ TRUY VẾT MẪU (AccessLogs)
            if (!await db.AccessLogs.AnyAsync() && adminAcc != null)
            {
                db.AccessLogs.AddRange(
                    new AccessLog { AccountId = adminAcc.Id, Action = "AdminLogin", OccurredAt = DateTime.UtcNow.AddHours(-3), IpAddress = "127.0.0.1" },
                    new AccessLog { AccountId = adminAcc.Id, Action = "ViewDepartmentCatalog", OccurredAt = DateTime.UtcNow.AddHours(-2), IpAddress = "127.0.0.1" }
                );
                await db.SaveChangesAsync();
            }

            logger.LogInformation("DevDataSeeder: Đã nạp đầy đủ dữ liệu mẫu thực tế cho Bác sĩ, Bệnh nhân, Hàng chờ, Bệnh án, Thu ngân, Tạm ứng và Thẻ BHYT.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DevDataSeeder error: {Message}", ex.Message);
        }
    }
}
