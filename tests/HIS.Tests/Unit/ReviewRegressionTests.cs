using System.ComponentModel.DataAnnotations;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Services;
using HIS.Web.Areas.Admin.Controllers;
using HIS.Tests.Integration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HIS.Tests.Unit;

public class ReviewRegressionTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 9, 0, 0);

    [Theory]
    [InlineData("qa_123456abcdef")]
    [InlineData("qa_a654321b")]
    public async Task TestEmailReaders_DoNotConfuseAccountDigitsWithOtp(string username)
    {
        var body = $"Xin chào {username},\nMã xác thực (OTP) đặt lại mật khẩu MediCare của bạn là: 987654\nMã này có hiệu lực trong 5 phút.";
        var captured = new CapturedEmailSender();
        await captured.SendEmailAsync("qa@example.invalid", "Synthetic reset email", body);
        Assert.Equal("987654", captured.LastOtp);

        var controlled = new ControlledRecoveryEmail();
        controlled.Release.TrySetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => controlled.SendEmailAsync("qa@example.invalid", "Synthetic reset email", body));
        await controlled.SendEmailAsync("qa@example.invalid", "Synthetic reset email", body);
        Assert.Equal("987654", controlled.LastOtp);
    }

    [Fact]
    public void ParallelIncorrectOtpAttempts_StopAtFive()
    {
        var challenge = new PasswordResetChallenge("123456", Now.AddMinutes(5));
        Parallel.For(0, 64, _ => Assert.False(challenge.TryConsume("000000", Now)));
        Assert.Equal(5, challenge.Attempts);
        Assert.False(challenge.TryConsume("123456", Now));
    }

    [Fact]
    public void ParallelCorrectOtpAttempts_SucceedExactlyOnce()
    {
        var challenge = new PasswordResetChallenge("123456", Now.AddMinutes(5));
        var successes = 0;
        Parallel.For(0, 64, _ =>
        {
            if (challenge.TryConsume("123456", Now)) Interlocked.Increment(ref successes);
        });
        Assert.Equal(1, successes);
        Assert.Equal(1, challenge.Attempts);
    }

    [Fact]
    public void Otp_AllowsTheFifthAttemptButCannotBeReused()
    {
        var challenge = new PasswordResetChallenge("123456", Now.AddMinutes(5));
        for (var attempt = 0; attempt < 4; attempt++) Assert.False(challenge.TryConsume("000000", Now));
        Assert.True(challenge.TryConsume("123456", Now));
        Assert.False(challenge.TryConsume("123456", Now));
        Assert.Equal(5, challenge.Attempts);
    }

    [Fact]
    public void Otp_ExpiresAtTheFiveMinuteBoundary()
    {
        var challenge = new PasswordResetChallenge("123456", Now.AddMinutes(5));
        Assert.False(challenge.TryConsume("123456", Now.AddMinutes(5)));
        Assert.Equal(0, challenge.Attempts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OptionalEmail_BlankValuesValidateAsNull(string? email)
    {
        var request = new CreateAdminAccountRequest { Username = "qa_admin", Password = "QA_password_2026!", RoleId = 1, Email = email };
        Assert.Null(request.Email);
        Assert.True(Validator.TryValidateObject(request, new ValidationContext(request), [], true));
    }

    [Theory]
    [InlineData("missing-at-sign")]
    [InlineData("@")]
    public void OptionalEmail_InvalidNonBlankValuesRemainInvalid(string email)
    {
        var request = new CreateAdminAccountRequest { Username = "qa_admin", Password = "QA_password_2026!", RoleId = 1, Email = email };
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), [], true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DepartmentErrors_ExposeOnlyTheKnownDuplicateMessage(bool duplicate)
    {
        var expected = "Khoa/phòng 'Khoa X' đã tồn tại.";
        var departments = new Mock<IDepartmentService>();
        departments.Setup(service => service.CreateDepartmentAsync("Khoa X", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(duplicate ? expected : "private database connection details"));
        var controller = new ManagementController(Mock.Of<IStaffService>(), departments.Object,
            Mock.Of<IAdminReportService>(), Mock.Of<IScheduleChangeService>(), TimeProvider.System,
            NullLogger<ManagementController>.Instance);
        var result = Assert.IsType<BadRequestObjectResult>(await controller.CreateDepartment(new DepartmentCreateRequest { Name = "Khoa X" }, default));
        var message = result.Value!.GetType().GetProperty("message")!.GetValue(result.Value);
        Assert.Equal(duplicate ? expected : "Chưa thực hiện được thao tác. Hãy kiểm tra dữ liệu và thử lại.", message);
    }
}
