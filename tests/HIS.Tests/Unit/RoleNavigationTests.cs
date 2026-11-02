using System.ComponentModel.DataAnnotations;
using HIS.Web.Common;
using HIS.Web.Models;
using Xunit;

namespace HIS.Tests.Unit;

public class RoleNavigationTests
{
    [Theory]
    [InlineData("ADMIN", "Index", "Accounts", "Admin")]
    [InlineData("DEPARTMENT_HEAD", "Index", "Schedule", "Staff")]
    [InlineData("DOCTOR", "Index", "Consultation", "Staff")]
    [InlineData("NURSE", "Index", "Reception", "Staff")]
    [InlineData("PATIENT", "Index", "Booking", "Patient")]
    [InlineData("UNKNOWN_ROLE", "Index", "Home", "")]
    [InlineData(null, "Index", "Home", "")]
    public void GetLandingRoute_ShouldRouteToExpectedActionAndArea(
        string? role, string expectedAction, string expectedController, string expectedArea)
    {
        var (action, controller, area) = RoleNavigationHelper.GetLandingRoute(role);

        Assert.Equal(expectedAction, action);
        Assert.Equal(expectedController, controller);
        Assert.Equal(expectedArea, area);
    }

    [Fact]
    public void LoginViewModel_ShouldFailValidation_WhenRequiredFieldsMissing()
    {
        var model = new LoginViewModel();
        var validationContext = new ValidationContext(model);
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(model, validationContext, validationResults, true);

        Assert.False(isValid);
        Assert.Contains(validationResults, r => r.MemberNames.Contains(nameof(LoginViewModel.Username)));
        Assert.Contains(validationResults, r => r.MemberNames.Contains(nameof(LoginViewModel.Password)));
    }

    [Fact]
    public void LoginViewModel_ShouldPassValidation_WhenFieldsProvided()
    {
        var model = new LoginViewModel
        {
            Username = "admin",
            Password = "Password123"
        };
        var validationContext = new ValidationContext(model);
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(model, validationContext, validationResults, true);

        Assert.True(isValid);
        Assert.Empty(validationResults);
    }
}
