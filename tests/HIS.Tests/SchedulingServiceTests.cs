using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace HIS.Tests;

public class SchedulingServiceTests
{
    private static AppDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new AppDbContext(options, TimeProvider.System);
    }

    [Fact]
    public async Task GenerateSlotsAsync_InvalidDateRange_ReturnsFailure()
    {
        using var context = CreateInMemoryDbContext("Test_InvalidDate");
        var service = new SchedulingService(context, Options.Create(new BookingOptions()));

        var request = new GenerateSlotsRequestDto
        {
            FromDate = new DateOnly(2026, 11, 10),
            ToDate = new DateOnly(2026, 11, 5)
        };

        var result = await service.GenerateSlotsAsync(request);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task GenerateSlotsAsync_GeneratesSlotsAndSkipsExisting()
    {
        using var context = CreateInMemoryDbContext("Test_GenerateSlots");

        var dept = new Department { Id = 1, Name = "Khoa Tim mạch" };
        var room = new ClinicRoom { Id = 1, DepartmentId = 1, RoomName = "Phòng 101", RoomNumber = "P101" };
        var doctor = new Doctor
        {
            Id = 1,
            AccountId = 10,
            DepartmentId = 1,
            FullName = "BS. Nguyễn Văn An",
            Specialty = "Tim mạch",
            LicenseNumber = "BS-001",
            AvgConsultationMinutes = 12
        };

        var today = DateOnly.FromDateTime(DateTime.Today);
        var targetDate = today.AddDays(7);
        DayOfWeek targetDow = targetDate.DayOfWeek;

        // Ca sáng 07:00-09:00, khung 60 phút -> 2 khung; mỗi khung chứa 60/12 = 5 lượt,
        // trong đó 60% là suất A (online) = 3 và 2 suất T (đến trực tiếp).
        var schedule = new DoctorSchedule
        {
            Id = 1,
            DoctorId = 1,
            RoomId = 1,
            DayOfWeek = targetDow,
            Shift = ExamShift.Morning,
            StartTime = new TimeOnly(7, 0),
            EndTime = new TimeOnly(9, 0),
            SlotDurationMinutes = 60,
            OnlineRatioPct = 60,
            EffectiveFrom = today,
            EffectiveTo = null,
            IsActive = true
        };

        context.Departments.Add(dept);
        context.ClinicRooms.Add(room);
        context.Doctors.Add(doctor);
        context.DoctorSchedules.Add(schedule);
        await context.SaveChangesAsync();

        var service = new SchedulingService(context, Options.Create(new BookingOptions()));
        var request = new GenerateSlotsRequestDto
        {
            FromDate = targetDate,
            ToDate = targetDate,
            DoctorId = 1
        };

        var result1 = await service.GenerateSlotsAsync(request);
        Assert.True(result1.IsSuccess);
        Assert.Equal(2, result1.TotalGenerated);

        var generated = await context.AppointmentSlots.OrderBy(s => s.StartTime).ToListAsync();
        Assert.All(generated, s => Assert.Equal(5, s.Capacity));
        Assert.All(generated, s => Assert.Equal(3, s.OnlineQuota));
        Assert.All(generated, s => Assert.Equal(0, s.OnlineBooked));

        var result2 = await service.GenerateSlotsAsync(request);
        Assert.True(result2.IsSuccess);
        Assert.Equal(0, result2.TotalGenerated);
        Assert.Equal(2, result2.TotalSkippedExisting);

        var slots = await service.GetAvailableSlotsAsync(new SlotSearchFilterDto
        {
            Date = targetDate,
            DoctorId = 1
        });

        Assert.Equal(2, slots.Count);
    }

    [Fact]
    public async Task GenerateSlotsAsync_CaKeoDenSatNuaDem_DungKhiGapDiemQuan()
    {
        // Ca 22:00 -> 23:59, khung 60 phút: chỉ sinh được khung 22:00-23:00. Điểm kế tiếp là 00:00,
        // TimeOnly quấn về nhỏ hơn giờ hiện tại nên vòng lặp phải dừng chứ không chạy mãi (regression cũ).
        using var context = await CreateNightContextAsync("Test_NightSameDay", new TimeOnly(22, 0), new TimeOnly(23, 59));
        DateOnly day = DateOnly.FromDateTime(DateTime.Today).AddDays(7);

        var service = new SchedulingService(context, Options.Create(new BookingOptions()));
        var result = await service.GenerateSlotsAsync(new GenerateSlotsRequestDto
        {
            FromDate = day,
            ToDate = day,
            DoctorId = 1
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.TotalGenerated);

        var slot = await context.AppointmentSlots.SingleAsync();
        Assert.Equal(day.ToDateTime(new TimeOnly(22, 0)), slot.StartTime);
        Assert.Equal(day.ToDateTime(new TimeOnly(23, 0)), slot.EndTime);
        Assert.Equal(3, slot.Capacity);      // 60 / 20 phút khám trung bình
        Assert.Equal(2, slot.OnlineQuota);   // round(3 * 60%) = 2 suất A, còn 1 suất T
    }

    [Fact]
    public async Task GenerateSlotsAsync_CaVatQuaNuaDem_KhongSinhKhungVatSangNgayKhac()
    {
        // Ca 20:00 -> 01:00 hôm sau: generator không cho khung vắt qua ngày nên không sinh khung nào,
        // nhưng phải trả lời ngay thay vì kẹt trong vòng lặp quấn giờ.
        using var context = await CreateNightContextAsync("Test_NightWrap", new TimeOnly(20, 0), new TimeOnly(1, 0));
        DateOnly day = DateOnly.FromDateTime(DateTime.Today).AddDays(7);

        var service = new SchedulingService(context, Options.Create(new BookingOptions()));
        var result = await service.GenerateSlotsAsync(new GenerateSlotsRequestDto
        {
            FromDate = day,
            ToDate = day,
            DoctorId = 1
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.TotalGenerated);
        Assert.Empty(await context.AppointmentSlots.ToListAsync());
    }

    // Bác sĩ trực ca tối với 20 phút khám một người, phòng và khoa tự dựng trong DB InMemory.
    private static async Task<AppDbContext> CreateNightContextAsync(string dbName, TimeOnly start, TimeOnly end)
    {
        var context = CreateInMemoryDbContext(dbName);
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly targetDate = today.AddDays(7);

        context.Departments.Add(new Department { Id = 1, Name = "Khoa Cấp cứu" });
        context.ClinicRooms.Add(new ClinicRoom
        {
            Id = 1,
            DepartmentId = 1,
            RoomName = "Phòng trực đêm",
            RoomNumber = "P901"
        });
        context.Doctors.Add(new Doctor
        {
            Id = 1,
            AccountId = 11,
            DepartmentId = 1,
            FullName = "BS. Trần Thị Bình",
            Specialty = "Cấp cứu",
            LicenseNumber = "BS-002",
            AvgConsultationMinutes = 20
        });
        context.DoctorSchedules.Add(new DoctorSchedule
        {
            Id = 1,
            DoctorId = 1,
            RoomId = 1,
            DayOfWeek = targetDate.DayOfWeek,
            Shift = ExamShift.Afternoon,
            StartTime = start,
            EndTime = end,
            SlotDurationMinutes = 60,
            OnlineRatioPct = 60,
            EffectiveFrom = today,
            EffectiveTo = null,
            IsActive = true
        });

        await context.SaveChangesAsync();
        return context;
    }
}
