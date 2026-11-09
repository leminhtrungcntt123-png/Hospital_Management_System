using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HIS.Tests;

public class DoctorPortalPart3Tests
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
        QueueService Queue,
        DoctorService DoctorSvc,
        DepartmentHeadService DeptHeadSvc,
        int DeptHeadAccountId,
        int Doctor1AccountId,
        int Doctor1Id,
        int Doctor2AccountId,
        int Doctor2Id,
        int NurseAccountId,
        int Dept1Id,
        int Room1Id,
        int PatientId
    ) CreateHarness()
    {
        var clock = new MutableTimeProvider(new DateTime(2026, 11, 7, 8, 0, 0, DateTimeKind.Utc));
        var dbName = "DoctorPortalTest_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var context = new AppDbContext(options, clock);

        context.Roles.AddRange(
            new Role { Id = (int)RoleId.Admin, Code = "ADMIN", Name = "Quản trị viên" },
            new Role { Id = (int)RoleId.Doctor, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = (int)RoleId.Nurse, Code = "NURSE", Name = "Điều dưỡng" },
            new Role { Id = (int)RoleId.DepartmentHead, Code = "DEPT_HEAD", Name = "Trưởng khoa" },
            new Role { Id = (int)RoleId.Patient, Code = "PATIENT", Name = "Bệnh nhân" }
        );

        var dept1 = new Department { Name = "Khoa Nội tổng quát", Description = "Nội tổng quát" };
        var dept2 = new Department { Name = "Khoa Ngoại", Description = "Ngoại lồng ngực" };
        context.Departments.AddRange(dept1, dept2);
        context.SaveChanges();

        var room1 = new ClinicRoom { DepartmentId = dept1.Id, RoomNumber = "P101", RoomName = "Phòng Khám Nội 1" };
        context.ClinicRooms.Add(room1);
        context.SaveChanges();

        // Dept Head
        var deptHeadAccount = new Account
        {
            Username = "dept_head_thang",
            Email = "thang.head@hospital.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("HeadPass123!"),
            RoleId = (int)RoleId.DepartmentHead,
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1),
            UpdatedAt = new DateTime(2026, 1, 1)
        };
        context.Accounts.Add(deptHeadAccount);
        context.SaveChanges();

        var deptHead = new DepartmentHead
        {
            AccountId = deptHeadAccount.Id,
            DepartmentId = dept1.Id,
            FullName = "BS CKII. Nguyễn Văn Thắng"
        };
        context.DepartmentHeads.Add(deptHead);

        // Doctor 1 (Dept 1)
        var doc1Account = new Account
        {
            Username = "doctor_minh",
            Email = "minh.doc@hospital.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("DocPass123!"),
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
            FullName = "ThS. BS Lê Hoàng Minh",
            LicenseNumber = "CCHN-00123"
        };
        context.Doctors.Add(doc1);

        // Doctor 2 (Dept 2)
        var doc2Account = new Account
        {
            Username = "doctor_hai",
            Email = "hai.doc@hospital.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("DocPass123!"),
            RoleId = (int)RoleId.Doctor,
            IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1),
            UpdatedAt = new DateTime(2026, 1, 1)
        };
        context.Accounts.Add(doc2Account);
        context.SaveChanges();

        var doc2 = new Doctor
        {
            AccountId = doc2Account.Id,
            DepartmentId = dept2.Id,
            FullName = "BS. Trần Đức Hải",
            LicenseNumber = "CCHN-00456"
        };
        context.Doctors.Add(doc2);

        // Nurse (Dept 1)
        var nurseAccount = new Account
        {
            Username = "nurse_lan",
            Email = "lan.nurse@hospital.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("NursePass123!"),
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

        // Patient
        var patient = new Patient
        {
            FullName = "Vũ Mạnh Hùng",
            DateOfBirth = new DateOnly(1988, 5, 20),
            Gender = Gender.Male,
            PhoneNumber = "0987654321",
            NationalId = "001088012345",
            CreatedAt = new DateTime(2026, 1, 1)
        };
        context.Patients.Add(patient);
        context.SaveChanges();

        var queue = new QueueService(context, clock);
        var doctorSvc = new DoctorService(context, clock);
        var deptHeadSvc = new DepartmentHeadService(context, clock);

        return (
            context,
            clock,
            queue,
            doctorSvc,
            deptHeadSvc,
            deptHeadAccount.Id,
            doc1Account.Id,
            doc1.Id,
            doc2Account.Id,
            doc2.Id,
            nurseAccount.Id,
            dept1.Id,
            room1.Id,
            patient.Id
        );
    }

    [Fact]
    public async Task CallNextPatientAsync_ValidWaitingAppointment_TransitionsToInProgress()
    {
        var (context, clock, queue, _, _, _, doc1AccountId, doc1Id, _, _, _, dept1Id, room1Id, patientId) = CreateHarness();

        var app = new Appointment
        {
            AppointmentCode = "KB-20261107-001",
            PatientId = patientId,
            DepartmentId = dept1Id,
            DoctorId = doc1Id,
            RoomId = room1Id,
            QueueNumber = "001",
            PriorityLevel = 0,
            StatusId = (int)AppointmentStatusId.ReadyForDoctor,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            UpdatedAt = clock.GetUtcNow().UtcDateTime
        };
        context.Appointments.Add(app);
        await context.SaveChangesAsync();

        var result = await queue.CallNextPatientAsync(roomId: room1Id, doctorAccountId: doc1AccountId);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);

        var updated = await context.Appointments.FindAsync(app.Id);
        Assert.NotNull(updated);
        Assert.Equal((int)AppointmentStatusId.InProgress, updated.StatusId);
    }

    [Fact]
    public async Task CallNextPatientAsync_NoWaitingPatients_ReturnsFailure()
    {
        var (_, _, queue, _, _, _, doc1AccountId, _, _, _, _, _, room1Id, _) = CreateHarness();

        var result = await queue.CallNextPatientAsync(roomId: room1Id, doctorAccountId: doc1AccountId);

        Assert.False(result.IsSuccess);
        Assert.Equal("NO_PATIENT", result.Error.Code);
    }

    [Fact]
    public async Task CreateMedicalRecordAsync_ValidDoctor_CreatesRecordAndCompletesAppointment()
    {
        var (context, clock, _, doctorSvc, _, _, _, doc1Id, _, _, _, dept1Id, room1Id, patientId) = CreateHarness();

        var app = new Appointment
        {
            AppointmentCode = "KB-20261107-002",
            PatientId = patientId,
            DepartmentId = dept1Id,
            DoctorId = doc1Id,
            RoomId = room1Id,
            StatusId = (int)AppointmentStatusId.InProgress,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            UpdatedAt = clock.GetUtcNow().UtcDateTime
        };
        context.Appointments.Add(app);
        await context.SaveChangesAsync();

        var req = new CreateMedicalRecordRequest(
            app.Id,
            doc1Id,
            "Sốt cao 39 độ, đau rát họng, mệt mỏi",
            "Họng đỏ xuất tiết, không có giả mạc",
            "J02.9",
            "Viêm họng cấp không xác định",
            "Kê đơn kháng sinh, hạ sốt, dặn uống nhiều nước và tái khám sau 3 ngày"
        );

        var recordId = await doctorSvc.CreateMedicalRecordAsync(req);

        Assert.True(recordId > 0);

        var record = await context.MedicalRecords.FindAsync(recordId);
        Assert.NotNull(record);
        Assert.Equal("J02.9", record.PrimaryDiagnosisCode);
        Assert.Equal("Viêm họng cấp không xác định", record.PrimaryDiagnosisDesc);
        Assert.Equal("Sốt cao 39 độ, đau rát họng, mệt mỏi", record.Symptoms);

        var updatedApp = await context.Appointments.FindAsync(app.Id);
        Assert.NotNull(updatedApp);
        Assert.Equal((int)AppointmentStatusId.Completed, updatedApp.StatusId);
    }

    [Fact]
    public async Task CreateMedicalRecordAsync_DoctorFromDifferentDepartment_ThrowsInvalidOperationException()
    {
        var (context, clock, _, doctorSvc, _, _, _, _, _, doc2Id, _, dept1Id, room1Id, patientId) = CreateHarness();

        var app = new Appointment
        {
            AppointmentCode = "KB-20261107-003",
            PatientId = patientId,
            DepartmentId = dept1Id, // Dept 1
            RoomId = room1Id,
            StatusId = (int)AppointmentStatusId.InProgress,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            UpdatedAt = clock.GetUtcNow().UtcDateTime
        };
        context.Appointments.Add(app);
        await context.SaveChangesAsync();

        // Doctor 2 belongs to Dept 2, attempting to create record for appointment in Dept 1
        var req = new CreateMedicalRecordRequest(
            app.Id,
            doc2Id,
            "Đau ngực",
            "Khám bình thường",
            "R07.4",
            "Đau ngực",
            "Nghỉ ngơi"
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => doctorSvc.CreateMedicalRecordAsync(req));
        Assert.Contains("khoa khác", ex.Message);
    }

    [Fact]
    public async Task GetQueuePatientsAsync_FiltersByDoctorDateAndShift()
    {
        var (context, clock, _, doctorSvc, _, _, _, doc1Id, _, _, _, dept1Id, room1Id, patientId) = CreateHarness();

        var morningSlot = new AppointmentSlot
        {
            DoctorId = doc1Id,
            RoomId = room1Id,
            StartTime = new DateTime(2026, 11, 7, 8, 30, 0),
            EndTime = new DateTime(2026, 11, 7, 8, 45, 0),
            Capacity = 1,
            BookedCount = 1,
            Status = SlotStatus.Full
        };
        var afternoonSlot = new AppointmentSlot
        {
            DoctorId = doc1Id,
            RoomId = room1Id,
            StartTime = new DateTime(2026, 11, 7, 14, 0, 0),
            EndTime = new DateTime(2026, 11, 7, 14, 15, 0),
            Capacity = 1,
            BookedCount = 1,
            Status = SlotStatus.Full
        };
        context.AppointmentSlots.AddRange(morningSlot, afternoonSlot);
        await context.SaveChangesAsync();

        var appMorning = new Appointment
        {
            AppointmentCode = "KB-20261107-M1",
            PatientId = patientId,
            DepartmentId = dept1Id,
            DoctorId = doc1Id,
            SlotId = morningSlot.Id,
            QueueNumber = "001",
            PriorityLevel = 0,
            StatusId = (int)AppointmentStatusId.CheckedIn,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            UpdatedAt = clock.GetUtcNow().UtcDateTime
        };
        var appAfternoon = new Appointment
        {
            AppointmentCode = "KB-20261107-A1",
            PatientId = patientId,
            DepartmentId = dept1Id,
            DoctorId = doc1Id,
            SlotId = afternoonSlot.Id,
            QueueNumber = "002",
            PriorityLevel = 0,
            StatusId = (int)AppointmentStatusId.CheckedIn,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            UpdatedAt = clock.GetUtcNow().UtcDateTime
        };
        context.Appointments.AddRange(appMorning, appAfternoon);
        await context.SaveChangesAsync();

        var morningQueue = await doctorSvc.GetQueuePatientsAsync(
            doc1Id,
            new DateOnly(2026, 11, 7),
            ExamShift.Morning
        );

        Assert.Single(morningQueue);
        Assert.Equal("KB-20261107-M1", morningQueue[0].AppointmentCode);
    }

    [Fact]
    public async Task ApproveExceptionAsync_ValidPendingRequest_ApprovesException()
    {
        var (context, clock, _, _, deptHeadSvc, deptHeadAccountId, _, doc1Id, _, _, _, dept1Id, _, _) = CreateHarness();

        var exception = new ScheduleException
        {
            DoctorId = doc1Id,
            Date = new DateOnly(2026, 11, 10),
            Shift = ExamShift.Morning,
            Type = ScheduleExceptionType.Leave,
            Status = ScheduleExceptionStatus.Pending,
            Reason = "Nghỉ phép thường niên",
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };
        context.ScheduleExceptions.Add(exception);
        await context.SaveChangesAsync();

        var result = await deptHeadSvc.ApproveExceptionAsync(
            new ApproveExceptionRequest(exception.Id, deptHeadAccountId, dept1Id)
        );

        Assert.True(result.IsSuccess);

        var updated = await context.ScheduleExceptions.FindAsync(exception.Id);
        Assert.NotNull(updated);
        Assert.Equal(ScheduleExceptionStatus.Approved, updated.Status);
        Assert.Equal(deptHeadAccountId, updated.ApprovedBy);
    }

    [Fact]
    public async Task AssignDutyShiftAsync_ValidFutureShift_Succeeds()
    {
        var (context, _, _, _, deptHeadSvc, deptHeadAccountId, _, _, _, _, nurseAccountId, dept1Id, _, _) = CreateHarness();

        var start = new DateTime(2026, 11, 8, 21, 0, 0);
        var end = new DateTime(2026, 11, 9, 6, 0, 0);

        var req = new AssignDutyShiftRequest(
            nurseAccountId,
            dept1Id,
            start,
            end,
            "Trực đêm tại khoa Nội",
            deptHeadAccountId
        );

        var result = await deptHeadSvc.AssignDutyShiftAsync(req);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value > 0);

        var duty = await context.DutyShifts.FindAsync(result.Value);
        Assert.NotNull(duty);
        Assert.Equal(nurseAccountId, duty.AccountId);
        Assert.Equal("Trực đêm tại khoa Nội", duty.Note);
    }

    [Fact]
    public async Task AssignDutyShiftAsync_DuplicateShiftOnSameDay_ReturnsFailure()
    {
        var (_, _, _, _, deptHeadSvc, deptHeadAccountId, _, _, _, _, nurseAccountId, dept1Id, _, _) = CreateHarness();

        var start1 = new DateTime(2026, 11, 8, 20, 0, 0);
        var end1 = new DateTime(2026, 11, 9, 6, 0, 0);

        var req1 = new AssignDutyShiftRequest(
            nurseAccountId,
            dept1Id,
            start1,
            end1,
            "Ca 1",
            deptHeadAccountId
        );

        var res1 = await deptHeadSvc.AssignDutyShiftAsync(req1);
        Assert.True(res1.IsSuccess);

        var start2 = new DateTime(2026, 11, 8, 22, 0, 0);
        var end2 = new DateTime(2026, 11, 9, 7, 0, 0);

        var req2 = new AssignDutyShiftRequest(
            nurseAccountId,
            dept1Id,
            start2,
            end2,
            "Ca 2 trùng",
            deptHeadAccountId
        );

        var res2 = await deptHeadSvc.AssignDutyShiftAsync(req2);
        Assert.False(res2.IsSuccess);
        Assert.Equal("DUPLICATE_DUTY", res2.Error.Code);
    }

    [Fact]
    public async Task AssignDutyShiftAsync_PastTime_ReturnsFailure()
    {
        var (_, _, _, _, deptHeadSvc, deptHeadAccountId, _, _, _, _, nurseAccountId, dept1Id, _, _) = CreateHarness();

        var start = new DateTime(2026, 11, 6, 21, 0, 0); // Before harness clock 2026-11-07
        var end = new DateTime(2026, 11, 7, 6, 0, 0);

        var req = new AssignDutyShiftRequest(
            nurseAccountId,
            dept1Id,
            start,
            end,
            "Ca trực quá khứ",
            deptHeadAccountId
        );

        var res = await deptHeadSvc.AssignDutyShiftAsync(req);
        Assert.False(res.IsSuccess);
        Assert.Equal("PAST_TIME", res.Error.Code);
    }

    [Fact]
    public async Task UpsertDoctorScheduleAsync_ValidNewSchedule_Succeeds()
    {
        var (context, _, _, _, deptHeadSvc, _, _, doc1Id, _, _, _, _, room1Id, _) = CreateHarness();

        var req = new UpsertDoctorScheduleRequest(
            ScheduleId: null,
            DoctorId: doc1Id,
            RoomId: room1Id,
            DayOfWeek: DayOfWeek.Monday,
            Shift: ExamShift.Morning,
            StartTime: new TimeOnly(7, 30),
            EndTime: new TimeOnly(11, 30),
            SlotDurationMinutes: 15,
            MaxPatients: 40,
            OnlineSlots: 24,
            EffectiveFrom: new DateOnly(2026, 11, 9),
            EffectiveTo: null
        );

        var res = await deptHeadSvc.UpsertDoctorScheduleAsync(req);

        Assert.True(res.IsSuccess);
        Assert.True(res.Value > 0);

        var schedule = await context.DoctorSchedules.FindAsync(res.Value);
        Assert.NotNull(schedule);
        Assert.Equal(DayOfWeek.Monday, schedule.DayOfWeek);
        Assert.Equal(ExamShift.Morning, schedule.Shift);
        Assert.Equal(60, schedule.OnlineRatioPct); // 24 / 40 = 60%
    }
}
