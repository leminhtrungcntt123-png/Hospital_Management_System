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
/// Ba chính sách đặt lịch chạy trên CSDL Postgres thật với đồng hồ cố định 10:15:
/// (1) hủy chỉ được đến 23:59 ngày liền trước; qua mốc là từ chối, còn hủy sớm thì trả lại suất A;
/// (2) vắng mặt (NO_SHOW) không còn chặn tài khoản đặt lịch web nữa;
/// (3) hạn mức một ngày: 3 hồ sơ khác nhau / 6 lượt, hủy một lượt là đặt lại được;
/// (4) nhân viên gán ưu tiên mức 2 chỉ chen tối đa MaxOvertakeAhead người và tăng overtaken_count của họ;
/// (5) hai khai báo bảo hiểm ở bước nhập thông tin người khám được ghi xuống appointments và hiện ở danh sách quầy.
/// DB lấy từ biến môi trường HIS_REHEARSAL_CONNECTION; chưa đặt thì bỏ qua để CI không cần DB local.
/// </summary>
[Trait("Category", "Integration")]
[Collection("RehearsalPostgres")]
public class BookingPolicyPathTests
{
    private const int Capacity = 4;
    private const int OnlineQuota = 2;
    private const int MaxOvertakeAhead = 3;
    private const int MaxRelativesPerDay = 3;

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
    public async Task Huy_chi_duoc_den_23h59_ngay_lien_truoc_va_tra_lai_suot_A()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        DateTime day = DateTime.Today;
        int todaySlotId = 0;
        int tomorrowSlotId = 0;
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

                DateTime todayStart = day.Date.Add(SoonSlotStart.ToTimeSpan());
                DateTime tomorrowStart = day.Date.AddDays(1).Add(SoonSlotStart.ToTimeSpan());
                if (await ctx.AppointmentSlots.AnyAsync(s => s.DoctorId == template.DoctorId
                        && (s.StartTime == todayStart || s.StartTime == tomorrowStart)))
                {
                    _out.WriteLine("Bỏ qua: bác sĩ này đã có khung giờ 10:37 hôm nay hoặc ngày mai.");
                    return;
                }

                roomId = await NewTestRoomAsync(ctx);
                var todaySlot = NewSlot(template, todayStart, roomId);
                var tomorrowSlot = NewSlot(template, tomorrowStart, roomId);
                ctx.AppointmentSlots.AddRange(todaySlot, tomorrowSlot);
                await ctx.SaveChangesAsync();
                todaySlotId = todaySlot.Id;
                tomorrowSlotId = tomorrowSlot.Id;

                var patients = await ctx.Patients.AsNoTracking().OrderBy(p => p.Id).Take(2).ToListAsync();
                if (patients.Count < 2) { _out.WriteLine($"Bỏ qua: DB chỉ có {patients.Count} bệnh nhân, cần 2."); return; }

                // Hai lượt của cùng một tài khoản, khác ngày khám để đối chiếu hạn hủy.
                var todayAppointment = NewAppointment(patients[0].Id, confirmed.Id, patientAccountId, "Khám trong hôm nay");
                var tomorrowAppointment = NewAppointment(patients[1].Id, confirmed.Id, patientAccountId, "Khám ngày mai");
                ctx.Appointments.AddRange(todayAppointment, tomorrowAppointment);
                await ctx.SaveChangesAsync();
                appointmentIds.AddRange([todayAppointment.Id, tomorrowAppointment.Id]);

                Assert.NotNull(await allocator.AssignOnlineAsync(todayAppointment, todaySlotId));
                Assert.NotNull(await allocator.AssignOnlineAsync(tomorrowAppointment, tomorrowSlotId));

                int todayBookedBefore = (await ctx.AppointmentSlots.AsNoTracking().SingleAsync(s => s.Id == todaySlotId)).BookedCount;
                int tomorrowBookedBefore = (await ctx.AppointmentSlots.AsNoTracking().SingleAsync(s => s.Id == tomorrowSlotId)).BookedCount;

