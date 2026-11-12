using System.Security.Claims;
using HIS.BLL.Services;
using HIS.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HIS.Tests.Unit;

public class UiNavigationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("PATIENT")]
    [InlineData("NURSE")]
    [InlineData("DOCTOR")]
    [InlineData("DEPARTMENT_HEAD")]
    [InlineData("ADMIN")]
    public void Hospital_home_is_available_without_redirecting_staff_back_to_their_workspace(string? role)
    {
        var controller = new HomeController(
            Mock.Of<IDoctorService>(MockBehavior.Strict),
            Mock.Of<IDepartmentService>(MockBehavior.Strict),
            NullLogger<HomeController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(role is null
                        ? new ClaimsIdentity()
                        : new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test"))
                }
            }
        };

        Assert.IsType<ViewResult>(controller.Index());
    }
}
