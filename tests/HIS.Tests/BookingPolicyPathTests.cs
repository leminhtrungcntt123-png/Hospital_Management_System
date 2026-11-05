using HIS.BLL.Interfaces;
using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace HIS.Tests;

/// <summary>
/// Ba chính sách của Mẻ 3b-5 chạy trên CSDL Postgres thật với đồng hồ cố định 10:15:
/// (1) hủy muộn (dưới FreeCancelHours) ghi is_late_cancel, trả lại suất đang giữ và lần thứ hai trong
///     tháng dương lịch bị từ chối;
/// (2) đủ MaxNoShowsBeforeBlock lần vắng mặt thì đặt dấu chặn 45 ngày lên tài khoản và CreateAppointment
///     từ chối;
/// (3) nhân viên gán ưu tiên mức 2 chỉ chen tối đa MaxOvertakeAhead người và tăng overtaken_count của họ.
/// DB lấy từ biến môi trường HIS_REHEARSAL_CONNECTION; chưa đặt thì bỏ qua để CI không cần DB local.
/// </summary>
[Trait("Category", "Integration")]
[Collection("RehearsalPostgres")]
public class BookingPolicyPathTests
{
    private const int Capacity = 4;
    private const int OnlineQuota = 2;
    private const int FreeCancelHours = 24;
    private const int MaxNoShowsBeforeBlock = 3;
    private const int BookingBlockDays = 45;
    private const int MaxOvertakeAhead = 3;
    private const int MaxLateCancelsPerMonth = 1;

    private static readonly TimeOnly SoonSlotStart = new(10, 37);
    private static readonly TimeOnly PastCutoffSlotStart = new(10, 7);
    private static readonly TimeOnly ServiceNow = new(10, 15);

    private readonly ITestOutputHelper _out;

    public BookingPolicyPathTests(ITestOutputHelper out1) => _out = out1;

    private static string? Connection => Environment.GetEnvironmentVariable("HIS_REHEARSAL_CONNECTION");

