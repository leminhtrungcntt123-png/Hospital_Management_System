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
    [InlineData(AppointmentStatusId.CheckedIn, AppointmentStatusId.InProgress, true)]
    [InlineData(AppointmentStatusId.CheckedIn, AppointmentStatusId.Completed, true)]
    [InlineData(AppointmentStatusId.ReadyForDoctor, AppointmentStatusId.InProgress, true)]
    [InlineData(AppointmentStatusId.ReadyForDoctor, AppointmentStatusId.Completed, true)]
    [InlineData(AppointmentStatusId.InProgress, AppointmentStatusId.Completed, true)]
    [InlineData(AppointmentStatusId.Completed, AppointmentStatusId.Cancelled, false)]
    [InlineData(AppointmentStatusId.Cancelled, AppointmentStatusId.Confirmed, false)]
    [InlineData(AppointmentStatusId.Cancelled, AppointmentStatusId.Completed, false)]
    public void CanTransition_ShouldValidateStateTransitionsCorrectly(
        AppointmentStatusId current, AppointmentStatusId next, bool expected)
    {
        var actual = AppointmentStateMachine.CanTransition(current, next);
        Assert.Equal(expected, actual);
    }
}

public class ConnectionStringParserTests
{
    [Fact]
    public void Should_Normalize_Postgresql_Uri_Correctly()
    {
        var uri = "postgresql://neondb_owner:mySecretPass123@ep-sparkling-wind-azs5uilk.c-3.ap-southeast-1.aws.neon.tech/hms_database?sslmode=require&channel_binding=require";
        var normalized = HIS.DAL.DataAccessServiceCollectionExtensions.NormalizeConnectionString(uri);

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(normalized);
        Assert.Equal("ep-sparkling-wind-azs5uilk.c-3.ap-southeast-1.aws.neon.tech", builder.Host);
        Assert.Equal(5432, builder.Port);
        Assert.Equal("hms_database", builder.Database);
        Assert.Equal("neondb_owner", builder.Username);
        Assert.Equal("mySecretPass123", builder.Password);
        Assert.Equal(Npgsql.SslMode.Require, builder.SslMode);
        Assert.Equal(Npgsql.ChannelBinding.Require, builder.ChannelBinding);
    }

    [Fact]
    public void Should_Normalize_Postgresql_Uri_With_VerifyFull_And_Encoded_Db()
    {
        var uri = "postgresql://user:pass@localhost:5433/my%20test%20db?sslmode=verify-full";
        var normalized = HIS.DAL.DataAccessServiceCollectionExtensions.NormalizeConnectionString(uri);

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(normalized);
        Assert.Equal("localhost", builder.Host);
        Assert.Equal(5433, builder.Port);
        Assert.Equal("my test db", builder.Database);
        Assert.Equal(Npgsql.SslMode.VerifyFull, builder.SslMode);
    }

    [Fact]
    public void Should_Keep_Standard_Key_Value_ConnectionString()
    {
        var standard = "Host=localhost;Port=5432;Database=his_db;Username=postgres;Password=CHANGE_ME";
        var normalized = HIS.DAL.DataAccessServiceCollectionExtensions.NormalizeConnectionString(standard);

        Assert.Equal(standard, normalized);
    }
}
