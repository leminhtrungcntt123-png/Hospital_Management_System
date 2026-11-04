using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace HIS.Tests;

/// <summary>
/// Các ràng buộc DB của mô hình số A/T và khả năng chống đụng độ khi hai quầy cùng bốc một suất,
/// chạy trên CSDL Postgres thật (biến môi trường HIS_REHEARSAL_CONNECTION):
/// (i) ck_online_booked chặn online_booked vượt chỉ tiêu A hoặc vượt tổng suất đang giữ;
/// (ii) uq_slot_position chặn hai lượt còn hiệu lực cùng giữ một vị trí trong khung, nhưng vẫn cho trùng
///      vị trí khi một lượt đã CANCELLED vì index có điều kiện lọc status_id 1..5;
/// (iii) ck_appointments_priority_level chỉ nhận 0..2, mức 3 của mô hình cũ bị từ chối;
/// (iv) hai phiên cùng chọn suất T cuối của một khung: phiên ghi sau nhận lỗi unique, CSDL chỉ còn một lượt giữ chỗ.
/// Mọi thao tác nằm trong transaction và rollback nên không để lại lượt khám; chỉ khung giờ dựng lên là phải dọn.
/// </summary>
[Trait("Category", "Integration")]
[Collection("RehearsalPostgres")]
public class SlotConstraintTests
{
    private const int Capacity = 4;
    private const int OnlineQuota = 2;

    // Mã riêng để Probe dọn được cả lượt thua cuộc (giao dịch abort nên slot_id vẫn NULL, không dò theo khung được).
    private const string CodePrefix = "HIS-TSTC-";

    // Giờ khung riêng (10:47) để không đụng khung mà các bài test quầy đang dựng.
    private static readonly TimeOnly SlotStart = new(10, 47);

    private readonly ITestOutputHelper _out;

    public SlotConstraintTests(ITestOutputHelper out1) => _out = out1;

    private static string? Connection => Environment.GetEnvironmentVariable("HIS_REHEARSAL_CONNECTION");