    private static AppDbContext NewContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options, TimeProvider.System);
    }

    private static TimeProvider Clock(DateTime day) =>
        new FixedTimeProvider(day.Date.Add(ServiceNow.ToTimeSpan()));

    private static IOptions<BookingOptions> Booking => Options.Create(new BookingOptions());

    private static QueuePositionAllocator NewAllocator(AppDbContext ctx, DateTime day) =>
        new(ctx, Booking, Clock(day));

    private static ReceptionService NewReception(AppDbContext ctx, DateTime day) =>
        new(ctx, NewAllocator(ctx, day), Booking, Clock(day));

    private static BookingService NewBooking(AppDbContext ctx, DateTime day) =>
        new(ctx, NewAllocator(ctx, day), Booking, Clock(day));

    /// <summary>
    /// Phòng test riêng cho các khung giờ dựng lên. Cấp số luôn quét giải phóng suất A đã quá mốc cắt
    /// của cả phòng trong ngày, nên nếu đặt khung test vào phòng thật thì bài test sẽ ăn mất chỗ A01
    /// của dữ liệu demo; phòng riêng giữ cho CSDL thử nguyên vẹn sau mỗi lần chạy.
    /// </summary>
    private static async Task<int> NewTestRoomAsync(AppDbContext ctx)
    {
        int departmentId = await ctx.Departments.OrderBy(d => d.Id).Select(d => d.Id).FirstAsync();
        var room = new ClinicRoom
        {
            DepartmentId = departmentId,
            RoomName = "Phòng test chính sách",
            RoomNumber = $"TEST-{Guid.NewGuid():N}"[..13]
        };

        ctx.ClinicRooms.Add(room);
        await ctx.SaveChangesAsync();
        return room.Id;
    }

    [Fact]
    public async Task Huy_muon_ghi_is_late_cancel_tra_suat_va_tu_choi_lan_thu_hai_trong_thang()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        DateTime day = DateTime.Today;
        int slotId = 0;
        int roomId = 0;
        var appointmentIds = new List<int>();

        try
        {
            int patientAccountId;

            await using (var ctx = NewContext(cs))
            {
                var allocator = NewAllocator(ctx, day);
                var booking = NewBooking(ctx, day);

                AppointmentStatus confirmed = await ctx.AppointmentStatuses
                    .AsNoTracking()
                    .SingleAsync(s => s.Code == AppointmentStatus.Codes.Confirmed);

                patientAccountId = await ctx.Accounts
                    .AsNoTracking()
                    .Where(a => a.Role.Code == "PATIENT")
                    .Select(a => a.Id)
                    .FirstAsync();

                AppointmentSlot? template = await ctx.AppointmentSlots.AsNoTracking().OrderByDescending(s => s.StartTime).FirstOrDefaultAsync();
                if (template is null) { _out.WriteLine("Bỏ qua: DB chưa có khung giờ để mượn bác sĩ."); return; }

                if (await ctx.AppointmentSlots.AnyAsync(s => s.DoctorId == template.DoctorId
                        && s.StartTime == day.Date.Add(SoonSlotStart.ToTimeSpan())))
                {
                    _out.WriteLine("Bỏ qua: bác sĩ này đã có khung giờ lúc 10:37 hôm nay.");
                    return;
                }

                roomId = await NewTestRoomAsync(ctx);
                var slot = NewSlot(template, day.Date.Add(SoonSlotStart.ToTimeSpan()), roomId);
                ctx.AppointmentSlots.Add(slot);
                await ctx.SaveChangesAsync();
                slotId = slot.Id;

                var patients = await ctx.Patients.AsNoTracking().OrderBy(p => p.Id).Take(2).ToListAsync();
                if (patients.Count < 2) { _out.WriteLine($"Bỏ qua: DB chỉ có {patients.Count} bệnh nhân, cần 2."); return; }

                // Hai lượt web cùng của một tài khoản, khung 10:37 chỉ cách now 10:15 có 22 phút nên đều là hủy muộn.
                var first = NewAppointment(patients[0].Id, confirmed.Id, patientAccountId, "Lượt hủy muộn thứ nhất");
                var second = NewAppointment(patients[1].Id, confirmed.Id, patientAccountId, "Lượt hủy muộn thứ hai");
                ctx.Appointments.AddRange(first, second);
                await ctx.SaveChangesAsync();
                appointmentIds.AddRange([first.Id, second.Id]);

                Assert.NotNull(await allocator.AssignOnlineAsync(first, slotId));
                Assert.NotNull(await allocator.AssignOnlineAsync(second, slotId));

                int bookedBefore = (await ctx.AppointmentSlots.AsNoTracking().SingleAsync(s => s.Id == slotId)).BookedCount;
                _out.WriteLine($"Khung #{slotId} giữ {bookedBefore} suất trước khi hủy");

                var firstCancel = await booking.CancelAppointmentAsync(first.Id, patientAccountId);
                _out.WriteLine($"Hủy lần 1: Success={firstCancel.Success} | Late={firstCancel.IsLateCancel} | dùng {firstCancel.LateCancelsUsedThisMonth} | {firstCancel.Message}");
                Assert.True(firstCancel.Success, "Hủy muộn lần đầu trong tháng phải được chấp nhận.");
                Assert.True(firstCancel.IsLateCancel, $"Khung 10:37 chỉ cách now {ServiceNow} 22 phút nên phải tính là hủy muộn (< {FreeCancelHours}h).");

                // So status_id chứ không đi qua navigation: các lượt này đã bị context theo dõi từ đầu nên
                // Include chỉ bật được navigation nào thực sự truy vấn, đọc .Status.Code sẽ gặp null.
                int cancelledStatusId = (await ctx.AppointmentStatuses.AsNoTracking()
                    .SingleAsync(s => s.Code == AppointmentStatus.Codes.Cancelled)).Id;

                var savedFirst = await ctx.Appointments.Include(a => a.Slot).SingleAsync(a => a.Id == first.Id);
                Assert.Equal(cancelledStatusId, savedFirst.StatusId);
                Assert.Null(savedFirst.SlotPosition);
                Assert.Equal(bookedBefore - 1, savedFirst.Slot!.BookedCount);

                var history = await ctx.AppointmentStatusHistories.AsNoTracking()
                    .SingleAsync(h => h.AppointmentId == first.Id && h.NewStatus.Code == AppointmentStatus.Codes.Cancelled);
                Assert.True(history.IsLateCancel, "Lịch sử phải ghi is_late_cancel cho lần hủy sát giờ.");
                Assert.Equal(patientAccountId, history.PerformedBy);

                var secondCancel = await booking.CancelAppointmentAsync(second.Id, patientAccountId);
                _out.WriteLine($"Hủy lần 2: Success={secondCancel.Success} | {secondCancel.Message}");
                Assert.False(secondCancel.Success, $"Đã dùng hết hạn mức {MaxLateCancelsPerMonth} lần hủy muộn trong tháng thì lần sau phải bị từ chối.");

                var stillHolding = await ctx.Appointments.Include(a => a.Slot).SingleAsync(a => a.Id == second.Id);
                Assert.Equal(confirmed.Id, stillHolding.StatusId);
                Assert.NotNull(stillHolding.SlotPosition);
                Assert.Equal(bookedBefore - 1, stillHolding.Slot!.BookedCount);
                Assert.Equal(OnlineQuota - 1, stillHolding.Slot.OnlineBooked);
            }
        }
        finally
        {
            await CleanupAsync(cs!, appointmentIds, slotId, roomId);
        }
    }

    [Fact]
    public async Task Du_ba_lan_vang_mat_thi_chan_dat_lich_dung_ba_mua()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        DateTime day = DateTime.Today;
        DateTime now = day.Date.Add(ServiceNow.ToTimeSpan());
        int blockedAccountId = 0;
        var appointmentIds = new List<int>();

        try
        {
            await using (var ctx = NewContext(cs))
            {
                var reception = NewReception(ctx, day);
                var booking = NewBooking(ctx, day);

                int confirmedStatusId = (await ctx.AppointmentStatuses
                    .AsNoTracking()
                    .SingleAsync(s => s.Code == AppointmentStatus.Codes.Confirmed)).Id;
                int noShowStatusId = (await ctx.AppointmentStatuses
                    .AsNoTracking()
                    .SingleAsync(s => s.Code == AppointmentStatus.Codes.NoShow)).Id;

                blockedAccountId = await ctx.Accounts
                    .AsNoTracking()
                    .Where(a => a.Role.Code == "PATIENT" && a.BookingBlockedUntil == null)
                    .Select(a => a.Id)
                    .Skip(1)
                    .FirstAsync();

                // ExpireOverdueAppointmentsAsync quét toàn DB, nên nếu còn lượt thật quá 30 phút chưa xử lý
                // thì bài test sẽ đánh NO_SHOW luôn cả dữ liệu demo. Thà bỏ qua còn hơn sửa nhầm.
                int otherOverdue = await ctx.Appointments.AsNoTracking()
                    .CountAsync(a => a.CalledAt != null && a.CalledAt < now.AddMinutes(-30)
                                  && a.StatusId >= 1 && a.StatusId <= 5);
                if (otherOverdue > 0)
                {
                    _out.WriteLine($"Bỏ qua: DB còn {otherOverdue} lượt thật quá 30 phút từ lần gọi, test sẽ đụng dữ liệu demo.");
                    return;
                }

                var patients = await ctx.Patients.AsNoTracking().OrderBy(p => p.Id).Take(MaxNoShowsBeforeBlock).ToListAsync();
                if (patients.Count < MaxNoShowsBeforeBlock)
                {
                    _out.WriteLine($"Bỏ qua: DB chỉ có {patients.Count} bệnh nhân, cần {MaxNoShowsBeforeBlock}.");
                    return;
                }

                // Ba lượt web đã gọi số cách đây 40 phút và vẫn chờ: đủ điều kiện bị đánh NO_SHOW.
                for (int i = 0; i < MaxNoShowsBeforeBlock; i++)
                {
                    var appointment = NewAppointment(patients[i].Id, confirmedStatusId, blockedAccountId, $"Vắng mặt lần {i + 1}");
                    appointment.CalledAt = now.AddMinutes(-40);
                    ctx.Appointments.Add(appointment);
                    await ctx.SaveChangesAsync();
                    appointmentIds.Add(appointment.Id);
                }

                int expired = await reception.ExpireOverdueAppointmentsAsync();
                _out.WriteLine($"ExpireOverdueAppointmentsAsync trả về {expired} lượt");
                Assert.Equal(MaxNoShowsBeforeBlock, expired);

                var savedStatuses = await ctx.Appointments.AsNoTracking()
                    .Where(a => appointmentIds.Contains(a.Id))
                    .Select(a => a.StatusId)
                    .ToListAsync();
                Assert.All(savedStatuses, id => Assert.Equal(noShowStatusId, id));

                Account account = await ctx.Accounts.SingleAsync(a => a.Id == blockedAccountId);
                _out.WriteLine($"booking_blocked_until = {account.BookingBlockedUntil:dd/MM/yyyy HH:mm}, no_show_reset_at = {account.NoShowResetAt:dd/MM/yyyy HH:mm}");
                Assert.NotNull(account.BookingBlockedUntil);
                Assert.Equal(now.AddDays(BookingBlockDays), account.BookingBlockedUntil!.Value);
                Assert.Equal(now, account.NoShowResetAt!.Value);

                // Chặn chỉ áp dụng cho việc đặt lịch web mới, và xét trước khi đọc khung giờ.
                var blockedException = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => booking.CreateAppointmentAsync(blockedAccountId, patients[0].Id, 1));
                _out.WriteLine($"CreateAppointmentAsync bị chặn: {blockedException.Message}");
                Assert.Contains("bị chặn đặt lịch", blockedException.Message);
            }
        }
        finally
        {
            await CleanupAsync(cs!, appointmentIds, 0, 0);

            await using var ctx = NewContext(cs!);
            if (blockedAccountId != 0)
            {
                await ctx.Database.ExecuteSqlRawAsync(
                    "UPDATE accounts SET booking_blocked_until = NULL, no_show_reset_at = NULL WHERE id = {0}", [blockedAccountId]);
            }
        }
    }

    [Fact]
    public async Task Gan_uu_tien_muc_2_chi_chen_ba_nguoi_va_tang_overtaken_count()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        DateTime day = DateTime.Today;
        DateTime now = day.Date.Add(ServiceNow.ToTimeSpan());
        // Khung 10:07 đã qua mốc cắt 09:57 nên bốn lượt trực tiếp xếp được hết vào vị trí 1..4,
        // nhờ đó lượt cuối chỉ còn đúng ba người phía trước trong cùng khung.
        DateTime overtakenSlotStart = day.Date.Add(PastCutoffSlotStart.ToTimeSpan());
        int slotId = 0;
        int roomId = 0;
        var appointmentIds = new List<int>();

        try
        {
            int lastId;

            await using (var ctx = NewContext(cs))
            {
                var allocator = NewAllocator(ctx, day);
                var reception = NewReception(ctx, day);

                AppointmentStatus confirmed = await ctx.AppointmentStatuses
                    .AsNoTracking()
                    .SingleAsync(s => s.Code == AppointmentStatus.Codes.Confirmed);

                AppointmentSlot? template = await ctx.AppointmentSlots.AsNoTracking().OrderByDescending(s => s.StartTime).FirstOrDefaultAsync();
                if (template is null) { _out.WriteLine("Bỏ qua: DB chưa có khung giờ để mượn bác sĩ."); return; }

                if (await ctx.AppointmentSlots.AnyAsync(s => s.DoctorId == template.DoctorId
                        && s.StartTime == overtakenSlotStart))
                {
                    _out.WriteLine("Bỏ qua: bác sĩ này đã có khung giờ lúc 10:07 hôm nay.");
                    return;
                }

                roomId = await NewTestRoomAsync(ctx);
                var slot = NewSlot(template, overtakenSlotStart, roomId);
                ctx.AppointmentSlots.Add(slot);
                await ctx.SaveChangesAsync();
                slotId = slot.Id;

                var patients = await ctx.Patients.AsNoTracking().OrderBy(p => p.Id).Take(Capacity).ToListAsync();
                if (patients.Count < Capacity)
                {
                    _out.WriteLine($"Bỏ qua: DB chỉ có {patients.Count} bệnh nhân, cần {Capacity}.");
                    return;
                }

                // lấp kín khung giờ bằng bốn lượt thường để lượt cuối chỉ còn đúng ba người phía trước.
                for (int i = 0; i < Capacity; i++)
                {
                    var appointment = NewAppointment(patients[i].Id, confirmed.Id, null, $"Lượt thường thứ {i + 1}");
                    appointment.RoomId = slot.RoomId;
                    appointment.DoctorId = slot.DoctorId;
                    ctx.Appointments.Add(appointment);
                    await ctx.SaveChangesAsync();
                    appointmentIds.Add(appointment.Id);

                    Assert.NotNull(await allocator.AssignOnSiteAsync(appointment, now, slot.RoomId, slot.DoctorId));
                }

                lastId = appointmentIds[^1];
                var result = await reception.AssignStaffPriorityAsync(lastId, 10);
                _out.WriteLine($"AssignStaffPriorityAsync: Success={result.Success} | {result.Message}");
                Assert.True(result.Success);

                var prioritized = await ctx.Appointments.AsNoTracking().SingleAsync(a => a.Id == lastId);
                Assert.Equal(PriorityLevels.StaffPriority, prioritized.PriorityLevel);

                var overtakenCounts = await ctx.Appointments.AsNoTracking()
                    .Where(a => appointmentIds.Contains(a.Id) && a.Id != lastId)
                    .Select(a => a.OvertakenCount)
                    .ToListAsync();

                _out.WriteLine($"overtaken_count ba lượt bị chen: {string.Join(", ", overtakenCounts)}");
                Assert.Equal(MaxOvertakeAhead, overtakenCounts.Count(c => c == 1));
                Assert.Equal(0, overtakenCounts.Count(c => c != 1));

                var queue = await reception.GetReceptionListAsync(ReceptionTab.Waiting);
                var ours = queue.Where(r => appointmentIds.Contains(r.AppointmentId)).ToList();
                _out.WriteLine($"Thứ tự gọi của bốn lượt test: {string.Join(", ", ours.Select(o => o.QueueNumber))}");
                Assert.Equal(prioritized.QueueNumber, ours[0].QueueNumber);
            }
        }
        finally
        {
            await CleanupAsync(cs!, appointmentIds, slotId, roomId);
        }
    }

    private static async Task CleanupAsync(string cs, List<int> appointmentIds, int slotId, int roomId)
    {
        await using var ctx = NewContext(cs);

        if (appointmentIds.Count > 0)
        {
            int[] ids = [.. appointmentIds];

            // Trả chỗ cho khung giờ trước khi xoá lượt để số liệu slot của DB thử không bị lệch.
            await ctx.Database.ExecuteSqlRawAsync(@"
UPDATE appointment_slots s SET
  booked_count = GREATEST(0, booked_count - (SELECT count(*) FROM appointments a WHERE a.slot_id = s.id AND a.id = ANY ({0}))),
  online_booked = GREATEST(0, online_booked - (SELECT count(*) FROM appointments a
      WHERE a.slot_id = s.id AND a.id = ANY ({0}) AND a.booking_channel = 'WEB'
      AND a.slot_position IS NOT NULL AND a.slot_position <= s.online_quota))
WHERE s.id IN (SELECT slot_id FROM appointments WHERE id = ANY ({0}) AND slot_id IS NOT NULL)", [ids]);

            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM appointment_status_history WHERE appointment_id = ANY ({0})", [ids]);
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM appointments WHERE id = ANY ({0})", [ids]);
        }

        if (slotId != 0)
        {
            await ctx.Database.ExecuteSqlRawAsync(
                "DELETE FROM appointment_slots WHERE id = {0} AND NOT EXISTS (SELECT 1 FROM appointments a WHERE a.slot_id = appointment_slots.id)",
                [slotId]);
        }

        if (roomId != 0)
        {
            await ctx.Database.ExecuteSqlRawAsync(
                "DELETE FROM clinic_rooms WHERE id = {0} AND NOT EXISTS (SELECT 1 FROM appointment_slots s WHERE s.room_id = clinic_rooms.id)",
                [roomId]);
        }
    }

    private static AppointmentSlot NewSlot(AppointmentSlot template, DateTime startTime, int roomId) => new()
    {
        DoctorId = template.DoctorId,
        RoomId = roomId,
        StartTime = DateTime.SpecifyKind(startTime, DateTimeKind.Unspecified),
        EndTime = DateTime.SpecifyKind(startTime.AddMinutes(60), DateTimeKind.Unspecified),
        Capacity = Capacity,
        BookedCount = 0,
        OnlineQuota = OnlineQuota,
        OnlineBooked = 0,
        Status = SlotStatus.Available,
        Version = 0
    };

    private static Appointment NewAppointment(int patientId, int confirmedStatusId, int? bookedBy, string reason) => new()
    {
        AppointmentCode = $"HIS-TEST-{Guid.NewGuid():N}"[..24],
        PatientId = patientId,
        StatusId = confirmedStatusId,
        BookingChannel = bookedBy is null ? BookingChannel.OnSite : BookingChannel.Web,
        VisitType = VisitType.Scheduled,
        BookedBy = bookedBy,
        Reason = reason,
        CreatedAt = DateTime.Now,
        UpdatedAt = DateTime.Now
    };
}
