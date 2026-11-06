using HIS.BLL.DTOs;
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
/// Đường ghi của quầy trên CSDL thật theo cơ chế số A/T, chạy với đồng hồ cố định 10:15:
/// (1) lượt web giữ suất A thì check-in vẫn mang số A##;
/// (2) qua mốc cắt, suất A chưa check-in bị giải phóng và lượt đến trễ được xếp lại thành T##;
/// (3) chưa tới mốc cắt thì khách trực tiếp không chiếm được suất A;
/// (4) số = tổng capacity các khung đứng trước trong cùng phòng + ca + ngày, cộng vị trí trong khung.
/// DB lấy từ biến môi trường HIS_REHEARSAL_CONNECTION.
/// </summary>
[Trait("Category", "Integration")]
[Collection("RehearsalPostgres")]
public class ReceptionCheckInPathTests
{
    private const int CounterAccountId = 10;
    private const int Capacity = 4;
    private const int OnlineQuota = 2;
    private const int ReleaseCutoffMinutes = 10;

    private static readonly TimeOnly CutoffSlotStart = new(10, 7);
    private static readonly TimeOnly SoonSlotStart = new(10, 37);
    private static readonly TimeOnly ServiceNow = new(10, 15);

    private readonly ITestOutputHelper _out;

    public ReceptionCheckInPathTests(ITestOutputHelper out1) => _out = out1;

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

    private static (ReceptionService Reception, QueuePositionAllocator Allocator) NewServices(AppDbContext ctx, DateTime day)
    {
        var bookingOptions = Options.Create(new BookingOptions());
        var allocator = new QueuePositionAllocator(ctx, bookingOptions, Clock(day));
        return (new ReceptionService(ctx, allocator, bookingOptions, Clock(day)), allocator);
    }

    [Fact]
    public async Task Cap_so_theo_vi_tri_A_T_va_giai_phong_suat_qua_mo_cat()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        DateTime day = DateTime.Today;
        int cutoffSlotId = 0;
        int soonSlotId = 0;
        int roomId = 0;
        var appointmentIds = new List<int>();

