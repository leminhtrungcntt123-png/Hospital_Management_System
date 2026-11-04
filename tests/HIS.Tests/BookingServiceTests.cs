using Microsoft.EntityFrameworkCore;
using HIS.DAL;
using HIS.BLL;
using HIS.BLL.Options;
using HIS.BLL.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace HIS.Tests;

public class BookingServiceTests
{
    [Fact]
    public void BookingService_CanBeInstantiated_WithDbContext()
    {
        // Arrange
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "TestHisDb")
            .Options;

        using var context = new AppDbContext(dbOptions, TimeProvider.System);

        // Act
        var service = new BookingService(context, Options.Create(new BookingOptions()), TimeProvider.System);

        // Assert
        Assert.NotNull(service);
    }
}
