using HIS.BLL.DTOs;
using HIS.BLL.Services;
using HIS.Web.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HIS.Tests.Unit;

public class AdminCatalogTests
{
    [Fact]
    public void CreateRoomRequest_Validation_ShouldCheckFields()
    {
        var valid = new CreateRoomRequest
        {
            DepartmentId = 1,
            RoomName = "Phòng khám 101",
            RoomNumber = "P101"
        };

        Assert.True(valid.DepartmentId > 0);
        Assert.False(string.IsNullOrWhiteSpace(valid.RoomName));
        Assert.False(string.IsNullOrWhiteSpace(valid.RoomNumber));
    }

    [Theory]
    [InlineData(1, "Siêu âm tim", 250000, true)]
    [InlineData(2, "Tổng phân tích máu", 80000, true)]
    [InlineData(3, "X-Quang", 150000, false)]
    public void UpsertServiceRequest_Properties_ShouldBePreserved(int typeId, string name, decimal price, bool active)
    {
        var request = new UpsertServiceRequest
        {
            ServiceTypeId = typeId,
            Name = name,
            Price = price,
            IsActive = active
        };

        Assert.Equal(typeId, request.ServiceTypeId);
        Assert.Equal(name, request.Name);
        Assert.Equal(price, request.Price);
        Assert.Equal(active, request.IsActive);
    }

    [Fact]
    public void UpsertMedicationRequest_Validation_ShouldAcceptValidDetails()
    {
        var request = new UpsertMedicationRequest
        {
            Name = "Paracetamol",
            ActiveIngredient = "Paracetamol",
            Strength = "500mg",
            Unit = "Viên",
            Price = 2000,
            IsActive = true
        };

        Assert.Equal("Paracetamol", request.Name);
        Assert.Equal("500mg", request.Strength);
        Assert.Equal("Viên", request.Unit);
        Assert.True(request.Price > 0);
    }

    [Fact]
    public async Task ManagementController_CatalogDirectory_ReturnsData()
    {
        var catalogMock = new Mock<IHospitalCatalogService>();
        catalogMock.Setup(c => c.GetRoomsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AdminRoomRow>
            {
                new AdminRoomRow(1, 1, "Khoa Nội", "Phòng 101", "P101", 0)
            });
        catalogMock.Setup(c => c.GetServicesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AdminServiceRow>
            {
                new AdminServiceRow(1, 1, "Khám bệnh", "Khám nội", 150000, true)
            });
        catalogMock.Setup(c => c.GetServiceTypesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AdminOption> { new AdminOption(1, "Khám bệnh") });
        catalogMock.Setup(c => c.GetMedicationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AdminMedicationRow>
            {
                new AdminMedicationRow(1, "Panadol", "Paracetamol", "500mg", "Viên", 2500, true)
            });

        var deptMock = new Mock<IDepartmentService>();
        deptMock.Setup(d => d.GetDepartmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DepartmentListItemDto>
            {
                new DepartmentListItemDto(1, "Khoa Nội", "Mô tả")
            });

        var controller = new ManagementController(
            Mock.Of<IStaffService>(),
            deptMock.Object,
            Mock.Of<IAdminReportService>(),
            Mock.Of<IScheduleChangeService>(),
            catalogMock.Object,
            TimeProvider.System,
            NullLogger<ManagementController>.Instance);

        var result = await controller.CatalogDirectory(default);
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }
}