        try
        {
            int onlineId;
            int lateId;
            int releasedId;
            string onlineNumber;

            await using (var ctx = NewContext(cs))
            {
                var (_, allocator) = NewServices(ctx, day);

                AppointmentStatus confirmed = await ctx.AppointmentStatuses
                    .AsNoTracking()
                    .SingleAsync(s => s.Code == AppointmentStatus.Codes.Confirmed);

                // Mượn phòng + bác sĩ có thật để hai khung tự dựng nằm trong ca đang chạy của DB.
                AppointmentSlot? template = await ctx.AppointmentSlots
                    .AsNoTracking()
                    .OrderByDescending(s => s.StartTime)
                    .FirstOrDefaultAsync();

                if (template is null)
                {
                    _out.WriteLine("Bỏ qua: DB chưa có khung giờ nào để mượn phòng và bác sĩ.");
                    return;
                }

                if (await ctx.AppointmentSlots.AnyAsync(s => s.DoctorId == template.DoctorId
                        && (s.StartTime == day.Date.Add(CutoffSlotStart.ToTimeSpan())
                            || s.StartTime == day.Date.Add(SoonSlotStart.ToTimeSpan()))))
                {
                    _out.WriteLine("Bỏ qua: bác sĩ này đã có khung giờ lúc 10:07 hoặc 10:37 hôm nay.");
                    return;
                }

                // Khung đã qua mốc cắt (10:07, mốc cắt 09:57) và khung chưa tới mốc cắt (10:37, mốc cắt 10:27).
                var cutoffSlot = NewSlot(template, day.Date.Add(CutoffSlotStart.ToTimeSpan()));
                var soonSlot = NewSlot(template, day.Date.Add(SoonSlotStart.ToTimeSpan()));

                ctx.AppointmentSlots.AddRange(cutoffSlot, soonSlot);
                await ctx.SaveChangesAsync();

                cutoffSlotId = cutoffSlot.Id;
                soonSlotId = soonSlot.Id;
                roomId = template.RoomId;
                _out.WriteLine($"Dựng khung quá mốc cắt #{cutoffSlot.Id} 10:07 và khung còn suất A #{soonSlot.Id} 10:37, phòng {roomId}");

                // Ba lượt khám phải thuộc ba bệnh nhân khác nhau: một bệnh nhân không được giữ
                // hai lượt còn hiệu lực trong cùng một khung giờ (uq_patient_slot_active).
                var patients = await ctx.Patients.AsNoTracking().OrderBy(p => p.Id).Take(3).ToListAsync();
                if (patients.Count < 3)
                {
                    _out.WriteLine($"Bỏ qua: DB chỉ có {patients.Count} bệnh nhân, cần 3.");
                    return;
                }

                var online = NewAppointment(patients[0].Id, confirmed.Id, "Giữ suất A khi check-in đúng giờ");
                ctx.Appointments.Add(online);
                await ctx.SaveChangesAsync();
                onlineId = online.Id;
                appointmentIds.Add(onlineId);

                // Đặt web thì nhận suất A ngay lúc đặt, chưa cần check-in.
                QueuePlacement? placement = await allocator.AssignOnlineAsync(online, soonSlot.Id);
                Assert.NotNull(placement);
                Assert.True(placement!.SlotPosition <= OnlineQuota, "Suất A phải nằm trong 1..online_quota.");
                onlineNumber = placement.QueueNumber;
                _out.WriteLine($"Lịch web #{online.Id} -> vị trí {placement.SlotPosition}, số {onlineNumber}");
                Assert.Matches("^A[0-9]{2,}$", onlineNumber);

                var late = NewAppointment(patients[1].Id, confirmed.Id, "Web đến trễ, phải xếp lại thành suất T");
                late.RoomId = soonSlot.RoomId;
                late.DoctorId = soonSlot.DoctorId;
                ctx.Appointments.Add(late);
                await ctx.SaveChangesAsync();
                lateId = late.Id;
                appointmentIds.Add(lateId);

                var released = NewAppointment(patients[2].Id, confirmed.Id, "Quá mốc cắt nên bị giải phóng suất A");
                released.RoomId = cutoffSlot.RoomId;
                released.DoctorId = cutoffSlot.DoctorId;
                ctx.Appointments.Add(released);
                await ctx.SaveChangesAsync();
                releasedId = released.Id;
                appointmentIds.Add(releasedId);

                QueuePlacement? heldSeat = await allocator.AssignOnlineAsync(released, cutoffSlot.Id);
                Assert.NotNull(heldSeat);
                _out.WriteLine($"Lịch web #{released.Id} giữ vị trí {heldSeat!.SlotPosition} số {heldSeat.QueueNumber} trên khung đã qua mốc cắt");
            }

            await using (var ctx = NewContext(cs))
            {
                var (reception, _) = NewServices(ctx, day);

                IReadOnlyList<ReceptionItemDto> waiting = await reception.GetReceptionListAsync(ReceptionTab.Waiting);
                ReceptionItemDto before = Assert.Single(waiting, i => i.AppointmentId == onlineId);
                _out.WriteLine($"Trước check-in: queue='{before.QueueNumber}' đặt bởi='{before.BookedByLabel}'");

                int expired = await reception.ExpireOverdueAppointmentsAsync();
                _out.WriteLine($"Sweep khi chưa gọi số: {expired} lượt bị đánh quá hạn");
                Assert.Equal(0, expired);

                ReceptionActionResult keepSeat = await reception.CheckInAsync(onlineId, CounterAccountId);
                _out.WriteLine($"Check-in đúng giờ: {keepSeat.Success} | {keepSeat.Message} | số={keepSeat.QueueNumber}");
                Assert.True(keepSeat.Success, keepSeat.Message);
                Assert.Equal(onlineNumber, keepSeat.QueueNumber);

                ReceptionActionResult lateCheckIn = await reception.CheckInAsync(lateId, CounterAccountId);
                _out.WriteLine($"Check-in lượt trễ: {lateCheckIn.Success} | {lateCheckIn.Message} | số={lateCheckIn.QueueNumber}");
                Assert.True(lateCheckIn.Success, lateCheckIn.Message);
                Assert.Matches("^T[0-9]{2,}$", lateCheckIn.QueueNumber);

                ReceptionActionResult releasedCheckIn = await reception.CheckInAsync(releasedId, CounterAccountId);
                _out.WriteLine($"Check-in lượt đã bị giải phóng suất A: {releasedCheckIn.Success} | {releasedCheckIn.Message} | số={releasedCheckIn.QueueNumber}");
                Assert.True(releasedCheckIn.Success, releasedCheckIn.Message);
                Assert.Matches("^T[0-9]{2,}$", releasedCheckIn.QueueNumber);

                Assert.Equal(
                    3,
                    new[] { keepSeat.QueueNumber, lateCheckIn.QueueNumber, releasedCheckIn.QueueNumber }.Distinct().Count());
            }

            await using (var ctx = NewContext(cs))
            {
                var saved = await ctx.Appointments
                    .AsNoTracking()
                    .Include(a => a.StatusHistories)
                    .Where(a => appointmentIds.Contains(a.Id))
                    .OrderBy(a => a.Id)
                    .ToListAsync();

                Assert.Equal(3, saved.Count);

                var slots = await ctx.AppointmentSlots.AsNoTracking()
                    .Where(s => s.RoomId == roomId && s.StartTime >= day && s.StartTime < day.AddDays(1))
                    .OrderBy(s => s.StartTime).ThenBy(s => s.Id)
                    .ToListAsync();

                foreach (var a in saved)
                {
                    _out.WriteLine($"#{a.Id} kênh={a.BookingChannel} slot={a.SlotId} vị trí={a.SlotPosition} số={a.QueueNumber} booked_by={a.BookedBy} checked_in={a.CheckedInAt:HH:mm:ss}");
                    Assert.NotNull(a.SlotId);
                    Assert.NotNull(a.SlotPosition);
                    Assert.Equal(CounterAccountId, a.BookedBy);
                    Assert.NotNull(a.CheckedInAt);
                    Assert.Contains(a.StatusHistories, h => h.OldStatusId != null && h.NewStatusId == a.StatusId);

                    var own = slots.Single(s => s.Id == a.SlotId);

                    // Chưa qua mốc cắt thì khách đến trễ không được ngồi vào suất A (1..online_quota).
                    bool pastCutoff = ServiceNow.ToTimeSpan() >= own.StartTime.TimeOfDay - TimeSpan.FromMinutes(ReleaseCutoffMinutes);
                    if (!pastCutoff && a.Id == lateId)
                    {
                        Assert.True(a.SlotPosition > own.OnlineQuota, $"Suất T phải nằm ngoài 1..{own.OnlineQuota}, thực tế là {a.SlotPosition}.");
                    }

                    int expectedOffset = slots
                        .Where(s => s.StartTime < own.StartTime && ExamShifts.For(s.StartTime) == ExamShifts.For(own.StartTime))
                        .Sum(s => s.Capacity);

                    string expected = a.QueueNumber![..1] + (expectedOffset + a.SlotPosition!.Value).ToString("D2");
                    _out.WriteLine($"  khung {own.StartTime:HH:mm} offset={expectedOffset} vị trí={a.SlotPosition} -> mong đợi {expected}, thực tế {a.QueueNumber}");
                    Assert.Equal(expected, a.QueueNumber);
                }

                var (reception, _) = NewServices(ctx, day);
                Assert.Equal(0, await reception.ExpireOverdueAppointmentsAsync());
            }
        }
        finally
        {
            await using var ctx = NewContext(cs!);

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
                await ctx.Database.ExecuteSqlRawAsync("DELETE FROM invoice_items WHERE invoice_id IN (SELECT id FROM invoices WHERE appointment_id = ANY ({0}))", [ids]);
                await ctx.Database.ExecuteSqlRawAsync("DELETE FROM payments WHERE invoice_id IN (SELECT id FROM invoices WHERE appointment_id = ANY ({0}))", [ids]);
                await ctx.Database.ExecuteSqlRawAsync("DELETE FROM invoices WHERE appointment_id = ANY ({0})", [ids]);
                await ctx.Database.ExecuteSqlRawAsync("DELETE FROM appointments WHERE id = ANY ({0})", [ids]);
            }