                // (a) Khám ngày hôm nay: đã qua 00:00 của ngày khám (tức qua 23:59 hôm trước) nên web hết quyền hủy.
                var refused = await booking.CancelAppointmentAsync(todayAppointment.Id, patientAccountId);
                _out.WriteLine($"Hủy lượt khám hôm nay: Success={refused.Success} | {refused.Message}");
                Assert.False(refused.Success, "Qua 23:59 ngày liền trước thì phải từ chối hủy trên web.");
                Assert.Contains("quá hạn hủy", refused.Message);

                var stillHolding = await ctx.Appointments.Include(a => a.Slot).SingleAsync(a => a.Id == todayAppointment.Id);
                Assert.Equal(confirmed.Id, stillHolding.StatusId);
                Assert.NotNull(stillHolding.SlotPosition);
                Assert.Equal(todayBookedBefore, stillHolding.Slot!.BookedCount);

                // (b) Khám ngày mai: còn trong hạn nên hủy được và suất A nhả lại cho khung giờ.
                var cancelledTomorrow = await booking.CancelAppointmentAsync(tomorrowAppointment.Id, patientAccountId);
                _out.WriteLine($"Hủy lượt khám ngày mai: Success={cancelledTomorrow.Success} | {cancelledTomorrow.Message}");
                Assert.True(cancelledTomorrow.Success);

                // So status_id chứ không đi qua navigation: các lượt này đã bị context theo dõi từ đầu nên
                // Include chỉ bật được navigation nào thực sự truy vấn, đọc .Status.Code sẽ gặp null.
                int cancelledStatusId = (await ctx.AppointmentStatuses.AsNoTracking()
                    .SingleAsync(s => s.Code == AppointmentStatus.Codes.Cancelled)).Id;

                var savedTomorrow = await ctx.Appointments.Include(a => a.Slot).SingleAsync(a => a.Id == tomorrowAppointment.Id);
                Assert.Equal(cancelledStatusId, savedTomorrow.StatusId);
                Assert.Null(savedTomorrow.SlotPosition);
                Assert.Equal(tomorrowBookedBefore - 1, savedTomorrow.Slot!.BookedCount);
                // Khung ngày mai chỉ có đúng một lượt web nên sau khi hủy thì online_booked về 0.
                Assert.Equal(0, savedTomorrow.Slot.OnlineBooked);

