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

        var defaultPassword = config.GetValue<string>("Seed:DefaultPassword");
        if (string.IsNullOrWhiteSpace(defaultPassword))
        {
            logger.LogWarning("Seed is enabled but Seed:DefaultPassword is not configured. Skipping seeding.");
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            if (await db.Accounts.AnyAsync())
            {
                return;
            }

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(defaultPassword);

            // 1. Tạo 1 ADMIN
            var adminAcc = new Account
            {
                Username = "admin",
                Email = "admin@his.local",
                PasswordHash = passwordHash,
                RoleId = (int)RoleId.Admin,
                IsActive = true
            };
            db.Accounts.Add(adminAcc);

            // 2. 2 khoa (mỗi khoa 1 trưởng khoa)
            var deptNoi = new Department { Name = "Khoa Nội tổng quát", Description = "Khám và điều trị nội khoa" };
            var deptNgoai = new Department { Name = "Khoa Ngoại tổng quát", Description = "Khám và phẫu thuật ngoại khoa" };
            db.Departments.AddRange(deptNoi, deptNgoai);
            await db.SaveChangesAsync();

            var headAcc1 = new Account { Username = "head_noi", Email = "head_noi@his.local", PasswordHash = passwordHash, RoleId = (int)RoleId.DepartmentHead };
            var headAcc2 = new Account { Username = "head_ngoai", Email = "head_ngoai@his.local", PasswordHash = passwordHash, RoleId = (int)RoleId.DepartmentHead };
            db.Accounts.AddRange(headAcc1, headAcc2);
            await db.SaveChangesAsync();

            db.DepartmentHeads.AddRange(
                new DepartmentHead { AccountId = headAcc1.Id, DepartmentId = deptNoi.Id, FullName = "PGS. TS. Trần Văn Trưởng" },
                new DepartmentHead { AccountId = headAcc2.Id, DepartmentId = deptNgoai.Id, FullName = "TS. Lê Quang Trưởng" }
            );

            // 3. 3 phòng khám
            var room1 = new ClinicRoom { DepartmentId = deptNoi.Id, RoomName = "Phòng khám Nội 1", RoomNumber = "P101" };
            var room2 = new ClinicRoom { DepartmentId = deptNoi.Id, RoomName = "Phòng khám Nội 2", RoomNumber = "P102" };
            var room3 = new ClinicRoom { DepartmentId = deptNgoai.Id, RoomName = "Phòng khám Ngoại 1", RoomNumber = "P201" };
            db.ClinicRooms.AddRange(room1, room2, room3);
            await db.SaveChangesAsync();

            // 4. 4 bác sĩ và 1 y tá
            var docAcc1 = new Account { Username = "bs_nam", Email = "nam@his.local", PasswordHash = passwordHash, RoleId = (int)RoleId.Doctor };
            var docAcc2 = new Account { Username = "bs_hoa", Email = "hoa@his.local", PasswordHash = passwordHash, RoleId = (int)RoleId.Doctor };
            var docAcc3 = new Account { Username = "bs_hung", Email = "hung@his.local", PasswordHash = passwordHash, RoleId = (int)RoleId.Doctor };
            var docAcc4 = new Account { Username = "bs_mai", Email = "mai@his.local", PasswordHash = passwordHash, RoleId = (int)RoleId.Doctor };
            var nurseAcc = new Account { Username = "nurse_lan", Email = "lan@his.local", PasswordHash = passwordHash, RoleId = (int)RoleId.Nurse };
            db.Accounts.AddRange(docAcc1, docAcc2, docAcc3, docAcc4, nurseAcc);
            await db.SaveChangesAsync();

            var doc1 = new Doctor { AccountId = docAcc1.Id, DepartmentId = deptNoi.Id, FullName = "BS. Nguyễn Văn Nam", Specialty = "Nội tổng quát", LicenseNumber = "CCHN-001" };
            var doc2 = new Doctor { AccountId = docAcc2.Id, DepartmentId = deptNoi.Id, FullName = "BS. Trần Thị Hoa", Specialty = "Nội tim mạch", LicenseNumber = "CCHN-002" };
            var doc3 = new Doctor { AccountId = docAcc3.Id, DepartmentId = deptNgoai.Id, FullName = "BS. Phạm Văn Hùng", Specialty = "Ngoại chấn thương", LicenseNumber = "CCHN-003" };
            var doc4 = new Doctor { AccountId = docAcc4.Id, DepartmentId = deptNgoai.Id, FullName = "BS. Đỗ Thị Mai", Specialty = "Ngoại tiêu hóa", LicenseNumber = "CCHN-004" };
            db.Doctors.AddRange(doc1, doc2, doc3, doc4);

            var nurse = new Nurse { AccountId = nurseAcc.Id, DepartmentId = deptNoi.Id, FullName = "Điều dưỡng Hoàng Lan" };
            db.Nurses.Add(nurse);

            // 5. 1 tài khoản PATIENT kèm 1 Patient + link SELF
            var patientAcc = new Account { Username = "patient_an", Email = "an@gmail.com", PasswordHash = passwordHash, RoleId = (int)RoleId.Patient };
            db.Accounts.Add(patientAcc);
            await db.SaveChangesAsync();

            var patient = new Patient
            {
                FullName = "Nguyễn Văn An",
                DateOfBirth = new DateOnly(1990, 1, 1),
                Gender = Gender.Male,
                PhoneNumber = "0912345678",
                NationalId = "001090123456",
                Address = "Hà Nội"
            };
            db.Patients.Add(patient);
            await db.SaveChangesAsync();

            db.AccountPatientProfiles.Add(new AccountPatientProfile
            {
                AccountId = patientAcc.Id,
                PatientId = patient.Id,
                Relationship = PatientRelationship.Self,
                Status = LinkStatus.Active,
                CanViewRecords = true
            });

            // 6. doctor_schedules thứ 2–thứ 6 (ca sáng + chiều, slot 15 phút, max_patients=40, online_slots=28)
            var scheduleStartDate = new DateOnly(2026, 1, 1);
            var activeDocs = new[] { (doc1, room1), (doc2, room2), (doc3, room3), (doc4, room3) };

            for (int day = 1; day <= 5; day++) // Thứ 2 đến Thứ 6 (DayOfWeek 1..5)
            {
                foreach (var (d, r) in activeDocs)
                {
                    // Ca sáng: 7:30 - 11:30
                    db.DoctorSchedules.Add(new DoctorSchedule
                    {
                        DoctorId = d.Id,
                        RoomId = r.Id,
                        DayOfWeek = (DayOfWeek)day,
                        Shift = ExamShift.Morning,
                        StartTime = new TimeOnly(7, 30),
                        EndTime = new TimeOnly(11, 30),
                        SlotDurationMinutes = 15,
                        MaxPatients = 40,
                        OnlineSlots = 28,
                        EffectiveFrom = scheduleStartDate
                    });

                    // Ca chiều: 13:30 - 17:30
                    db.DoctorSchedules.Add(new DoctorSchedule
                    {
                        DoctorId = d.Id,
                        RoomId = r.Id,
                        DayOfWeek = (DayOfWeek)day,
                        Shift = ExamShift.Afternoon,
                        StartTime = new TimeOnly(13, 30),
                        EndTime = new TimeOnly(17, 30),
                        SlotDurationMinutes = 15,
                        MaxPatients = 40,
                        OnlineSlots = 28,
                        EffectiveFrom = scheduleStartDate
                    });
                }
            }
            await db.SaveChangesAsync();

            // 7. appointment_slots cho 7 ngày tới chèn trực tiếp (bỏ qua T7/CN)
            var today = DateOnly.FromDateTime(DateTime.Today);
            for (int i = 0; i < 7; i++)
            {
                var curDate = today.AddDays(i);
                if (curDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                foreach (var (d, r) in activeDocs)
                {
                    // Chèn slot sáng: 8:00, 8:15, 8:30, 8:45
                    for (int m = 0; m < 4; m++)
                    {
                        var start = curDate.ToDateTime(new TimeOnly(8, m * 15));
                        db.AppointmentSlots.Add(new AppointmentSlot
                        {
                            DoctorId = d.Id,
                            RoomId = r.Id,
                            StartTime = start,
                            EndTime = start.AddMinutes(15),
                            Capacity = 1,
                            BookedCount = 0,
                            Status = SlotStatus.Available
                        });
                    }

                    // Chèn slot chiều: 14:00, 14:15, 14:30, 14:45
                    for (int m = 0; m < 4; m++)
                    {
                        var start = curDate.ToDateTime(new TimeOnly(14, m * 15));
                        db.AppointmentSlots.Add(new AppointmentSlot
                        {
                            DoctorId = d.Id,
                            RoomId = r.Id,
                            StartTime = start,
                            EndTime = start.AddMinutes(15),
                            Capacity = 1,
                            BookedCount = 0,
                            Status = SlotStatus.Available
                        });
                    }
                }
            }

            // 8. 3 insurance_plans, services và medications
            var plan1 = new InsurancePlan { Name = "Đúng tuyến - 80%", CoveragePct = 80.00m, ReferralType = ReferralType.InNetwork };
            var plan2 = new InsurancePlan { Name = "Đúng tuyến - 100%", CoveragePct = 100.00m, ReferralType = ReferralType.InNetwork };
            var plan3 = new InsurancePlan { Name = "Trái tuyến - 40%", CoveragePct = 40.00m, ReferralType = ReferralType.OutOfNetwork };
            db.InsurancePlans.AddRange(plan1, plan2, plan3);

            db.Services.AddRange(
                new Service { Name = "Khám lâm sàng nội", ServiceTypeId = 1, Price = 150000m },
                new Service { Name = "Khám lâm sàng ngoại", ServiceTypeId = 1, Price = 150000m },
                new Service { Name = "Xét nghiệm tổng phân tích máu", ServiceTypeId = 2, Price = 120000m },
                new Service { Name = "Chụp X-quang ngực thẳng", ServiceTypeId = 3, Price = 180000m }
            );

            db.Medications.AddRange(
                new Medication { Name = "Paracetamol 500mg", ActiveIngredient = "Paracetamol", Strength = "500mg", Unit = "Viên", Price = 1500m },
                new Medication { Name = "Amoxicillin 500mg", ActiveIngredient = "Amoxicillin", Strength = "500mg", Unit = "Viên", Price = 3000m }
            );

            await db.SaveChangesAsync();
            logger.LogInformation("DevDataSeeder completed successfully with directly seeded appointment slots.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not run DevDataSeeder (DB might not be connected or migrated yet).");
        }
    }
}