            int[] slotIds = [cutoffSlotId, soonSlotId];
            slotIds = slotIds.Where(id => id != 0).ToArray();

            if (slotIds.Length > 0)
            {
                await ctx.Database.ExecuteSqlRawAsync(
                    "DELETE FROM appointment_slots WHERE id = ANY ({0}) AND NOT EXISTS (SELECT 1 FROM appointments a WHERE a.slot_id = appointment_slots.id)",
                    [slotIds]);
            }
        }
    }

    private static AppointmentSlot NewSlot(AppointmentSlot template, DateTime startTime) => new()
    {
        DoctorId = template.DoctorId,
        RoomId = template.RoomId,
        StartTime = DateTime.SpecifyKind(startTime, DateTimeKind.Unspecified),
        EndTime = DateTime.SpecifyKind(startTime.AddMinutes(60), DateTimeKind.Unspecified),
        Capacity = Capacity,
        BookedCount = 0,
        OnlineQuota = OnlineQuota,
        OnlineBooked = 0,
        Status = SlotStatus.Available,
        Version = 0
    };

    private static Appointment NewAppointment(int patientId, int confirmedStatusId, string reason) => new()
    {
        AppointmentCode = $"HIS-TEST-{Guid.NewGuid():N}"[..24],
        PatientId = patientId,
        StatusId = confirmedStatusId,
        BookingChannel = BookingChannel.Web,
        VisitType = VisitType.Scheduled,
        Reason = reason,
        CreatedAt = DateTime.Now,
        UpdatedAt = DateTime.Now
    };
}