    private static AppDbContext NewContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options, TimeProvider.System);
    }

    [Fact]
    public async Task CK_online_booked_chan_so_a_vuot_chi_tieu_hoac_vuot_tong_suat_giu_chu()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        await using Probe probe = await Probe.OpenAsync(cs, _out);
        if (!probe.HasSlot) return;

        await using var ctx = NewContext(cs);

        // Vượt chỉ tiêu A: online_quota 2 nhưng ghi 3.
        await using var txOverQuota = await ctx.Database.BeginTransactionAsync();
        string overQuota = await SqlStateAsync(() =>
            ctx.Database.ExecuteSqlRawAsync("UPDATE appointment_slots SET online_booked = {0} WHERE id = {1}",
                [OnlineQuota + 1, probe.SlotId]));
        _out.WriteLine($"online_booked = {OnlineQuota + 1} trên chỉ tiêu {OnlineQuota}: {overQuota}");
        Assert.Equal(PostgresErrorCodes.CheckViolation, overQuota);
        await txOverQuota.RollbackAsync();

        // Vượt tổng suất đang giữ: booked_count 0 nhưng online_booked 1.
        await using var txOverBooked = await ctx.Database.BeginTransactionAsync();
        string overBooked = await SqlStateAsync(() =>
            ctx.Database.ExecuteSqlRawAsync("UPDATE appointment_slots SET booked_count = 0, online_booked = 1 WHERE id = {0}",
                [probe.SlotId]));
        _out.WriteLine($"booked_count 0 / online_booked 1: {overBooked}");
        Assert.Equal(PostgresErrorCodes.CheckViolation, overBooked);
        await txOverBooked.RollbackAsync();
    }

    [Fact]
    public async Task Uq_slot_position_khong_cho_hai_luot_con_hieu_luc_cung_mot_vi_tri()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        await using Probe probe = await Probe.OpenAsync(cs, _out);
        if (!probe.HasSlot) return;

        // Suất T đầu tiên: chưa qua mốc cắt nên cả hai lượt cùng nhắm vào đúng vị trí này.
        const int position = OnlineQuota + 1;

        await using var ctx = NewContext(cs);

        await using var txDuplicate = await ctx.Database.BeginTransactionAsync();
        ctx.Appointments.Add(NewAppointment(probe.PatientIds[0], probe.ConfirmedStatusId, probe.SlotId, position));
        await ctx.SaveChangesAsync();

        ctx.Appointments.Add(NewAppointment(probe.PatientIds[1], probe.ConfirmedStatusId, probe.SlotId, position));
        string duplicate = await SqlStateAsync(() => ctx.SaveChangesAsync());
        _out.WriteLine($"Hai lượt còn hiệu lực cùng vị trí {position}: {duplicate}");
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate);
        await txDuplicate.RollbackAsync();

        // SaveChanges thất bại vẫn để lại entity Added trong change tracker, nên phải xóa theo dõi
        // trước khi.insert tiếp, không là lần ghi sau sẽ lặp lại đúng lỗi trùng vị trí vừa rollback.
        ctx.ChangeTracker.Clear();

        // Vị trí chỉ bị coi là đang giữ khi lượt còn trong hàng đợi, nên lượt đã hủy không chiếm chỗ:
        // nhờ vậy suất A trả về cho T không bị index chặn dù khung còn lưu lượt hủy.
        await using var txCancelled = await ctx.Database.BeginTransactionAsync();
        ctx.Appointments.Add(NewAppointment(probe.PatientIds[0], probe.ConfirmedStatusId, probe.SlotId, position));
        await ctx.SaveChangesAsync();
        ctx.Appointments.Add(NewAppointment(probe.PatientIds[1], probe.CancelledStatusId, probe.SlotId, position));
        await ctx.SaveChangesAsync();
        _out.WriteLine($"Vị trí {position} trùng nhưng một lượt đã CANCELLED: được chấp nhận");
        await txCancelled.RollbackAsync();
    }

    [Fact]
    public async Task Priority_level_khong_nhan_muc_3()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        await using Probe probe = await Probe.OpenAsync(cs, _out);
        if (!probe.HasSlot) return;

        await using var ctx = NewContext(cs);

        await using var txAccepted = await ctx.Database.BeginTransactionAsync();
        var staff = NewAppointment(probe.PatientIds[0], probe.ConfirmedStatusId, null, null);
        staff.PriorityLevel = PriorityLevels.StaffPriority;
        ctx.Appointments.Add(staff);
        await ctx.SaveChangesAsync();
        _out.WriteLine($"priority_level = {PriorityLevels.StaffPriority}: được chấp nhận");
        await txAccepted.RollbackAsync();
        ctx.ChangeTracker.Clear();

        // Mức 3 (Emergency) của mô hình cũ đã bỏ: cấp cứu là cờ nghiệp vụ ở quầy, không phải một mức ưu tiên.
        await using var txRejected = await ctx.Database.BeginTransactionAsync();
        var emergency = NewAppointment(probe.PatientIds[1], probe.ConfirmedStatusId, null, null);
        emergency.PriorityLevel = 3;
        ctx.Appointments.Add(emergency);
        string rejected = await SqlStateAsync(() => ctx.SaveChangesAsync());
        _out.WriteLine($"priority_level = 3: {rejected}");
        Assert.Equal(PostgresErrorCodes.CheckViolation, rejected);
        await txRejected.RollbackAsync();
    }

    [Fact]
    public async Task Hai_quoi_cung_boc_suot_cuoi_chi_mot_luot_giu_duoc_vi_tri()
    {
        var cs = Connection;
        if (cs is null) { _out.WriteLine("Bỏ qua: chưa đặt HIS_REHEARSAL_CONNECTION"); return; }

        await using Probe probe = await Probe.OpenAsync(cs, _out);
        if (!probe.HasSlot) return;

        var bookingOptions = Options.Create(new BookingOptions());

        // Giữ trước vị trí 1..3 bằng lượt ON_SITE ghi thẳng, không qua allocator, để khung capacity 4
        // chỉ còn ĐÚNG MỘT suất. Như vậy kết quả races không phụ thuộc thứ tự đọc/ghi còn an toàn.
        await using (var setup = NewContext(cs))
        {
            for (int position = 1; position < Capacity; position++)
            {
                setup.Appointments.Add(NewAppointment(
                    probe.PatientIds[position - 1], probe.ConfirmedStatusId, probe.SlotId, position));
            }

            AppointmentSlot slot = await setup.AppointmentSlots.SingleAsync(s => s.Id == probe.SlotId);
            slot.BookedCount = Capacity - 1;
            slot.Status = SlotStatus.Available;
            await setup.SaveChangesAsync();
        }

        await using var ctxA = NewContext(cs);
        await using var ctxB = NewContext(cs);

        var appointmentA = NewAppointment(probe.PatientIds[Capacity - 1], probe.ConfirmedStatusId, null, null);
        var appointmentB = NewAppointment(probe.PatientIds[Capacity], probe.ConfirmedStatusId, null, null);
        ctxA.Appointments.Add(appointmentA);
        ctxB.Appointments.Add(appointmentB);
        await ctxA.SaveChangesAsync();
        await ctxB.SaveChangesAsync();

        var allocatorA = new QueuePositionAllocator(ctxA, bookingOptions, TimeProvider.System);
        var allocatorB = new QueuePositionAllocator(ctxB, bookingOptions, TimeProvider.System);

        // Mốc cắt của khung 10:47 là 10:37; gọi lúc 10:25 nên suất A vẫn dành cho web, hai quầy chỉ tranh nhau suất T cuối.
        DateTime now = DateTime.Today.Add(SlotStart.ToTimeSpan()).AddMinutes(-22);

        // Hai ca trực ở hai máy, hai connection riêng, bốc cùng lúc.
        var taskA = TryAssignAsync(() => allocatorA.AssignOnSiteAsync(appointmentA, now, probe.RoomId, probe.DoctorId));
        var taskB = TryAssignAsync(() => allocatorB.AssignOnSiteAsync(appointmentB, now, probe.RoomId, probe.DoctorId));
        (QueuePlacement? Placed, string Error) first = await taskA;
        (QueuePlacement? Placed, string Error) second = await taskB;

        _out.WriteLine($"Quầy A: {(first.Placed?.QueueNumber ?? "không có chỗ")} | lỗi {first.Error}");
        _out.WriteLine($"Quầy B: {(second.Placed?.QueueNumber ?? "không có chỗ")} | lỗi {second.Error}");

        // Bên đọc muộn thấy suất cuối đã kín nên trả về null; bên đọc sớm ghi trùng vị trí thì uq_slot_position
        // từ chối. Cả hai nhánh đều chỉ cho một lượt thắng.
        Assert.Equal(1, (first.Placed is null ? 0 : 1) + (second.Placed is null ? 0 : 1));

        await using var check = NewContext(cs);
        var holders = await check.Appointments.AsNoTracking()
            .Where(a => a.SlotId == probe.SlotId && a.SlotPosition != null)
            .Select(a => new { a.QueueNumber, Position = a.SlotPosition!.Value })
            .ToListAsync();

        AppointmentSlot finalSlot = await check.AppointmentSlots.AsNoTracking().SingleAsync(s => s.Id == probe.SlotId);

        _out.WriteLine($"Khung #{probe.SlotId} giữ {holders.Count}/{finalSlot.Capacity} suất, booked_count = {finalSlot.BookedCount}, " +
                       $"vị trí: {string.Join(", ", holders.OrderBy(h => h.Position).Select(h => h.Position))}");

        Assert.Equal(Capacity, holders.Count);
        Assert.Equal(Capacity, holders.Select(h => h.Position).Distinct().Count());
        Assert.Equal(Capacity, finalSlot.BookedCount);
        Assert.Equal(SlotStatus.Full, finalSlot.Status);
    }

    /// <summary>Chạy một lần cấp chỗ và bắt lỗi unique thành dữ liệu để so sánh, không làm hỏng task song song.</summary>
    private static async Task<(QueuePlacement? Placed, string Error)> TryAssignAsync(Func<Task<QueuePlacement?>> action)
    {
        try
        {
            return (await action(), "không lỗi");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException duplicate)
        {
            return (null, duplicate.SqlState);
        }
        catch (PostgresException ex)
        {
            return (null, ex.SqlState);
        }
    }

    /// <summary>Trả về SqlState của lỗi Postgres, hoặc chuỗi báo ngoài dự kiến nếu thao tác lại thành công.</summary>
    private static async Task<string> SqlStateAsync(Func<Task> action)
    {
        try
        {
            await action();
            return "thành công ngoài dự kiến";
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException duplicate)
        {
            return duplicate.SqlState;
        }
        catch (PostgresException ex)
        {
            return ex.SqlState;
        }
    }

    private static Appointment NewAppointment(int patientId, int statusId, int? slotId, int? slotPosition) => new()
    {
        AppointmentCode = $"{CodePrefix}{Guid.NewGuid():N}"[..24],
        PatientId = patientId,
        StatusId = statusId,
        SlotId = slotId,
        BookingChannel = BookingChannel.OnSite,
        VisitType = VisitType.Scheduled,
        SlotPosition = slotPosition,
        PriorityLevel = PriorityLevels.Normal,
        CreatedAt = DateTime.Now,
        UpdatedAt = DateTime.Now
    };

    /// <summary>
    /// Dựng một phòng khám riêng rồi đặt khung 10:47 hôm nay (capacity 4 / chỉ tiêu A 2) vào đó, mượn bác sĩ
    /// của khung xa nhất trong DB. Phòng riêng là bắt buộc: cấp số T luôn quét giải phóng suất A quá mốc cắt
    /// của cả phòng trong ngày, nên nếu dùng phòng thật thì test sẽ ăn mất chỗ A01 của dữ liệu demo.
    /// Dispose xóa lượt khám, khung rồi phòng; các transaction trong test tự rollback.
    /// </summary>
    private sealed class Probe : IAsyncDisposable
    {
        public bool HasSlot { get; set; }
        public int SlotId { get; set; }
        public int RoomId { get; set; }
        public int DoctorId { get; set; }
        public int ConfirmedStatusId { get; set; }
        public int CancelledStatusId { get; set; }
        public int[] PatientIds { get; set; } = [];

        private string ConnectionString { get; set; } = string.Empty;

        public static async Task<Probe> OpenAsync(string cs, ITestOutputHelper out1)
        {
            var probe = new Probe { ConnectionString = cs, HasSlot = false };
            await using var ctx = NewContext(cs);

            DateTime startTime = DateTime.Today.Add(SlotStart.ToTimeSpan());

            // Bác sĩ mượn để dựng khung phải chưa có khung nào đúng giờ 10:47 (uq_slot chặn trùng doctor + giờ).
            int? doctorId = await ctx.Doctors.AsNoTracking()
                .Where(d => !ctx.AppointmentSlots.Any(s => s.DoctorId == d.Id && s.StartTime == startTime))
                .OrderByDescending(d => d.Id)
                .Select(d => (int?)d.Id)
                .FirstOrDefaultAsync();

            int? departmentId = await ctx.Departments.AsNoTracking()
                .OrderBy(d => d.Id)
                .Select(d => (int?)d.Id)
                .FirstOrDefaultAsync();

            var statuses = await ctx.AppointmentStatuses.AsNoTracking()
                .Where(s => s.Code == AppointmentStatus.Codes.Confirmed || s.Code == AppointmentStatus.Codes.Cancelled)
                .Select(s => new { s.Code, s.Id })
                .ToListAsync();

            // Một lượt/bệnh nhân một khung (uq_patient_slot_active) nên cần đủ số bệnh nhân cho cả khung test.
            probe.PatientIds = await ctx.Patients.AsNoTracking()
                .OrderBy(p => p.Id).Take(Capacity + 1).Select(p => p.Id).ToArrayAsync();

            if (doctorId is null || departmentId is null || statuses.Count < 2 || probe.PatientIds.Length < Capacity + 1)
            {
                probe.HasSlot = false;
                out1.WriteLine($"Bỏ qua: thiếu bác sĩ ({doctorId?.ToString() ?? "0"}), khoa ({departmentId?.ToString() ?? "0"}), " +
                               $"trạng thái ({statuses.Count}) hoặc bệnh nhân ({probe.PatientIds.Length}/{Capacity + 1}).");
                return probe;
            }

            var room = new ClinicRoom
            {
                DepartmentId = departmentId.Value,
                RoomName = "Phòng test ràng buộc số",
                RoomNumber = $"TEST-{Guid.NewGuid():N}"[..13]
            };

            ctx.ClinicRooms.Add(room);
            await ctx.SaveChangesAsync();

            var slot = new AppointmentSlot
            {
                DoctorId = doctorId.Value,
                RoomId = room.Id,
                StartTime = DateTime.SpecifyKind(startTime, DateTimeKind.Unspecified),
                EndTime = DateTime.SpecifyKind(startTime.AddMinutes(60), DateTimeKind.Unspecified),
                Capacity = Capacity,
                OnlineQuota = OnlineQuota,
                BookedCount = 0,
                OnlineBooked = 0,
                Status = SlotStatus.Available,
                Version = 0
            };

            ctx.AppointmentSlots.Add(slot);
            await ctx.SaveChangesAsync();

            probe.HasSlot = true;
            probe.SlotId = slot.Id;
            probe.RoomId = slot.RoomId;
            probe.DoctorId = slot.DoctorId;
            probe.ConfirmedStatusId = statuses.Single(s => s.Code == AppointmentStatus.Codes.Confirmed).Id;
            probe.CancelledStatusId = statuses.Single(s => s.Code == AppointmentStatus.Codes.Cancelled).Id;
            out1.WriteLine($"Dựng phòng test #{room.Id} ({room.RoomNumber}) và khung #{slot.Id} 10:47, bác sĩ {slot.DoctorId}, capacity {Capacity} / chỉ tiêu A {OnlineQuota}");
            return probe;
        }

        public async ValueTask DisposeAsync()
        {
            if (!HasSlot) return;

            await using var ctx = NewContext(ConnectionString);

            await ctx.Database.ExecuteSqlRawAsync(
                "DELETE FROM appointment_status_history WHERE appointment_id IN " +
                "(SELECT id FROM appointments WHERE appointment_code LIKE {0})", [$"{CodePrefix}%"]);
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM appointments WHERE appointment_code LIKE {0}", [$"{CodePrefix}%"]);

            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM appointment_slots WHERE id = {0}", [SlotId]);
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM clinic_rooms WHERE id = {0}", [RoomId]);
        }
    }
}
