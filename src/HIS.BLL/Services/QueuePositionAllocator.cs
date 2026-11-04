using HIS.BLL.Options;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HIS.BLL.Services;

/// <summary>
/// Kết quả cấp chỗ: khung giờ giữ chỗ, vị trí trong khung (1..capacity) và số hiển thị trên màn gọi (A##/T##).
/// </summary>
public sealed record QueuePlacement(AppointmentSlot Slot, int SlotPosition, string QueueNumber);

/// <summary>
/// Cấp số thứ tự theo vị trí cố định trong khung giờ, thay cho bộ đếm queue_counters đã bỏ.
/// Số = tổng capacity các khung đứng trước trong cùng phòng + ca + ngày, cộng với vị trí trong khung,
/// nên dãy số của một phòng chạy liên tục 1,2,3... và chỉ cần đọc slot là dựng lại được, không cần lưu bộ đếm.
/// Hai quầy bấm cùng lúc chỉ có thể đụng nhau khi chọn cùng một vị trí, và uq_slot_position sẽ từ chối lượt ghi sau.
/// </summary>
public class QueuePositionAllocator
{
    // Trạng thái còn giữ chỗ: PENDING, CONFIRMED, CHECKED_IN, READY_FOR_DOCTOR, IN_PROGRESS.
    // Đúng tập trạng thái mà uq_slot_position lọc nên không được lệch.
    private static readonly int[] ActiveStatusIds = [1, 2, 3, 4, 5];

    private readonly AppDbContext _context;
    private readonly BookingOptions _options;
    private readonly TimeProvider _timeProvider;

