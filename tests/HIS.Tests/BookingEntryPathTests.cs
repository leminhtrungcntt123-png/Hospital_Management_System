using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.DAL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace HIS.Tests;

/// <summary>
/// Ba đường vào đặt khám (theo chuyên khoa / theo ngày / theo bác sĩ) phải hội tụ về cùng những khung giờ
/// trong cửa sổ đặt được. Test chạy trên CSDL Postgres thật, so chiếu chéo các truy vấn với nhau
/// chứ không giả định dữ liệu: chưa có khung giờ nào còn đặt được thì bỏ qua để CI không cần lịch trực.
/// DB lấy từ biến môi trường HIS_REHEARSAL_CONNECTION; chưa đặt thì bỏ qua.
/// </summary>
[Trait("Category", "Integration")]
[Collection("RehearsalPostgres")]
public class BookingEntryPathTests
{
    private static readonly string[] OpenDayStates = ["available", "few", "full"];

    private readonly ITestOutputHelper _out;

    public BookingEntryPathTests(ITestOutputHelper out1) => _out = out1;

    private static string? Connection => Environment.GetEnvironmentVariable("HIS_REHEARSAL_CONNECTION");

    private static AppDbContext NewContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options, TimeProvider.System);
    }

    private static BookingService NewBooking(AppDbContext ctx)
    {
        var bookingOptions = Options.Create(new BookingOptions());
        return new BookingService(ctx, new QueuePositionAllocator(ctx, bookingOptions, TimeProvider.System),
            bookingOptions, TimeProvider.System);
    }

    /// <summary>Ngày gần nhất còn khung đặt được trong cửa sổ; null nếu lịch trống (CI chạy ngày chủ nhật).</summary>
    private async Task<DateOnly?> FirstOpenDayAsync(BookingService booking)
    {
        var days = await booking.GetDaysAsync(booking.FirstBookableDate, booking.BookableDayCount);
        var open = days.FirstOrDefault(d => OpenDayStates.Contains(d.State));
        _out.WriteLine($"cửa sổ {days.Count} ngày từ {booking.FirstBookableDate:dd/MM} tới {booking.LastBookableDate:dd/MM}, " +
                       $"ngày mở đầu tiên = {(open?.Date.ToString("dd/MM") ?? "không có")}");
        return open?.Date;
    }

    [Fact]
    public async Task Luong_theo_chuyen_khoa_dem_suat_trung_voi_danh_sach_bac_si()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        using var ctx = NewContext(cs);
        var booking = NewBooking(ctx);

        var departments = await booking.GetActiveDepartmentsAsync();
        Assert.NotEmpty(departments);
        Assert.All(departments, d => Assert.True(d.SlotsAvailable >= 0));

        var department = departments.FirstOrDefault(d => d.SlotsAvailable > 0);
        if (department is null) { _out.WriteLine("Bỏ qua: chưa khoa nào còn suất đặt online trong cửa sổ."); return; }

        // Màn "chọn chuyên khoa" bấm vào một khoa phải ra đúng con số vừa thấy trên thẻ.
        var oneDepartment = await booking.GetDepartmentByIdAsync(department.Id);
        Assert.NotNull(oneDepartment);
        Assert.Equal(department.SlotsAvailable, oneDepartment!.SlotsAvailable);

        var doctors = await booking.GetDoctorsAsync(department.Id);
        Assert.NotEmpty(doctors);
        Assert.All(doctors, d => Assert.Equal(department.Id, d.DepartmentId));

        // Bác sĩ bất kỳ = cộng mọi suất của từng người trong khoa, không được lệch với thẻ khoa.
        Assert.Equal(department.SlotsAvailable, doctors.Sum(d => d.SlotsAvailable));

        foreach (var doctor in doctors)
        {
            // DutyDays lấy từ doctor_schedules đang hiệu lực; bác sĩ chưa có lịch trực thì ghi nhãn rõ ràng.
            Assert.False(string.IsNullOrWhiteSpace(doctor.DutyDays));
            Assert.Equal(doctor.SlotsAvailable > 0, doctor.NextAvailableDate is not null);
            if (doctor.NextAvailableDate is { } next)
            {
                Assert.InRange(next, booking.FirstBookableDate, booking.LastBookableDate);
            }
        }

        _out.WriteLine(string.Join(Environment.NewLine, doctors.Select(d =>
            $"{d.Id} {d.FullName} | {d.DutyDays} | còn {d.SlotsAvailable} | sớm nhất {d.NextAvailableDate:dd/MM}")
            .DefaultIfEmpty($"{department.Name}: không có bác sĩ")));
    }

    [Fact]
    public async Task Luong_theo_ngay_liet_ke_dung_khoa_va_bac_si_truc_hom_do()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        using var ctx = NewContext(cs);
        var booking = NewBooking(ctx);

        var date = await FirstOpenDayAsync(booking);
        if (date is null) { _out.WriteLine("Bỏ qua: cửa sổ đặt được không có ngày nào còn khung giờ."); return; }

        var onDate = await booking.GetDepartmentsOnDateAsync(date.Value);
        Assert.NotEmpty(onDate);
        Assert.Contains(onDate, d => d.SlotsAvailable > 0);

        // SlotsAvailable của mỗi khoa đúng bằng danh sách khung giờ trả về khi vào sâu hơn.
        foreach (var department in onDate)
        {
            var slots = await booking.GetSlotsAsync(date.Value, department.Id);
            Assert.Equal(department.SlotsAvailable, slots.Sum(s => s.SlotsLeft));
            Assert.All(slots, s => Assert.Equal(department.Id, s.DepartmentId));
        }

        var openDepartment = onDate.First(d => d.SlotsAvailable > 0);
        var byDoctor = await booking.GetDoctorSlotsAsync(openDepartment.Id, date.Value);
        Assert.NotEmpty(byDoctor);

        // Màn "Bác sĩ & khung giờ" gộp theo người: gộp lại phải khớp trọn danh sách khung của khoa.
        var flat = byDoctor.SelectMany(g => g.Slots).ToList();
        var departmentSlots = await booking.GetSlotsAsync(date.Value, openDepartment.Id);
        Assert.Equal(departmentSlots.Select(s => s.Id).OrderBy(id => id), flat.Select(s => s.Id).OrderBy(id => id));
        Assert.All(byDoctor, g => Assert.True(g.SlotsLeft > 0 || g.Slots.Any(s => s.Status == "FULL")));
        Assert.Equal(openDepartment.SlotsAvailable, byDoctor.Sum(g => g.SlotsLeft));

        foreach (var group in byDoctor)
        {
            Assert.Equal(group.DoctorId, Assert.Single(group.Slots.Select(s => s.DoctorId).Distinct()));
            Assert.False(string.IsNullOrWhiteSpace(group.RoomName));
            _out.WriteLine($"{group.DoctorName} @ {group.RoomName}: {group.Slots.Count} khung, còn {group.SlotsLeft}");
        }

        // Bác sĩ hiện ở màn "chọn bác sĩ" của ngày này phải thật sự còn khung hôm đó.
        var doctorsOnDate = await booking.GetDoctorsAsync(openDepartment.Id, date);
        Assert.NotEmpty(doctorsOnDate);
        foreach (var doctor in doctorsOnDate)
        {
            Assert.NotEmpty(await booking.GetSlotsAsync(date.Value, doctorId: doctor.Id));
        }
    }

    [Fact]
    public async Task Luong_theo_bac_si_loc_lich_va_khung_gio_dung_theo_nguoi()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        using var ctx = NewContext(cs);
        var booking = NewBooking(ctx);

        var doctors = await booking.GetDoctorsAsync();
        var doctor = doctors.FirstOrDefault(d => d.SlotsAvailable > 0);
        if (doctor is null) { _out.WriteLine("Bỏ qua: chưa bác sĩ nào còn suất đặt online trong cửa sổ."); return; }

        var days = await booking.GetDaysAsync(booking.FirstBookableDate, booking.BookableDayCount, doctorId: doctor.Id);
        Assert.Equal(booking.BookableDayCount, days.Count);
        Assert.Equal(days.Select(d => d.Date).OrderBy(d => d).First(), booking.FirstBookableDate);

        // Lịch một bác sĩ: ngày nghỉ của người đó phải thành "off"/"closed", không bao giờ còn suất.
        var openDays = days.Where(d => OpenDayStates.Contains(d.State)).Select(d => d.Date).ToList();
        Assert.NotEmpty(openDays);

        int totalLeft = 0;
        foreach (var day in openDays)
        {
            var slots = await booking.GetSlotsAsync(day, doctorId: doctor.Id);
            Assert.NotEmpty(slots);
            Assert.All(slots, s => Assert.Equal(doctor.Id, s.DoctorId));
            Assert.All(slots, s => Assert.Contains(s.ShiftLabel, ["Ca sáng", "Ca chiều"]));
            totalLeft += slots.Sum(s => s.SlotsLeft);
        }
        Assert.Equal(doctor.SlotsAvailable, totalLeft);

        // Ngày chủ nhật không mở đặt online cho bất kỳ ai.
        Assert.All(days, d => Assert.True(d.Date.DayOfWeek != DayOfWeek.Sunday || !OpenDayStates.Contains(d.State)));

        BookingSlotDto? selectable = null;
        foreach (var day in openDays)
        {
            selectable = (await booking.GetSlotsAsync(day, doctorId: doctor.Id)).FirstOrDefault(s => s.IsSelectable);
            if (selectable is not null) break;
        }
        if (selectable is null)
        {
            _out.WriteLine("Bỏ qua bước đối chiếu khung giờ theo Id: mọi khung của bác sĩ này đều đã kín suất online.");
            return;
        }

        var reloaded = await booking.GetSlotByIdAsync(selectable.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(selectable.Id, reloaded!.Id);
        Assert.Equal(selectable.Label, reloaded.Label);
        Assert.Equal(selectable.RoomName, reloaded.RoomName);
        Assert.Equal(selectable.DoctorName, reloaded.DoctorName);
        Assert.Equal(selectable.SlotsLeft, reloaded.SlotsLeft);

        Assert.Null(await booking.GetSlotByIdAsync(int.MaxValue));
        _out.WriteLine($"{doctor.FullName}: {openDays.Count} ngày mở, {totalLeft} suất, kiểm lại khung {reloaded.Label} OK");
    }
}