                var history = await ctx.AppointmentStatusHistories.AsNoTracking()
                    .SingleAsync(h => h.AppointmentId == tomorrowAppointment.Id && h.NewStatus.Code == AppointmentStatus.Codes.Cancelled);
                Assert.False(history.IsLateCancel, "Không còn phạt hủy trễ nên lịch sử không được ghi is_late_cancel.");
                Assert.Equal(patientAccountId, history.PerformedBy);
            }
        }
        finally
        {
            await CleanupAsync(cs!, appointmentIds, roomId, todaySlotId, tomorrowSlotId);
        }
    }

    [Fact]
    public async Task Vang_mat_nhieu_lan_van_duoc_dat_lich_khong_bi_chan()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        DateTime day = DateTime.Today;
        DateTime now = day.Date.Add(ServiceNow.ToTimeSpan());
        int missedAccountId = 0;
        var appointmentIds = new List<int>();

        try
        {
            await using (var ctx = NewContext(cs))
            {
                var reception = NewReception(ctx, day);

                int confirmedStatusId = (await ctx.AppointmentStatuses
                    .AsNoTracking()
                    .SingleAsync(s => s.Code == AppointmentStatus.Codes.Confirmed)).Id;
                int noShowStatusId = (await ctx.AppointmentStatuses
                    .AsNoTracking()
                    .SingleAsync(s => s.Code == AppointmentStatus.Codes.NoShow)).Id;

                missedAccountId = await ctx.Accounts
                    .AsNoTracking()
                    .Where(a => a.Role.Code == "PATIENT")
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

                var patients = await ctx.Patients.AsNoTracking().OrderBy(p => p.Id).Take(3).ToListAsync();
                if (patients.Count < 3)
                {
                    _out.WriteLine($"Bỏ qua: DB chỉ có {patients.Count} bệnh nhân, cần 3.");
                    return;
                }

                // Ba lượt web đã gọi số cách đây 40 phút và vẫn chờ: đủ điều kiện bị đánh NO_SHOW.
                for (int i = 0; i < 3; i++)
                {
                    var appointment = NewAppointment(patients[i].Id, confirmedStatusId, missedAccountId, $"Vắng mặt lần {i + 1}");
                    appointment.CalledAt = now.AddMinutes(-40);
                    ctx.Appointments.Add(appointment);
                    await ctx.SaveChangesAsync();
                    appointmentIds.Add(appointment.Id);
                }

                int expired = await reception.ExpireOverdueAppointmentsAsync();
                _out.WriteLine($"ExpireOverdueAppointmentsAsync trả về {expired} lượt");
                Assert.Equal(3, expired);

                var savedStatuses = await ctx.Appointments.AsNoTracking()
                    .Where(a => appointmentIds.Contains(a.Id))
                    .Select(a => a.StatusId)
                    .ToListAsync();
                Assert.All(savedStatuses, id => Assert.Equal(noShowStatusId, id));

                // Nghiệp vụ mới bỏ hẳn cơ chế phạt vắng mặt: vẫn ghi NO_SHOW nhưng không đặt dấu chặn.
                Account account = await ctx.Accounts.AsNoTracking().SingleAsync(a => a.Id == missedAccountId);
                _out.WriteLine($"booking_blocked_until = {(account.BookingBlockedUntil is null ? "NULL" : account.BookingBlockedUntil.Value.ToString("dd/MM/yyyy HH:mm"))}");
                Assert.Null(account.BookingBlockedUntil);
                Assert.Null(account.NoShowResetAt);
            }
        }
        finally
        {
            await CleanupAsync(cs!, appointmentIds, 0);
        }
    }

    [Fact]
    public async Task Han_muc_ngay_chan_3_nguoi_than_6_luot_va_huy_la_hoan_luot()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        DateTime day = DateTime.Today;
        // Ngày khám phải còn trong hạn đặt: cutoff là 15:00 ngày liền trước nên lấy ngày cách hai bữa.
        DateTime visitDay = day.Date.AddDays(2);
        var slotIds = new List<int>();
        var appointmentIds = new List<int>();
        int roomId = 0;

        try
        {
            await using (var ctx = NewContext(cs))
            {
                var booking = NewBooking(ctx, day);

                int patientAccountId = await ctx.Accounts
                    .AsNoTracking()
                    .Where(a => a.Role.Code == "PATIENT")
                    .Select(a => a.Id)
                    .FirstAsync();

                AppointmentSlot? template = await ctx.AppointmentSlots.AsNoTracking().OrderByDescending(s => s.StartTime).FirstOrDefaultAsync();
                if (template is null) { _out.WriteLine("Bỏ qua: DB chưa có khung giờ để mượn bác sĩ."); return; }

                // Tài khoản đã có lượt đặt hôm nay trong DB thử thì đếm hạn mức sẽ lệch -> bỏ qua.
                int bookedTodayAlready = await ctx.Appointments.AsNoTracking()
                    .CountAsync(a => a.BookedBy == patientAccountId
                                     && a.CreatedAt >= day && a.CreatedAt < day.AddDays(1)
                                     && a.Status.Code != AppointmentStatus.Codes.Cancelled);
                if (bookedTodayAlready > 0)
                {
                    _out.WriteLine($"Bỏ qua: tài khoản #{patientAccountId} đã có {bookedTodayAlready} lượt đặt hôm nay.");
                    return;
                }

                // 16:00–18:00 nằm ngoài hai ca 07:30–11:30 và 13:00–16:00 nên bác sĩ mượn chưa có khung giờ ở đó.
                DateTime testFrom = visitDay, testTo = visitDay.AddDays(1);
                DateTime firstSlotStart = visitDay.AddHours(16);
                if (await ctx.AppointmentSlots.AnyAsync(s => s.DoctorId == template.DoctorId
                        && s.StartTime >= firstSlotStart && s.StartTime < testTo))
                {
                    _out.WriteLine("Bỏ qua: bác sĩ mượn đã có khung giờ từ 16:00 trong ngày test.");
                    return;
                }

                var patients = await ctx.Patients.AsNoTracking()
                    .Where(p => !ctx.Appointments.Any(a => a.PatientId == p.Id
                                 && a.Slot != null && a.Slot.StartTime >= testFrom && a.Slot.StartTime < testTo
                                 && a.Status.Code != AppointmentStatus.Codes.Cancelled))
                    .OrderBy(p => p.Id)
                    .Take(MaxRelativesPerDay + 1)
                    .ToListAsync();
                if (patients.Count < MaxRelativesPerDay + 1)
                {
                    _out.WriteLine($"Bỏ qua: DB chỉ có {patients.Count} bệnh nhân trống ngày test, cần {MaxRelativesPerDay + 1}.");
                    return;
                }

                roomId = await NewTestRoomAsync(ctx);
                for (int i = 0; i < MaxRelativesPerDay + 1; i++)
                {
                    // Mỗi người một khung giờ khác nhau (16:00, 16:30, 17:00, 17:30) để không chạm uq (doctor_id, start_time).
                    var slot = NewSlot(template, firstSlotStart.AddMinutes(30 * i), roomId);
                    ctx.AppointmentSlots.Add(slot);
                    await ctx.SaveChangesAsync();
                    slotIds.Add(slot.Id);
                }

                // Ba người thân đầu tiên phải đặt được.
                for (int i = 0; i < MaxRelativesPerDay; i++)
                {
                    var created = await booking.CreateAppointmentAsync(patientAccountId, patients[i].Id, slotIds[i]);
                    _out.WriteLine($"Lượt {i + 1}: {created.AppointmentCode} | số {created.QueueNumber}");
                    appointmentIds.Add(created.Id);
                }

                // Người thứ tư cùng ngày: vượt 3 người thân nên bị từ chối trước cả khi trừ chỗ.
                var tooManyRelatives = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => booking.CreateAppointmentAsync(patientAccountId, patients[MaxRelativesPerDay].Id, slotIds[MaxRelativesPerDay]));
                _out.WriteLine($"Người thân thứ {MaxRelativesPerDay + 1}: {tooManyRelatives.Message}");
                Assert.Contains("người thân trong một ngày", tooManyRelatives.Message);

                // Hủy một lượt là hoàn chỉ tiêu: người thứ tư đặt lại được ngay trong cùng ngày.
                var freed = await booking.CancelAppointmentAsync(appointmentIds[0], patientAccountId);
                Assert.True(freed.Success, freed.Message);

                var fourth = await booking.CreateAppointmentAsync(patientAccountId, patients[MaxRelativesPerDay].Id, slotIds[MaxRelativesPerDay]);
                _out.WriteLine($"Sau khi hủy lượt đầu, người thứ {MaxRelativesPerDay + 1} đặt được: {fourth.AppointmentCode}");
                appointmentIds.Add(fourth.Id);
            }
        }
        finally
        {
            await CleanupAsync(cs!, appointmentIds, roomId, [.. slotIds]);
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
            await CleanupAsync(cs!, appointmentIds, roomId, slotId);
        }
    }

    [Fact]
    public async Task Hai_khai_bao_bao_hiem_duoc_luu_va_hien_o_danh_sach_quay()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        DateTime day = DateTime.Today;
        // Cutoff là 15:00 ngày liền trước nên ngày khám phải còn cách hôm nay hơn một ngày.
        DateTime visitDay = day.Date.AddDays(2);
        int slotId = 0;
        int roomId = 0;
        var appointmentIds = new List<int>();

        try
        {
            string appointmentCode;

            await using (var ctx = NewContext(cs))
            {
                var booking = NewBooking(ctx, day);

                // Hạn mức một ngày đếm theo booked_by (6 lượt, 3 người thân) nên phải chọn tài khoản còn chỉ tiêu;
                // DB thử có sẵn lượt demo trong ngày nên lấy tài khoản đặt ít nhất rồi kiểm tra hai mốc.
                var candidate = await ctx.Accounts
                    .Where(a => a.Role.Code == "PATIENT")
                    .Select(a => new
                    {
                        a.Id,
                        BookedToday = ctx.Appointments.Count(b => b.BookedBy == a.Id
                            && b.CreatedAt >= day && b.CreatedAt < day.AddDays(1)
                            && b.Status.Code != AppointmentStatus.Codes.Cancelled)
                    })
                    .OrderBy(x => x.BookedToday)
                    .FirstAsync();

                if (candidate.BookedToday >= Booking.Value.MaxBookingsPerDay)
                {
                    _out.WriteLine($"Bỏ qua: mọi tài khoản bệnh nhân đều đã đặt đủ {Booking.Value.MaxBookingsPerDay} lượt hôm nay.");
                    return;
                }

                int patientAccountId = candidate.Id;

                AppointmentSlot? template = await ctx.AppointmentSlots.AsNoTracking()
                    .OrderByDescending(s => s.StartTime).FirstOrDefaultAsync();
                if (template is null) { _out.WriteLine("Bỏ qua: DB chưa có khung giờ để mượn bác sĩ."); return; }

                DateTime slotStart = visitDay.AddHours(16);
                if (await ctx.AppointmentSlots.AnyAsync(s => s.DoctorId == template.DoctorId
                        && s.StartTime >= slotStart && s.StartTime < visitDay.AddDays(1)))
                {
                    _out.WriteLine("Bỏ qua: bác sĩ mượn đã có khung giờ từ 16:00 trong ngày khám.");
                    return;
                }

                DateTime testFrom = visitDay, testTo = visitDay.AddDays(1);
                int patientId = await ctx.Patients
                    .Where(p => !ctx.Appointments.Any(a => a.PatientId == p.Id
                                 && a.Slot != null && a.Slot.StartTime >= testFrom && a.Slot.StartTime < testTo
                                 && a.Status.Code != AppointmentStatus.Codes.Cancelled))
                    .OrderBy(p => p.Id)
                    .Select(p => p.Id)
                    .FirstOrDefaultAsync();
                if (patientId == 0) { _out.WriteLine("Bỏ qua: không còn bệnh nhân trống ngày khám."); return; }

                roomId = await NewTestRoomAsync(ctx);
                var slot = NewSlot(template, slotStart, roomId);
                ctx.AppointmentSlots.Add(slot);
                await ctx.SaveChangesAsync();
                slotId = slot.Id;

                var created = await booking.CreateAppointmentAsync(
                    patientAccountId, patientId, slotId, hasHealthInsurance: true, hasPrivateInsurance: true);
                appointmentIds.Add(created.Id);
                appointmentCode = created.AppointmentCode;
                _out.WriteLine($"Đặt lịch {created.AppointmentCode} | số {created.QueueNumber}");

                // Đọc lại bằng AsNoTracking để chắc hai cột thực sự nằm trên DB chứ không chỉ trên object in-memory.
                var saved = await ctx.Appointments.AsNoTracking().SingleAsync(a => a.Id == created.Id);
                Assert.True(saved.HasHealthInsurance);
                Assert.True(saved.HasPrivateInsurance);
            }

            await using (var ctx = NewContext(cs))
            {
                var reception = NewReception(ctx, day);
                var rows = await reception.GetReceptionListAsync(ReceptionTab.Scheduled);
                var row = rows.Single(r => r.AppointmentCode == appointmentCode);

                _out.WriteLine($"Quầy thấy {row.PatientName}: khai bảo hiểm = \"{row.InsuranceDeclaration}\"");
                Assert.Equal("BHYT + tư nhân", row.InsuranceDeclaration);
            }
        }
        finally
        {
            await CleanupAsync(cs!, appointmentIds, roomId, slotId);
        }
    }

    private static async Task CleanupAsync(string cs, List<int> appointmentIds, int roomId, params int[] slotIds)
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

        foreach (int slotId in slotIds.Where(id => id != 0))
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
