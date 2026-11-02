using HIS.BLL.Common;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace HIS.Tests.Integration;

[Trait("Category", "Integration")]
public class DatabaseIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("his_test_db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private AppDbContext _context = null!;

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_dbContainer.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        _context = new AppDbContext(options, new VietnamTimeProvider());
        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _dbContainer.DisposeAsync();
    }

    [Fact]
    public async Task Test_Account_IsActive_False_Persists_Correctly()
    {
        var account = new Account
        {
            Username = "inactive_user",
            PasswordHash = "hash",
            RoleId = (int)RoleId.Patient,
            IsActive = false
        };

        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();

        var saved = await _context.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Username == "inactive_user");
        Assert.NotNull(saved);
        Assert.False(saved.IsActive);
    }
}