    public QueuePositionAllocator(AppDbContext context, IOptions<BookingOptions> options, TimeProvider timeProvider)
    {
        _context = context;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    // Mốc cắt phải so với giờ hiện tại, không phải giờ bắt đầu khung: lấy giờ khung ra so thì mọi lượt
    // web vừa đặt đều bị coi là quá hạn và bị giải phóng ngay lần đặt kế tiếp trong phòng.
    private DateTime Now => _timeProvider.GetLocalNow().DateTime;

    /// <summary>
    /// Cấp suất T (đến trực tiếp) cho một lượt khám: khung đang mở trước, hết suất thì sang khung kế tiếp,
    /// hết cả ca thì trả về null để quầy báo "hết số". Lượt khám được gắn luôn slot_id, slot_position và queue_number.
    /// </summary>
    public async Task<QueuePlacement?> AssignOnSiteAsync(
        Appointment appointment,
        DateTime at,
        int? roomId,
        int? doctorId,
        CancellationToken cancellationToken = default)
    {
        if (roomId == null)
        {
            return null;
        }

        await ReleaseExpiredOnlinePositionsAsync(roomId.Value, at, cancellationToken);

        var shiftSlots = await LoadShiftAsync(roomId.Value, at, ExamShifts.For(at), cancellationToken);

        foreach (var entry in shiftSlots)
        {
            var slot = entry.Slot;

            // Khung đã qua giờ kết thúc thì không nhận thêm người, nhưng capacity của nó vẫn nằm trong dãy số.
            if (slot.EndTime <= at || slot.Status == SlotStatus.Blocked)
            {
                continue;
            }

            if (doctorId is int wantedDoctorId && slot.DoctorId != wantedDoctorId)
            {
                continue;
            }

            // Chưa tới mốc cắt thì vị trí 1..online_quota vẫn dành riêng cho kênh A.
            // Qua mốc cắt, suất A chưa ai nhận được mở cho bệnh nhân đến trực tiếp.
            int firstPosition = IsPastReleaseCutoff(slot, at) ? 1 : slot.OnlineQuota + 1;

            for (int position = firstPosition; position <= slot.Capacity; position++)
            {
                if (entry.TakenPositions.Contains(position))
                {
                    continue;
                }

                return await ReserveAsync(appointment, entry, position, BookingChannel.OnSite, cancellationToken);
            }
        }

        return null;
    }

    /// <summary>
    /// Cấp suất A (đặt online) vào khung giờ bệnh nhân đã chọn. Hết suất A thì trả về null,
    /// không tự đẩy sang suất T vì suất T dành cho người đến quầy.
    /// </summary>
    public async Task<QueuePlacement?> AssignOnlineAsync(
        Appointment appointment,
        int slotId,
        CancellationToken cancellationToken = default)
    {
        var slot = await _context.AppointmentSlots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == slotId, cancellationToken);

        if (slot is null)
        {
            return null;
        }

        await ReleaseExpiredOnlinePositionsAsync(slot.RoomId, Now, cancellationToken);

        var shiftSlots = await LoadShiftAsync(slot.RoomId, slot.StartTime, ExamShifts.For(slot.StartTime), cancellationToken);
        var entry = shiftSlots.FirstOrDefault(e => e.Slot.Id == slotId);

        if (entry is null)
        {
            return null;
        }

        for (int position = 1; position <= entry.Slot.OnlineQuota; position++)
        {
            if (entry.TakenPositions.Contains(position))
            {
                continue;
            }

            return await ReserveAsync(appointment, entry, position, BookingChannel.Web, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Qua mốc cắt (trước giờ khung bắt đầu ReleaseCutoffMinutes) mà suất A vẫn chưa check-in thì giải phóng:
    /// vị trí trả về NULL để kênh T dùng được, booked_count và online_booked giảm theo.
    /// Lượt hẹn vẫn giữ trạng thái cũ; bệnh nhân đến trễ được cấp lại suất T khi check-in.
    /// </summary>
    public async Task<int> ReleaseExpiredOnlinePositionsAsync(
        int roomId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var dayStart = DayStart(now);
        var dayEnd = dayStart.AddDays(1);

        var slots = await _context.AppointmentSlots
            .Where(s => s.RoomId == roomId && s.StartTime >= dayStart && s.StartTime < dayEnd)
            .ToListAsync(cancellationToken);

        var releasable = slots
            .Where(s => s.OnlineQuota > 0 && IsPastReleaseCutoff(s, now))
            .ToDictionary(s => s.Id, s => s);

        if (releasable.Count == 0)
        {
            return 0;
        }

        var slotIds = releasable.Keys.ToList();
        var held = await _context.Appointments
            .Where(a => a.SlotId != null
                        && slotIds.Contains(a.SlotId.Value)
                        && a.SlotPosition != null
                        && a.BookingChannel == BookingChannel.Web
                        && a.CheckedInAt == null
                        && ActiveStatusIds.Contains(a.StatusId))
            .ToListAsync(cancellationToken);

        int released = 0;
        foreach (var appointment in held)
        {
            var slot = releasable[appointment.SlotId!.Value];
            if (appointment.SlotPosition > slot.OnlineQuota)
            {
                continue;
            }

            ReleaseSeat(appointment, slot);
            released++;
        }

        if (released > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return released;
    }

    /// <summary>
    /// Trả lại suất mà lượt khám đang giữ để người khác dùng được: vị trí về NULL,
    /// booked_count/online_booked giảm theo, trạng thái khung tính lại.
    /// Chỉ sửa entity đang được track; caller quyết định thời điểm SaveChanges để việc trả suất
    /// nằm chung transaction với thao tác đóng lượt khám.
    /// </summary>
    public static void ReleaseSeat(Appointment appointment, AppointmentSlot? slot = null)
    {
        slot ??= appointment.Slot;

        if (appointment.SlotPosition is not int position || slot is null)
        {
            return;
        }

        bool holdsOnlineSeat = appointment.BookingChannel == BookingChannel.Web && position <= slot.OnlineQuota;

        appointment.SlotPosition = null;
        slot.BookedCount = Math.Max(0, slot.BookedCount - 1);

        if (holdsOnlineSeat)
        {
            slot.OnlineBooked = Math.Max(0, slot.OnlineBooked - 1);
        }

        slot.Status = slot.BookedCount >= slot.Capacity ? SlotStatus.Full : SlotStatus.Available;
        slot.Version++;
    }

    private bool IsPastReleaseCutoff(AppointmentSlot slot, DateTime at) =>
        at >= slot.StartTime.AddMinutes(-_options.ReleaseCutoffMinutes);

    private async Task<QueuePlacement> ReserveAsync(
        Appointment appointment,
        ShiftSlot entry,
        int position,
        BookingChannel channel,
        CancellationToken cancellationToken)
    {
        var slot = entry.Slot;

        appointment.SlotId = slot.Id;
        appointment.Slot = slot;
        appointment.SlotPosition = position;
        appointment.QueueNumber = QueueNumberPrefix.Format(channel, QueuePositions.ShiftNumber(entry.CapacityBefore, position));

        slot.BookedCount++;
        if (channel == BookingChannel.Web && position <= slot.OnlineQuota)
        {
            slot.OnlineBooked++;
        }

        slot.Status = slot.BookedCount >= slot.Capacity ? SlotStatus.Full : SlotStatus.Available;
        slot.Version++;

        await _context.SaveChangesAsync(cancellationToken);

        return new QueuePlacement(slot, position, appointment.QueueNumber);
    }

    /// <summary>
    /// Dựng dãy khung giờ của một phòng trong cùng ca khám, kèm tổng capacity đứng trước mỗi khung
    /// và tập vị trí đã có người giữ. Capacity của khung đã qua vẫn được cộng dồn để dãy số không bị nhảy.
    /// </summary>
    private async Task<List<ShiftSlot>> LoadShiftAsync(
        int roomId,
        DateTime at,
        ExamShift shift,
        CancellationToken cancellationToken)
    {
        var dayStart = DayStart(at);
        var dayEnd = dayStart.AddDays(1);

        var slots = await _context.AppointmentSlots
            .Where(s => s.RoomId == roomId && s.StartTime >= dayStart && s.StartTime < dayEnd)
            .OrderBy(s => s.StartTime)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        // Ca khám suy ra từ giờ bắt đầu khung; ExamShifts.For không dịch sang SQL nên lọc trong bộ nhớ.
        // Một ngày của một phòng chỉ vài khung nên không đáng lo.
        var shiftSlots = slots.Where(s => ExamShifts.For(s.StartTime) == shift).ToList();

        var slotIds = shiftSlots.Select(s => s.Id).ToList();
        var takenBySlot = new Dictionary<int, HashSet<int>>();

        if (slotIds.Count > 0)
        {
            var taken = await _context.Appointments
                .AsNoTracking()
                .Where(a => a.SlotId != null
                            && slotIds.Contains(a.SlotId.Value)
                            && a.SlotPosition != null
                            && ActiveStatusIds.Contains(a.StatusId))
                .Select(a => new { a.SlotId, a.SlotPosition })
                .ToListAsync(cancellationToken);

            takenBySlot = taken
                .GroupBy(t => t.SlotId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(x => x.SlotPosition!.Value).ToHashSet());
        }

        var result = new List<ShiftSlot>();
        int capacityBefore = 0;

        foreach (var slot in shiftSlots)
        {
            result.Add(new ShiftSlot(
                slot,
                capacityBefore,
                takenBySlot.TryGetValue(slot.Id, out var positions) ? positions : []));
            capacityBefore += slot.Capacity;
        }

        return result;
    }

    private static DateTime DayStart(DateTime at)
    {
        var date = DateOnly.FromDateTime(at);
        return DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
    }

    private sealed record ShiftSlot(AppointmentSlot Slot, int CapacityBefore, HashSet<int> TakenPositions);
}
