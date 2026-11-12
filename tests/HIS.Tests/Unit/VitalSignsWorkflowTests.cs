using System.ComponentModel.DataAnnotations;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.Domain;
using HIS.Web.Areas.Staff.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HIS.Tests.Unit;

public class VitalSignsWorkflowTests
{
    [Fact]
    public void VitalSignsRequest_ValidatesPulsePressureDifference()
    {
        // Hiệu áp < 10 mmHg: Không hợp lệ
        var req1 = new RecordVitalSignsRequest
        {
            AppointmentId = 1,
            Pulse = 75,
            SystolicBp = 120,
            DiastolicBp = 115, // Delta = 5 < 10
            Temperature = 36.5m,
            Weight = 60m
        };
        var context1 = new ValidationContext(req1);
        var results1 = new List<ValidationResult>();
        var valid1 = Validator.TryValidateObject(req1, context1, results1, true);
        Assert.False(valid1);
        Assert.Contains(results1, r => r.ErrorMessage!.Contains("Hiệu áp"));

        // Huyết áp tâm thu <= tâm trương: Không hợp lệ
        var req2 = new RecordVitalSignsRequest
        {
            AppointmentId = 1,
            Pulse = 75,
            SystolicBp = 80,
            DiastolicBp = 120,
            Temperature = 36.5m,
            Weight = 60m
        };
        var context2 = new ValidationContext(req2);
        var results2 = new List<ValidationResult>();
        var valid2 = Validator.TryValidateObject(req2, context2, results2, true);
        Assert.False(valid2);
        Assert.Contains(results2, r => r.ErrorMessage!.Contains("lớn hơn"));

        // Huyết áp chuẩn 120/80: Hợp lệ
        var req3 = new RecordVitalSignsRequest
        {
            AppointmentId = 1,
            Pulse = 75,
            SystolicBp = 120,
            DiastolicBp = 80,
            Temperature = 36.5m,
            Weight = 60m
        };
        var context3 = new ValidationContext(req3);
        var results3 = new List<ValidationResult>();
        var valid3 = Validator.TryValidateObject(req3, context3, results3, true);
        Assert.True(valid3);
        Assert.Empty(results3);
    }

    [Theory]
    [InlineData(25, 120, 80, 36.5, 60, false)] // Mạch < 30
    [InlineData(230, 120, 80, 36.5, 60, false)] // Mạch > 220
    [InlineData(75, 120, 80, 33.0, 60, false)] // Thân nhiệt < 34
    [InlineData(75, 120, 80, 44.0, 60, false)] // Thân nhiệt > 43
    [InlineData(75, 120, 80, 37.0, 0.5, false)] // Cân nặng < 1.0
    [InlineData(75, 120, 80, 37.0, 65.0, true)]  // Chuẩn
    public void VitalSignsRequest_ValidatesMedicalRanges(
        int pulse, int sys, int dia, decimal temp, decimal weight, bool expectedValid)
    {
        var req = new RecordVitalSignsRequest
        {
            AppointmentId = 10,
            Pulse = pulse,
            SystolicBp = sys,
            DiastolicBp = dia,
            Temperature = temp,
            Weight = weight
        };
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(req, new ValidationContext(req), results, true);
        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void AppointmentStateMachine_EnforcesStrictSequentialTransitions()
    {
        // 1. Confirmed (2) -> CheckedIn (3)
        Assert.True(AppointmentStateMachine.CanTransition(AppointmentStatusId.Confirmed, AppointmentStatusId.CheckedIn));

        // 2. CheckedIn (3) -> ReadyForDoctor (4) sau khi đo sinh hiệu
        Assert.True(AppointmentStateMachine.CanTransition(AppointmentStatusId.CheckedIn, AppointmentStatusId.ReadyForDoctor));

        // 3. ReadyForDoctor (4) -> InProgress (5) khi Bác sĩ bắt đầu khám
        Assert.True(AppointmentStateMachine.CanTransition(AppointmentStatusId.ReadyForDoctor, AppointmentStatusId.InProgress));

        // 4. InProgress (5) -> Completed (6) khi lưu bệnh án
        Assert.True(AppointmentStateMachine.CanTransition(AppointmentStatusId.InProgress, AppointmentStatusId.Completed));

        // 5. CẤM NHẢY CÓC: CheckedIn (3) KHÔNG ĐƯỢC nhảy thẳng sang InProgress (5) hoặc Completed (6)
        Assert.False(AppointmentStateMachine.CanTransition(AppointmentStatusId.CheckedIn, AppointmentStatusId.InProgress));
        Assert.False(AppointmentStateMachine.CanTransition(AppointmentStatusId.CheckedIn, AppointmentStatusId.Completed));

        // 6. CẤM NHẢY CÓC: ReadyForDoctor (4) KHÔNG ĐƯỢC nhảy thẳng sang Completed (6)
        Assert.False(AppointmentStateMachine.CanTransition(AppointmentStatusId.ReadyForDoctor, AppointmentStatusId.Completed));
    }

    [Fact]
    public void StaffWorkspaceApiController_RequiresNurseOnlyAndAntiCsrf()
    {
        var controller = typeof(StaffWorkspaceApiController);
        var authAttr = controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("NurseOnly", authAttr.Policy);

        var saveMethod = controller.GetMethod("SaveVitals");
        Assert.NotNull(saveMethod);
        Assert.NotNull(saveMethod!.GetCustomAttributes(typeof(HttpPostAttribute), true).SingleOrDefault());
        Assert.NotNull(saveMethod.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true).SingleOrDefault());
    }

    [Fact]
    public void ConsultationController_StartExam_RequiresHttpPostAndAntiCsrf()
    {
        var controller = typeof(ConsultationController);
        var startExamMethod = controller.GetMethod("StartExam");
        Assert.NotNull(startExamMethod);
        Assert.NotNull(startExamMethod!.GetCustomAttributes(typeof(HttpPostAttribute), true).SingleOrDefault());
        Assert.NotNull(startExamMethod.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true).SingleOrDefault());
    }
}
