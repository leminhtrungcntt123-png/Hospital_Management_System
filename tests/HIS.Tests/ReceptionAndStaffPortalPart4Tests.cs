using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace HIS.Tests;

public class ReceptionAndStaffPortalPart4Tests
{
    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utc;

        public MutableTimeProvider(DateTime start)
        {
            _utc = new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc));
        }

        public override DateTimeOffset GetUtcNow() => _utc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static (
        AppDbContext Context,
        MutableTimeProvider Clock,
        ReceptionService Reception,
        int NurseAccountId,
        int Dept1Id,
        int Doctor1Id,
        int Room1Id,
        int Schedule1Id,
        int PatientId
    ) CreateHarness()
    {
        // 2026-11-09 is a Monday
        var clock = new MutableTimeProvider(new DateTime(2026, 11, 9, 8, 30, 0, DateTimeKind.Utc));
        var dbName = "ReceptionPart4Test_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new AppDbContext(options, clock);

        context.Roles.AddRange(
            new Role { Id = (int)RoleId.Admin, Code = "ADMIN", Name = "Quản trị viên" },
            new Role { Id = (int)RoleId.Doctor, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = (int)RoleId.Nurse, Code = "NURSE", Name = "Điều dưỡng" },
            new Role { Id = (int)RoleId.DepartmentHead, Code = "DEPT_HEAD", Name = "Trưởng khoa" },
            new Role { Id = (int)RoleId.Patient, Code = "PATIENT", Name = "Bệnh nhân" }
        );

        context.AppointmentStatuses.AddRange(
            new AppointmentStatus { Id = 1, Code = "PENDING", Name = "Chờ duyệt", SortOrder = 1, IsTerminal = false },
            new AppointmentStatus { Id = 2, Code = "CONFIRMED", Name = "Đã xác nhận", SortOrder = 2, IsTerminal = false },
            new AppointmentStatus { Id = 3, Code = "CHECKED_IN", Name = "Đã check-in", SortOrder = 3, IsTerminal = false },
            new AppointmentStatus { Id = 4, Code = "READY_FOR_DOCTOR", Name = "Chờ bác sĩ gọi", SortOrder = 4, IsTerminal = false },
            new AppointmentStatus { Id = 5, Code = "IN_PROGRESS", Name = "Đang khám", SortOrder = 5, IsTerminal = false },
            new AppointmentStatus { Id = 6, Code = "COMPLETED", Name = "Hoàn thành", SortOrder = 6, IsTerminal = true },
            new AppointmentStatus { Id = 7, Code = "CANCELLED", Name = "Đã hủy", SortOrder = 7, IsTerminal = true }
        );

        var dept1 = new Department { Name = "Khoa Nội tổng hợp", Description = "Nội tổng quát" };
        context.Departments.Add(dept1);
        context.SaveChanges();

        var room1 = new ClinicRoom { DepartmentId = dept1.Id, RoomNumber = "P101", RoomName = "Phòng Khám Nội 1" };
        context.ClinicRooms.Add(room1);
        context.SaveChanges();

        var doc1Account = new Account
        {
            Username = "dr.minh",
            Email = "minh.doc@hospital.com",
            PasswordHash = "BCRYPT_HASH",
            RoleId = (int)RoleId.Doctor,
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1),
            UpdatedAt = new DateTime(2026, 1, 1)
        };
        context.Accounts.Add(doc1Account);
        context.SaveChanges();

        var doc1 = new Doctor
        {
            AccountId = doc1Account.Id,
            DepartmentId = dept1.Id,
            FullName = "BS. Lê Hoàng Minh",
            LicenseNumber = "CCHN-001"
        };
        context.Doctors.Add(doc1);

        var nurseAccount = new Account
        {
            Username = "nurse.lan",
            Email = "lan.nurse@hospital.com",
            PasswordHash = "BCRYPT_HASH",
            RoleId = (int)RoleId.Nurse,
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1),
            UpdatedAt = new DateTime(2026, 1, 1)
        };
        context.Accounts.Add(nurseAccount);
        context.SaveChanges();

        var nurse = new Nurse
        {
            AccountId = nurseAccount.Id,
            DepartmentId = dept1.Id,
            FullName = "ĐD. Phạm Thị Lan"
        };
        context.Nurses.Add(nurse);

        var patient = new Patient
        {
            FullName = "Nguyễn Văn Hùng",
            DateOfBirth = new DateOnly(1990, 3, 15),
            Gender = Gender.Male,
            PhoneNumber = "0912345678",
            NationalId = "001090012345",
            CreatedAt = new DateTime(2026, 1, 1)
        };
        context.Patients.Add(patient);

        var schedule = new DoctorSchedule
        {
            DoctorId = doc1.Id,
            RoomId = room1.Id,
            DayOfWeek = DayOfWeek.Monday,
            Shift = ExamShift.Morning,
            StartTime = new TimeOnly(7, 30),
            EndTime = new TimeOnly(11, 30),
            SlotDurationMinutes = 15,
            OnlineRatioPct = 60,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true
        };
        context.DoctorSchedules.Add(schedule);
        context.SaveChanges();

        var bookingOpts = Options.Create(new BookingOptions());

        var allocator = new QueuePositionAllocator(context, bookingOpts, clock);
        var reception = new ReceptionService(context, allocator, bookingOpts, clock);

        return (
            context,
            clock,
            reception,
            nurseAccount.Id,
            dept1.Id,
            doc1.Id,
            room1.Id,
            schedule.Id,
            patient.Id
        );
    }

    [Fact]
    public async Task GetDeskAsync_ValidNurse_ReturnsReceptionDeskWithVisitsAndSchedules()
    {
        var (context, clock, reception, nurseAccountId, dept1Id, doc1Id, room1Id, _, patientId) = CreateHarness();

        var app = new Appointment
        {
            AppointmentCode = "HIS-20261109-001",
            PatientId = patientId,
            DepartmentId = dept1Id,
            DoctorId = doc1Id,
            RoomId = room1Id,
            BookingChannel = BookingChannel.Web,
            StatusId = (int)AppointmentStatusId.Confirmed,
            PriorityLevel = 0,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            UpdatedAt = clock.GetUtcNow().UtcDateTime
        };
        context.Appointments.Add(app);
        await context.SaveChangesAsync();

        var result = await reception.GetDeskAsync(nurseAccountId, null);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Khoa Nội tổng hợp", result.Value.Department);
        Assert.Single(result.Value.Visits);
        Assert.Equal("HIS-20261109-001", result.Value.Visits[0].Code);
        Assert.NotEmpty(result.Value.Schedules);
    }

    [Fact]
    public async Task GetDeskAsync_WithSearch_FiltersVisits()
    {
        var (context, clock, reception, nurseAccountId, dept1Id, doc1Id, room1Id, _, patientId) = CreateHarness();

        var app1 = new Appointment
        {
            AppointmentCode = "HIS-20261109-MATCH",
            PatientId = patientId,
            DepartmentId = dept1Id,
            DoctorId = doc1Id,
            RoomId = room1Id,
            BookingChannel = BookingChannel.Web,
            StatusId = (int)AppointmentStatusId.Confirmed,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            UpdatedAt = clock.GetUtcNow().UtcDateTime
        };
        context.Appointments.Add(app1);
        await context.SaveChangesAsync();

        var result = await reception.GetDeskAsync(nurseAccountId, "0912345678");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value.Visits);
        Assert.Equal("HIS-20261109-MATCH", result.Value.Visits[0].Code);
    }

    [Fact]
    public async Task CheckInAsync_WithoutIdentityVerified_ReturnsFailure()
    {
        var (context, clock, reception, nurseAccountId, dept1Id, doc1Id, room1Id, _, patientId) = CreateHarness();

        var app = new Appointment
        {
            AppointmentCode = "HIS-20261109-CHK1",
            PatientId = patientId,
            DepartmentId = dept1Id,
            DoctorId = doc1Id,
            RoomId = room1Id,
            BookingChannel = BookingChannel.Web,
            StatusId = (int)AppointmentStatusId.Confirmed,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            UpdatedAt = clock.GetUtcNow().UtcDateTime
        };
        context.Appointments.Add(app);
        await context.SaveChangesAsync();

        var result = await reception.CheckInAsync(nurseAccountId, app.Id, identityVerified: false);

        Assert.False(result.IsSuccess);
        Assert.Equal("NOT_VERIFIED", result.Error.Code);
    }

    [Fact]
    public async Task CheckInAsync_WithIdentityVerified_IssuesQueueNumberAndSetsCheckedIn()
    {
        var (context, clock, reception, nurseAccountId, dept1Id, doc1Id, room1Id, _, patientId) = CreateHarness();

        var slot = new AppointmentSlot
        {
            DoctorId = doc1Id,
            RoomId = room1Id,
            StartTime = new DateTime(2026, 11, 9, 8, 30, 0),
            EndTime = new DateTime(2026, 11, 9, 8, 45, 0),
            Capacity = 1,
            BookedCount = 1,
            Status = SlotStatus.Available
        };
        context.AppointmentSlots.Add(slot);
        await context.SaveChangesAsync();

        var app = new Appointment
        {
            AppointmentCode = "HIS-20261109-CHK2",
            PatientId = patientId,
            DepartmentId = dept1Id,
            DoctorId = doc1Id,
            RoomId = room1Id,
            SlotId = slot.Id,
            BookingChannel = BookingChannel.Web,
            StatusId = (int)AppointmentStatusId.Confirmed,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            UpdatedAt = clock.GetUtcNow().UtcDateTime
        };
        context.Appointments.Add(app);
        await context.SaveChangesAsync();

        var result = await reception.CheckInAsync(nurseAccountId, app.Id, identityVerified: true);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value!);

        var updated = await context.Appointments.FindAsync(app.Id);
        Assert.NotNull(updated);
        Assert.Equal((int)AppointmentStatusId.CheckedIn, updated.StatusId);
    }

    [Fact]
    public async Task RegisterWalkInAsync_WithoutIdentityVerified_ReturnsFailure()
    {
        var (_, _, reception, nurseAccountId, _, _, _, scheduleId, _) = CreateHarness();

        var req = new WalkInReceptionRequest
        {
            ScheduleId = scheduleId,
            IdentityVerified = false,
            FullName = "Vãng Lai"
        };

        var result = await reception.RegisterWalkInAsync(nurseAccountId, req);

        Assert.False(result.IsSuccess);
        Assert.Equal("NOT_VERIFIED", result.Error.Code);
    }

    [Fact]
    public async Task RegisterWalkInAsync_ValidWalkInRequest_CreatesAppointmentAndIssuesQueueNumber()
    {
        var (context, _, reception, nurseAccountId, _, doc1Id, room1Id, scheduleId, _) = CreateHarness();

        var slot = new AppointmentSlot
        {
            DoctorId = doc1Id,
            RoomId = room1Id,
            StartTime = new DateTime(2026, 11, 9, 9, 0, 0),
            EndTime = new DateTime(2026, 11, 9, 9, 15, 0),
            Capacity = 2,
            OnlineQuota = 1,
            BookedCount = 0,
            Status = SlotStatus.Available
        };
        context.AppointmentSlots.Add(slot);
        await context.SaveChangesAsync();

        var req = new WalkInReceptionRequest
        {
            ScheduleId = scheduleId,
            IdentityVerified = true,
            FullName = "Trần Khắc Vãng Lai",
            DateOfBirth = new DateOnly(1985, 4, 10),
            Gender = Gender.Male,
            PhoneNumber = "0988776655",
            NationalId = "034085001234",
            Reason = "Đau đầu chóng mặt",
            PriorityLevel = 0
        };

        var result = await reception.RegisterWalkInAsync(nurseAccountId, req);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value.QueueNumber);

        var created = await context.Appointments
            .Include(a => a.Patient)
            .FirstOrDefaultAsync(a => a.Patient.PhoneNumber == "0988776655");

        Assert.NotNull(created);
        Assert.Equal(BookingChannel.OnSite, created.BookingChannel);
        Assert.Equal(VisitType.WalkIn, created.VisitType);
        Assert.Equal((int)AppointmentStatusId.CheckedIn, created.StatusId);
        Assert.Equal("Trần Khắc Vãng Lai", created.Patient.FullName);
    }

    [Fact]
    public async Task FindPatientByNationalIdAsync_ExistingCccd_ReturnsPatient()
    {
        var (context, _, reception, _, _, _, _, _, patientId) = CreateHarness();

        var result = await reception.FindPatientByNationalIdAsync("001090012345");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Nguyễn Văn Hùng", result.Value.FullName);
        Assert.Equal("0912345678", result.Value.PhoneNumber);
    }

    [Fact]
    public async Task RegisterWalkInAsync_ChildWithGuardian_SetsReasonAndGuardianPhone()
    {
        var (context, _, reception, nurseAccountId, _, doc1Id, room1Id, scheduleId, _) = CreateHarness();

        var slot = new AppointmentSlot
        {
            DoctorId = doc1Id,
            RoomId = room1Id,
            StartTime = new DateTime(2026, 11, 9, 9, 0, 0),
            EndTime = new DateTime(2026, 11, 9, 9, 15, 0),
            Capacity = 2,
            OnlineQuota = 1,
            BookedCount = 0,
            Status = SlotStatus.Available
        };
        context.AppointmentSlots.Add(slot);
        await context.SaveChangesAsync();

        var req = new WalkInReceptionRequest
        {
            ScheduleId = scheduleId,
            IdentityVerified = true,
            IsChild = true,
            FullName = "Bé Nguyễn Văn Tí",
            DateOfBirth = new DateOnly(2020, 5, 1),
            Gender = Gender.Male,
            GuardianName = "Nguyễn Văn Bố",
            GuardianRelationship = "Bố",
            GuardianPhone = "0977889900",
            GuardianNationalId = "001201009988",
            Reason = "Sốt cao 39 độ",
            PriorityLevel = 1
        };

        var result = await reception.RegisterWalkInAsync(nurseAccountId, req);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);

        var app = await context.Appointments
            .Include(a => a.Patient)
            .FirstOrDefaultAsync(a => a.Patient.FullName == "Bé Nguyễn Văn Tí");

        Assert.NotNull(app);
        Assert.Contains("Nguyễn Văn Bố", app.Reason);
        Assert.Contains("0977889900", app.Reason);
        Assert.Equal("0977889900", app.Patient.PhoneNumber);
    }

    [Fact]
    public async Task ValidateNewProfileAsync_WhenEmailReachesQuota_RejectsWithQuotaError()
    {
        var (context, _, _, nurseAccountId, _, _, _, _, _) = CreateHarness();
        var bookingOpts = Options.Create(new BookingOptions { MaxProfilesPerEmail = 3 });
        var profileService = new PatientProfileService(context, bookingOpts);

        // Thêm 3 bệnh nhân đã dùng email family@example.com
        for (int i = 1; i <= 3; i++)
        {
            context.Patients.Add(new Patient
            {
                FullName = $"Người nhà {i}",
                DateOfBirth = new DateOnly(2010, 1, i),
                Gender = Gender.Male,
                PhoneNumber = $"098765432{i}",
                Email = "family@example.com",
                CreatedAt = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync();

        var req = new CreateProfileRequest
        {
            FullName = "Người nhà thứ 4",
            DateOfBirth = new DateOnly(2012, 5, 5),
            Gender = "MALE",
            PhoneNumber = "0987111222",
            Email = "family@example.com",
            Relationship = "CHILD",
            GuardianNationalId = "001090012345"
        };

        var err = await profileService.ValidateNewProfileAsync(nurseAccountId, req);

        Assert.NotNull(err);
        Assert.Contains("tối đa 3 hồ sơ", err);
    }

    [Fact]
    public async Task RegisterWalkInAsync_WrongDepartmentDoctor_ReturnsFailure()
    {
        var (context, _, reception, nurseAccountId, _, doc1Id, room1Id, scheduleId, _) = CreateHarness();

        var otherDept = new Department { Name = "Khoa Nhi", Description = "Khoa Nhi" };
        context.Departments.Add(otherDept);
        await context.SaveChangesAsync();

        var req = new WalkInReceptionRequest
        {
            ScheduleId = scheduleId, // Thuộc Khoa Nội (Department 1)
            DepartmentId = otherDept.Id, // Nhưng yêu cầu Khoa Nhi (mismatch)
            IdentityVerified = true,
            FullName = "Phan Văn Nam",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = Gender.Male,
            PhoneNumber = "0911223344",
            Reason = "Khám sức khỏe",
            PriorityLevel = 0
        };

        var result = await reception.RegisterWalkInAsync(nurseAccountId, req);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_DEPARTMENT", result.Error.Code);
    }

    [Fact]
    public async Task RegisterWalkInAsync_ChildModeWithStaleAdultPatientId_ReconcilesToNewPatient()
    {
        var (context, _, reception, nurseAccountId, _, doc1Id, room1Id, scheduleId, adultPatientId) = CreateHarness();

        // adultPatientId là người lớn "Nguyễn Văn Hùng", sinh năm 1990
        var slot = new AppointmentSlot
        {
            DoctorId = doc1Id,
            RoomId = room1Id,
            StartTime = new DateTime(2026, 11, 9, 10, 0, 0),
            EndTime = new DateTime(2026, 11, 9, 10, 15, 0),
            Capacity = 2,
            OnlineQuota = 1,
            BookedCount = 0,
            Status = SlotStatus.Available
        };
        context.AppointmentSlots.Add(slot);
        await context.SaveChangesAsync();

        var req = new WalkInReceptionRequest
        {
            PatientId = adultPatientId, // ID người lớn còn sót lại từ tra cứu trước
            ScheduleId = scheduleId,
            IdentityVerified = true,
            IsChild = true, // Nhưng đăng ký cho trẻ em
            FullName = "Bé Nguyễn Văn Tí Nị",
            DateOfBirth = new DateOnly(2022, 1, 1),
            Gender = Gender.Male,
            GuardianName = "Nguyễn Văn Bố",
            GuardianPhone = "0988001122",
            Reason = "Sốt mọc răng",
            PriorityLevel = 0
        };

        var result = await reception.RegisterWalkInAsync(nurseAccountId, req);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);

        // Kiểm tra rằng hồ sơ người lớn Nguyễn Văn Hùng KHÔNG bị ghi đè thành Bé Tí Nị
        var adult = await context.Patients.FindAsync(adultPatientId);
        Assert.NotNull(adult);
        Assert.Equal("Nguyễn Văn Hùng", adult.FullName);

        // Kiểm tra rằng một hồ sơ bệnh nhân mới đã được tạo cho Bé Tí Nị
        var child = await context.Patients.FirstOrDefaultAsync(p => p.FullName == "Bé Nguyễn Văn Tí Nị");
        Assert.NotNull(child);
        Assert.NotEqual(adultPatientId, child.Id);
    }

    [Fact]
    public async Task RegisterWalkInAsync_WhenEmailExceedsQuota_ReturnsValidationError()
    {
        var (context, _, reception, nurseAccountId, _, doc1Id, room1Id, scheduleId, _) = CreateHarness();

        // Đã có 3 bệnh nhân dùng chung email shared@hospital.vn
        for (int i = 1; i <= 3; i++)
        {
            context.Patients.Add(new Patient
            {
                FullName = $"Bệnh nhân cũ {i}",
                DateOfBirth = new DateOnly(1990, 1, i),
                Gender = Gender.Male,
                PhoneNumber = $"091200000{i}",
                Email = "shared@hospital.vn",
                CreatedAt = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync();

        var slot = new AppointmentSlot
        {
            DoctorId = doc1Id,
            RoomId = room1Id,
            StartTime = new DateTime(2026, 11, 9, 11, 0, 0),
            EndTime = new DateTime(2026, 11, 9, 11, 15, 0),
            Capacity = 2,
            OnlineQuota = 1,
            BookedCount = 0,
            Status = SlotStatus.Available
        };
        context.AppointmentSlots.Add(slot);
        await context.SaveChangesAsync();

        var req = new WalkInReceptionRequest
        {
            ScheduleId = scheduleId,
            IdentityVerified = true,
            FullName = "Người khám thứ 4",
            DateOfBirth = new DateOnly(1995, 5, 5),
            Gender = Gender.Female,
            PhoneNumber = "0944555666",
            NationalId = "001195000004",
            Email = "shared@hospital.vn",
            Reason = "Đau họng",
            PriorityLevel = 0
        };

        var result = await reception.RegisterWalkInAsync(nurseAccountId, req);

        Assert.False(result.IsSuccess);
        Assert.Equal("VALIDATION_ERROR", result.Error.Code);
        Assert.Contains("tối đa 3 hồ sơ", result.Error.Message);
    }

    [Fact]
    public async Task RegisterWalkInAsync_WithInsuranceCardBelongingToAnotherPatient_ReturnsValidationError()
    {
        var (context, _, reception, nurseAccountId, _, doc1Id, room1Id, scheduleId, existingPatientId) = CreateHarness();

        // Thêm một bệnh nhân khác sở hữu thẻ BHYT GD4010123456789
        var anotherPatient = new Patient
        {
            FullName = "Nguyễn Văn Chủ Thẻ",
            DateOfBirth = new DateOnly(1985, 3, 10),
            Gender = Gender.Male,
            PhoneNumber = "0901234567",
            NationalId = "001185099999",
            CreatedAt = DateTime.UtcNow
        };
        context.Patients.Add(anotherPatient);
        await context.SaveChangesAsync();

        var plan = new InsurancePlan { Name = "BHYT Chuẩn", CoveragePct = 80m };
        context.InsurancePlans.Add(plan);
        await context.SaveChangesAsync();

        context.InsuranceCards.Add(new InsuranceCard
        {
            PatientId = anotherPatient.Id,
            InsurancePlanId = plan.Id,
            CardNumber = "GD4010123456789",
            ValidFrom = new DateOnly(2025, 1, 1),
            ValidTo = new DateOnly(2027, 12, 31),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var slot = new AppointmentSlot
        {
            DoctorId = doc1Id,
            RoomId = room1Id,
            StartTime = new DateTime(2026, 11, 9, 11, 0, 0),
            EndTime = new DateTime(2026, 11, 9, 11, 15, 0),
            Capacity = 2,
            OnlineQuota = 1,
            BookedCount = 0,
            Status = SlotStatus.Available
        };
        context.AppointmentSlots.Add(slot);
        await context.SaveChangesAsync();

        // Bệnh nhân mới cố tình đăng ký với thẻ BHYT của người khác
        var req = new WalkInReceptionRequest
        {
            ScheduleId = scheduleId,
            IdentityVerified = true,
            FullName = "Bệnh nhân chiếm dụng thẻ",
            DateOfBirth = new DateOnly(2000, 1, 1),
            Gender = Gender.Male,
            PhoneNumber = "0988777666",
            NationalId = "001200088888",
            InsuranceCardNumber = "GD4010123456789",
            Reason = "Khám sốt",
            PriorityLevel = 0
        };

        var result = await reception.RegisterWalkInAsync(nurseAccountId, req);

        Assert.False(result.IsSuccess);
        Assert.Equal("VALIDATION_ERROR", result.Error.Code);
        Assert.Contains("đã được liên kết với một bệnh nhân khác", result.Error.Message);
    }

    [Fact]
    public async Task RegisterWalkInAsync_WithoutNationalIdForAdult_ReturnsValidationError()
    {
        var (context, _, reception, nurseAccountId, _, doc1Id, room1Id, scheduleId, _) = CreateHarness();

        var req = new WalkInReceptionRequest
        {
            ScheduleId = scheduleId,
            IdentityVerified = true,
            FullName = "Người Lớn Không CCCD",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = Gender.Male,
            PhoneNumber = "0988112233",
            NationalId = null, // Thiếu CCCD cho người lớn
            Reason = "Đau lưng",
            PriorityLevel = 0
        };

        var result = await reception.RegisterWalkInAsync(nurseAccountId, req);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_NATIONAL_ID", result.Error.Code);
    }

    [Fact]
    public async Task RegisterWalkInAsync_ScheduleWrongDay_ReturnsInvalidSchedule()
    {
        var (context, _, reception, nurseAccountId, _, doc1Id, room1Id, _, _) = CreateHarness();

        // Harness có clock là Thứ 2 (2026-11-09). Tạo lịch khám cho ngày Chủ Nhật (Sunday)
        var sundaySchedule = new DoctorSchedule
        {
            DoctorId = doc1Id,
            RoomId = room1Id,
            DayOfWeek = DayOfWeek.Sunday,
            Shift = ExamShift.Morning,
            StartTime = new TimeOnly(7, 30),
            EndTime = new TimeOnly(11, 30),
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true
        };
        context.DoctorSchedules.Add(sundaySchedule);
        await context.SaveChangesAsync();

        var req = new WalkInReceptionRequest
        {
            ScheduleId = sundaySchedule.Id,
            IdentityVerified = true,
            FullName = "Bệnh Nhân Thử Lịch Sai Ngày",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = Gender.Male,
            PhoneNumber = "0988112233",
            NationalId = "001190001122",
            Reason = "Khám",
            PriorityLevel = 0
        };

        var result = await reception.RegisterWalkInAsync(nurseAccountId, req);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_SCHEDULE", result.Error.Code);
    }

    [Fact]
    public async Task UpsertInsuranceCardAsync_WithNewCardNumber_DeactivatesOldCardAndCreatesNewOne()
    {
        var (context, _, reception, nurseAccountId, _, doc1Id, room1Id, scheduleId, existingPatientId) = CreateHarness();

        var plan = new InsurancePlan { Name = "BHYT Chuẩn", CoveragePct = 80m };
        context.InsurancePlans.Add(plan);
        await context.SaveChangesAsync();

        // Bệnh nhân đã có 1 thẻ BHYT cũ đang active
        var oldCard = new InsuranceCard
        {
            PatientId = existingPatientId,
            InsurancePlanId = plan.Id,
            CardNumber = "OLD1234567890",
            ValidFrom = new DateOnly(2024, 1, 1),
            ValidTo = new DateOnly(2024, 12, 31),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        context.InsuranceCards.Add(oldCard);
        await context.SaveChangesAsync();

        var slot = new AppointmentSlot
        {
            DoctorId = doc1Id,
            RoomId = room1Id,
            StartTime = new DateTime(2026, 11, 9, 11, 0, 0),
            EndTime = new DateTime(2026, 11, 9, 11, 15, 0),
            Capacity = 2,
            OnlineQuota = 1,
            BookedCount = 0,
            Status = SlotStatus.Available
        };
        context.AppointmentSlots.Add(slot);
        await context.SaveChangesAsync();

        // Đăng ký với thẻ mới
        var req = new WalkInReceptionRequest
        {
            PatientId = existingPatientId,
            ScheduleId = scheduleId,
            IdentityVerified = true,
            FullName = "Nguyễn Văn Hùng", // Khớp đúng với patient trong harness
            DateOfBirth = new DateOnly(1990, 3, 15),
            Gender = Gender.Male,
            PhoneNumber = "0912345678",
            NationalId = "001090012345",
            InsuranceCardNumber = "NEW9876543210",
            Reason = "Khám định kỳ",
            PriorityLevel = 0
        };

        var result = await reception.RegisterWalkInAsync(nurseAccountId, req);

        Assert.True(result.IsSuccess);

        // Kiểm tra thẻ cũ đã bị tắt active, thẻ mới được tạo riêng biệt
        var cards = await context.InsuranceCards.Where(c => c.PatientId == existingPatientId).ToListAsync();
        Assert.Equal(2, cards.Count);

        var updatedOldCard = cards.First(c => c.CardNumber == "OLD1234567890");
        Assert.False(updatedOldCard.IsActive);

        var newCard = cards.First(c => c.CardNumber == "NEW9876543210");
        Assert.True(newCard.IsActive);
        Assert.Equal(new DateOnly(2026, 1, 1), newCard.ValidFrom);
    }
}

