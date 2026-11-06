using FluentValidation.TestHelper;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Validators;
using HIS.Domain;
using Xunit;

namespace HIS.Tests.Unit;

public class RegisterPatientRequestValidatorTests
{
    private readonly RegisterPatientRequestValidator _validator = new();

    [Fact]
    public void Should_Have_Error_When_Username_Is_Empty()
    {
        var model = new RegisterPatientRequest("", "Password123", "Nguyễn Văn A", new DateOnly(1995, 1, 1), "0912345678");
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Username);
    }

    [Fact]
    public void Should_Have_Error_When_Phone_Is_Invalid()
    {
        var model = new RegisterPatientRequest("patient1", "Password123", "Nguyễn Văn A", new DateOnly(1995, 1, 1), "123");
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Fact]
    public void Should_Pass_When_Request_Is_Valid()
    {
        var model = new RegisterPatientRequest("patient1", "Password123", "Nguyễn Văn A", new DateOnly(1995, 1, 1), "0912345678", "patient@test.com");
        var result = _validator.TestValidate(model);
        result.ShouldNotHaveAnyValidationErrors();
    }
}

public class AppointmentStateMachineTests
{
    [Theory]
    [InlineData(AppointmentStatusId.Pending, AppointmentStatusId.Confirmed, true)]
    [InlineData(AppointmentStatusId.Pending, AppointmentStatusId.Expired, true)]
    [InlineData(AppointmentStatusId.Pending, AppointmentStatusId.Cancelled, true)]
    [InlineData(AppointmentStatusId.Pending, AppointmentStatusId.InProgress, false)]
    [InlineData(AppointmentStatusId.Confirmed, AppointmentStatusId.CheckedIn, true)]
    [InlineData(AppointmentStatusId.Confirmed, AppointmentStatusId.Cancelled, true)]
    [InlineData(AppointmentStatusId.Confirmed, AppointmentStatusId.NoShow, true)]
    [InlineData(AppointmentStatusId.Confirmed, AppointmentStatusId.RescheduleRequired, true)]
    [InlineData(AppointmentStatusId.CheckedIn, AppointmentStatusId.ReadyForDoctor, true)]
    [InlineData(AppointmentStatusId.ReadyForDoctor, AppointmentStatusId.InProgress, true)]
    [InlineData(AppointmentStatusId.InProgress, AppointmentStatusId.Completed, true)]
    [InlineData(AppointmentStatusId.Completed, AppointmentStatusId.Cancelled, false)]
    [InlineData(AppointmentStatusId.Cancelled, AppointmentStatusId.Confirmed, false)]
    public void CanTransition_ShouldValidateStateTransitionsCorrectly(
        AppointmentStatusId current, AppointmentStatusId next, bool expected)
    {
        var actual = AppointmentStateMachine.CanTransition(current, next);
        Assert.Equal(expected, actual);
    }
}
