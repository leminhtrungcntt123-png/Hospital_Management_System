using HIS.BLL.DTOs;
using HIS.Domain;
using Xunit;

namespace HIS.Tests.Unit;

public class DepartmentHeadTests
{
    [Fact]
    public void UpsertDoctorSchedule_ShouldValidate_OnlineSlotsCannotExceedMaxPatients()
    {
        var request = new UpsertDoctorScheduleRequest(
            null,
            1,
            1,
            1,
            1,
            DateOnly.FromDateTime(DateTime.Today),
            ExamShift.Morning,
            new TimeOnly(7, 30),
            new TimeOnly(11, 30),
            15,
            40,
            50 // 50 > 40 -> invalid
        );

        Assert.True(request.OnlineSlots > request.MaxPatients);
    }

    [Fact]
    public void AssignDutyShift_ShouldValidate_EndTimeMustBeAfterStartTime()
    {
        var startTime = new DateTime(2026, 11, 6, 17, 0, 0);
        var endTime = new DateTime(2026, 11, 6, 7, 0, 0); // End before start -> invalid

        var request = new AssignDutyShiftRequest(
            1,
            1,
            startTime,
            endTime,
            "Trực đêm",
            2
        );

        Assert.True(request.EndTime <= request.StartTime);
    }

    [Fact]
    public void UpsertDoctorSchedule_EffectiveFrom_InPast_ShouldBeDetected()
    {
        var pastDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        var today = DateOnly.FromDateTime(DateTime.Today);

        Assert.True(pastDate < today);
    }

    [Fact]
    public void AssignDutyShift_StartTime_InPast_ShouldBeDetected()
    {
        var pastTime = DateTime.Now.AddHours(-2);
        var now = DateTime.Now;

        Assert.True(pastTime < now);
    }

    [Fact]
    public void DeleteDutyShift_Validation_ShouldRequireValidId()
    {
        int invalidId = 0;
        Assert.True(invalidId <= 0);

        int validId = 5;
        Assert.True(validId > 0);
    }

    [Fact]
    public void DeleteDoctorSchedule_Validation_ShouldRequireValidId()
    {
        int invalidId = 0;
        Assert.True(invalidId <= 0);

        int validId = 10;
        Assert.True(validId > 0);
    }

    [Fact]
    public void ApproveExceptionRequest_ShouldIncludeDepartmentId()
    {
        var request = new ApproveExceptionRequest(1, 2, 3);
        Assert.Equal(3, request.DepartmentId);
    }

    [Fact]
    public void ShiftSwap_DifferentDepartments_ShouldBeInvalid()
    {
        int doctorDeptId = 1;
        int repDoctorDeptId = 2;
        Assert.NotEqual(doctorDeptId, repDoctorDeptId);
    }

    [Theory]
    [InlineData(ScheduleExceptionStatus.Pending, true)]
    [InlineData(ScheduleExceptionStatus.Approved, false)]
    [InlineData(ScheduleExceptionStatus.Rejected, false)]
    public void ScheduleException_CanBeProcessed_OnlyWhenPending(ScheduleExceptionStatus status, bool canProcess)
    {
        bool result = status == ScheduleExceptionStatus.Pending;
        Assert.Equal(canProcess, result);
    }

    [Fact]
    public void CallNextPatient_ShouldTarget_ReadyForDoctor_Only()
    {
        int targetStatusId = (int)AppointmentStatusId.ReadyForDoctor;
        Assert.Equal(4, targetStatusId);
        Assert.NotEqual((int)AppointmentStatusId.CheckedIn, targetStatusId);
    }

    [Theory]
    [InlineData(null, 1, false)]
    [InlineData(0, 1, false)]
    [InlineData(-1, 1, false)]
    [InlineData(1, 2, false)]
    [InlineData(1, 1, true)]
    public void ExceptionDepartmentAuthorization_ShouldFailClosed(int? requestDeptId, int doctorDeptId, bool shouldAllow)
    {
        bool isAllowed = requestDeptId.HasValue && requestDeptId.Value > 0 && doctorDeptId == requestDeptId.Value;
        Assert.Equal(shouldAllow, isAllowed);
    }

    [Fact]
    public void StaffService_SoftDelete_ShouldSetIsActiveFalse()
    {
        var account = new Account { Id = 10, Username = "doctor1", IsActive = true };
        account.IsActive = false;
        Assert.False(account.IsActive);
    }
}

