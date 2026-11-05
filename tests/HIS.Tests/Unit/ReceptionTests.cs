using HIS.BLL.Services;
using HIS.Web.Areas.Staff.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HIS.Tests.Unit;

public class ReceptionTests
{
    [Theory]
    [InlineData(8, 29, false)]
    [InlineData(8, 30, true)]
    [InlineData(9, 30, true)]
    [InlineData(11, 30, true)]
    [InlineData(11, 31, false)]
    public void CheckIn_RespectsEarlyWindowAndShiftEnd(int hour, int minute, bool allowed)
    {
        var slot = new DateTime(2026, 11, 5, 9, 30, 0);
        Assert.Equal(allowed, ReceptionService.CanCheckIn(new DateTime(2026, 11, 5, hour, minute, 0), slot, slot.Date.AddHours(11.5), 60));
        Assert.False(ReceptionService.CanCheckIn(slot.AddDays(1), slot, slot.AddDays(1), 60));
    }

    [Fact]
    public void Reception_RequiresNurseAndAntiforgery()
    {
        var controller = typeof(ReceptionController);
        Assert.Equal("NurseOnly", controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Policy);
        foreach (var action in new[] { "CheckIn", "WalkIn" })
        {
            Assert.NotNull(controller.GetMethod(action)!.GetCustomAttributes(typeof(HttpPostAttribute), true).SingleOrDefault());
            Assert.NotNull(controller.GetMethod(action)!.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true).SingleOrDefault());
        }
    }
}
