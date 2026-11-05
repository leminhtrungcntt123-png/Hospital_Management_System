using System.Globalization;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.BLL.Options;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HIS.BLL.Services;

public class BookingService : IBookingService
{
    private readonly AppDbContext _context;
    private readonly BookingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly QueuePositionAllocator _allocator;

    public BookingService(
        AppDbContext context,
        QueuePositionAllocator allocator,
        IOptions<BookingOptions> options,
        TimeProvider timeProvider)
    {
        _context = context;
        _allocator = allocator;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    // Cùng nguồn giờ với updated_at do AppDbContext ghi để hạn mức hủy theo tháng dương lịch tính đúng.
    private DateTime Now => _timeProvider.GetLocalNow().DateTime;

    // Màn đặt lịch luôn báo số chỗ của 7 ngày, bắt đầu từ ngày mai.
    private const int DaysInWeek = 7;

    public async Task<List<BookingDepartmentDto>> GetActiveDepartmentsAsync()
    {
        var departments = await _context.Departments
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .ToListAsync();

        if (departments.Count == 0) return new List<BookingDepartmentDto>();

        var fromDate = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        DateTime fromDateTime = fromDate.ToDateTime(TimeOnly.MinValue);
        DateTime afterLastDate = fromDate.AddDays(DaysInWeek).ToDateTime(TimeOnly.MinValue);

        // Một lượt tải khung giờ cho cả tuần, thay vì đi từ lịch trực rồi COUNT lượt hẹn.
        var frames = await _context.AppointmentSlots
            .AsNoTracking()
            .Where(s => s.StartTime >= fromDateTime && s.StartTime < afterLastDate
                        && s.Status != SlotStatus.Blocked)
            .Select(s => new { s.StartTime, s.OnlineQuota, s.OnlineBooked, DepartmentId = s.Doctor.DepartmentId })
            .ToListAsync();

        var deptSlots = frames
            .Where(f => DateOnly.FromDateTime(f.StartTime).DayOfWeek != DayOfWeek.Sunday)
            .GroupBy(f => f.DepartmentId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(f => Math.Max(0, f.OnlineQuota - f.OnlineBooked)));

        return departments.Select(d => new BookingDepartmentDto
        {
            Id = d.Id,
            Name = d.Name,
            Description = d.Description,
            SlotsThisWeek = deptSlots.GetValueOrDefault(d.Id, 0)
        }).ToList();
    }

    public async Task<BookingDepartmentDto?> GetDepartmentByIdAsync(int id)
    {
        var d = await _context.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (d is null) return null;

        return new BookingDepartmentDto
        {
            Id = d.Id,
            Name = d.Name,
            Description = d.Description,
            SlotsThisWeek = await CountSlotsThisWeekAsync(d.Id)
        };
    }

    // Tính tổng số suất đặt online còn trống trong 7 ngày tới theo chuyên khoa
    private async Task<int> CountSlotsThisWeekAsync(int departmentId)
    {
        var fromDate = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        var frames = await LoadFramesAsync(departmentId, fromDate, DaysInWeek);

        return frames.Sum(f => f.OnlineLeft);
    }

    /// <summary>
    /// Khung giờ của một chuyên khoa trong vài ngày tới, kèm số suất A còn lại.
    /// Chỗ trống đọc thẳng từ appointment_slots (online_quota − online_booked) vì đó là con số
    /// QueuePositionAllocator giữ khi cấp/thu hồi chỗ; đếm lượt hẹn sẽ tính lệch suất T và suất đã giải phóng.
    /// Khung đã qua giờ bắt đầu và chủ nhật bị loại vì web không đặt được.
    /// </summary>
    private async Task<List<BookingFrame>> LoadFramesAsync(int departmentId, DateOnly fromDate, int dayCount)
    {
        var fromDateTime = fromDate.ToDateTime(TimeOnly.MinValue);
        var afterLastDate = fromDate.AddDays(dayCount).ToDateTime(TimeOnly.MinValue);
        DateTime now = Now;

        var rows = await _context.AppointmentSlots
            .AsNoTracking()
            .Where(s => s.StartTime >= fromDateTime
                        && s.StartTime < afterLastDate
                        && s.Status != SlotStatus.Blocked
                        && s.Doctor.DepartmentId == departmentId)
            .OrderBy(s => s.StartTime)
            .ThenBy(s => s.Id)
            .Select(s => new
            {
                s.Id,
                s.DoctorId,
                DoctorName = s.Doctor.FullName,
                DoctorSpecialty = s.Doctor.Specialty,
                RoomName = s.Room.RoomName,
                s.StartTime,
                s.EndTime,
                s.Capacity,
                s.OnlineQuota,
                s.OnlineBooked
            })
            .ToListAsync();

        return rows
            .Where(r => DateOnly.FromDateTime(r.StartTime).DayOfWeek != DayOfWeek.Sunday
                        && r.StartTime > now
                        && r.OnlineQuota > 0)
            .Select(r => new BookingFrame(
                r.Id,
                r.DoctorId,
                r.DoctorName,
                r.DoctorSpecialty,
                r.RoomName,
                r.StartTime,
                r.EndTime,
                r.Capacity,
                r.OnlineQuota,
                Math.Max(0, r.OnlineQuota - r.OnlineBooked)))
            .ToList();
    }

    // Lấy danh sách ca khám và số chỗ trống theo chuyên khoa và ngày
    public async Task<List<BookingShiftDto>> GetShiftsAsync(int departmentId, DateOnly date)
    {
        var frames = await LoadFramesAsync(departmentId, date, 1);

        var grouped = frames
            .GroupBy(f => (f.DoctorId, Shift: ExamShifts.For(f.StartTime)))
            .Select(g => new
            {
                g.Key.Shift,
                g.Key.DoctorId,
                First = g.First(),
                StartTime = g.Min(f => f.StartTime),
                EndTime = g.Max(f => f.EndTime),
                SlotsLeft = g.Sum(f => f.OnlineLeft)
            })
            .OrderBy(x => x.StartTime)
            .ThenBy(x => x.DoctorId)
            .ToList();

        return grouped.Select(x => new BookingShiftDto
        {
            Key = x.Shift.ToDatabaseCode(),
            Label = x.Shift.ToLabel(),
            TimeRange = $"{x.StartTime:HH\\:mm} – {x.EndTime:HH\\:mm}",
            SlotsLeft = x.SlotsLeft,
            Status = x.SlotsLeft > 0 ? "AVAILABLE" : "FULL",
            DoctorId = x.DoctorId,
            DoctorName = x.First.DoctorName,
            DoctorTitle = x.First.DoctorSpecialty,
            RoomName = x.First.RoomName
        }).ToList();
    }

    // Lấy danh sách khung giờ còn suất đặt online trong một ca của chuyên khoa
    public async Task<List<BookingSlotDto>> GetSlotsAsync(
        int departmentId,
        DateOnly date,
        string shiftKey,
        CancellationToken cancellationToken = default)
    {
        ExamShift? wantedShift = EnumCodes.ParseDatabaseCode<ExamShift>(shiftKey);
        if (wantedShift is null)
        {
            return new List<BookingSlotDto>();
        }

        var frames = await LoadFramesAsync(departmentId, date, 1);

        return frames
            .Where(f => ExamShifts.For(f.StartTime) == wantedShift.Value)
            .Select(f => new BookingSlotDto
            {
                Id = f.SlotId,
                Label = $"{f.StartTime:HH\\:mm} – {f.EndTime:HH\\:mm}",
                ShiftLabel = wantedShift.Value.ToLabel(),
                StartTime = f.StartTime,
                EndTime = f.EndTime,
                Capacity = f.Capacity,
                OnlineQuota = f.OnlineQuota,
                SlotsLeft = f.OnlineLeft,
                Status = f.OnlineLeft > 0 ? "AVAILABLE" : "FULL",
                DoctorId = f.DoctorId,
                DoctorName = f.DoctorName,
                DoctorTitle = f.DoctorSpecialty,
                RoomName = f.RoomName
            })
            .ToList();
    }

    // Lấy trạng thái khả dụng của các ngày trong tuần
    public async Task<List<BookingDayDto>> GetDaysAsync(int departmentId, DateOnly from, int count)
    {
        var days = new List<BookingDayDto>();

        for (int i = 0; i < count; i++)
        {
            var date = from.AddDays(i);
            var shifts = await GetShiftsAsync(departmentId, date);

            int totalSlots = shifts.Sum(s => s.SlotsLeft);

            string state = shifts.Count == 0 ? "off"
                         : totalSlots == 0 ? "full"
                         : totalSlots <= 6 ? "few"
                         : "available";

            days.Add(new BookingDayDto { Date = date, State = state });
        }

        return days;
    }

    // Lấy chi phí khám bệnh của chuyên khoa
    public async Task<decimal> GetExaminationFeeAsync(int departmentId)
    {
        var service = await _context.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.IsActive && s.ServiceTypeId == (int)ServiceTypeId.Examination);

        return service?.Price ?? 300000m;
    }

    /// <summary>
    /// Tạo lịch hẹn web và giữ một suất A của khung giờ bệnh nhân đã chọn.
    /// Khung giờ là nguồn chân lý về chỗ trống nên mọi kiểm tra (còn mở, còn suất A, trùng ngày khám)
    /// đều làm ở đây thay vì để UI tự tính. Cấp số qua QueuePositionAllocator để số A và booked_count
    /// tăng trong cùng một lần ghi, và uq_slot_position từ chối lượt đặt trúng tranh chấp.
    /// </summary>
    public async Task<Appointment> CreateAppointmentAsync(
        int accountId,
        int patientId,
        int slotId,
        CancellationToken cancellationToken = default)
    {
        var status = await _context.AppointmentStatuses
            .FirstAsync(s => s.Code == AppointmentStatus.Codes.Confirmed, cancellationToken);

        Patient patient = await _context.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == patientId, cancellationToken)
            ?? throw new ArgumentException("Không tìm thấy hồ sơ bệnh nhân đã chọn.");

        Account account = await _context.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            ?? throw new ArgumentException("Tài khoản đặt lịch không tồn tại.");

        DateTime now = Now;

        // Xét chặn trước khi đọc khung giờ: tài khoản bị chặn thì thông báo phải là lý do chặn.
        if (account.BookingBlockedUntil is DateTime blockedUntil && blockedUntil > now)
        {
            throw new InvalidOperationException(
                $"Tài khoản đang bị chặn đặt lịch mới đến hết ngày {blockedUntil:dd/MM/yyyy} " +
                $"do đã đủ {_options.MaxNoShowsBeforeBlock} lần vắng mặt. Đăng nhập và xem lịch sử vẫn bình thường.");
        }

        AppointmentSlot slot = await _context.AppointmentSlots
            .AsNoTracking()
            .Include(s => s.Doctor)
            .FirstOrDefaultAsync(s => s.Id == slotId, cancellationToken)
            ?? throw new ArgumentException("Không tìm thấy khung giờ đã chọn.");

        if (slot.Status == SlotStatus.Blocked)
        {
            throw new InvalidOperationException("Khung giờ này đã ngừng nhận bệnh nhân. Vui lòng chọn khung khác.");
        }

        if (slot.StartTime <= now)
        {
            throw new InvalidOperationException("Khung giờ vừa tới giờ khám nên không đặt được nữa. Vui lòng chọn khung khác.");
        }

        DateOnly date = DateOnly.FromDateTime(slot.StartTime);

        // Một người chỉ một lượt khám trong ngày, tính trên ngày của khung giờ chứ không phải ngày đặt.
        var dayStart = slot.StartTime.Date;
        bool alreadyBooked = await _context.Appointments
            .AsNoTracking()
            .AnyAsync(a => a.PatientId == patientId
                           && a.Slot != null
                           && a.Slot.StartTime >= dayStart
                           && a.Slot.StartTime < dayStart.AddDays(1)
                           && a.Status.Code != AppointmentStatus.Codes.Cancelled
                           && a.Status.Code != AppointmentStatus.Codes.NoShow
                           && a.Status.Code != AppointmentStatus.Codes.Expired,
                        cancellationToken);

        if (alreadyBooked)
        {
            throw new InvalidOperationException(
                $"{patient.FullName} đã có lịch khám trong ngày {date:dd/MM/yyyy}. " +
                "Vui lòng chọn ngày khác hoặc xem lại lịch hẹn.");
        }

        int priorityLevel = PatientAge.NeedsAgePriority(
            patient.DateOfBirth,
            DateOnly.FromDateTime(now),
            _options.PriorityChildUnderAge,
            _options.PrioritySeniorFromAge)
            ? PriorityLevels.AgePriority
            : PriorityLevels.Normal;

        var appointment = new Appointment
        {
            PatientId = patientId,
            StatusId = status.Id,
            BookingChannel = BookingChannel.Web,
            VisitType = VisitType.Scheduled,
            AppointmentCode = AppointmentCode.Generate(date),
            PriorityLevel = priorityLevel,
            BookedBy = accountId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            _context.Appointments.Add(appointment);

            QueuePlacement? placement = await _allocator.AssignOnlineAsync(appointment, slotId, cancellationToken);

            if (placement is null)
            {
                throw new InvalidOperationException("Khung giờ này vừa hết suất đặt online. Vui lòng chọn khung khác.");
            }

            // Bác sĩ, phòng và chuyên khoa lấy từ khung giờ thật sự giữ chỗ để màn danh sách không trống cột.
            appointment.RoomId = placement.Slot.RoomId;
            appointment.DoctorId = placement.Slot.DoctorId;
            appointment.DepartmentId = slot.Doctor.DepartmentId;

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException race)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(
                "Khung giờ vừa có người đặt trước nên chỗ đã kín. Vui lòng chọn khung khác.", race);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return appointment;
    }

    /// <summary>
    /// Bệnh nhân tự hủy lịch hẹn web trên tài khoản của mình. Hủy trước FreeCancelHours giờ thì không
    /// tính hạn mức; hủy muộn ghi is_late_cancel vào lịch sử và mỗi tháng dương lịch chỉ được
    /// MaxLateCancelsPerMonth lần. Suất đang giữ trong khung giờ được trả lại ngay.
    /// </summary>
    public async Task<CancelAppointmentResult> CancelAppointmentAsync(
        int appointmentId,
        int accountId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await _context.Appointments
            .Include(a => a.Slot)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

        if (appointment is null || appointment.BookingChannel != BookingChannel.Web || appointment.BookedBy != accountId)
        {
            return new CancelAppointmentResult(false, "Không tìm thấy lịch hẹn của bạn.");
        }

        int cancelledStatusId = (await _context.AppointmentStatuses
            .AsNoTracking()
            .FirstAsync(s => s.Code == AppointmentStatus.Codes.Cancelled, cancellationToken)).Id;

        var cancellableIds = await _context.AppointmentStatuses
            .AsNoTracking()
            .Where(s => s.Code == AppointmentStatus.Codes.Pending || s.Code == AppointmentStatus.Codes.Confirmed)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (!cancellableIds.Contains(appointment.StatusId))
        {
            return new CancelAppointmentResult(
                false,
                $"Lượt {appointment.AppointmentCode} không còn ở trạng thái có thể hủy.");
        }

        DateTime now = Now;

        // Chưa gắn khung giờ thì không có mốc để xét muộn, nên tính là hủy sớm và không trừ hạn mức.
        bool isLate = appointment.Slot is { } slot
                      && slot.StartTime - now < TimeSpan.FromHours(_options.FreeCancelHours);

        int lateCancelsThisMonth = 0;
        if (isLate)
        {
            DateTime monthStart = new DateTime(now.Year, now.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            lateCancelsThisMonth = await _context.AppointmentStatusHistories
                .AsNoTracking()
                .CountAsync(h => h.PerformedBy == accountId
                                && h.IsLateCancel
                                && h.NewStatusId == cancelledStatusId
                                && h.ChangedAt >= monthStart
                                && h.ChangedAt < nextMonthStart, cancellationToken);

            if (lateCancelsThisMonth >= _options.MaxLateCancelsPerMonth)
            {
                return new CancelAppointmentResult(
                    false,
                    $"Tháng này bạn đã dùng hết {_options.MaxLateCancelsPerMonth} lần hủy muộn (trong vòng " +
                    $"{_options.FreeCancelHours} giờ trước giờ khám). Vui lòng đến quầy để hủy hoặc chọn ngày khám khác.",
                    IsLateCancel: true,
                    LateCancelsUsedThisMonth: lateCancelsThisMonth);
            }
        }

        int oldStatusId = appointment.StatusId;
        appointment.StatusId = cancelledStatusId;
        appointment.UpdatedAt = now;

        QueuePositionAllocator.ReleaseSeat(appointment);

        _context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            OldStatusId = oldStatusId,
            NewStatusId = cancelledStatusId,
            PerformedBy = accountId,
            ChangedAt = now,
            IsLateCancel = isLate
        });

        await _context.SaveChangesAsync(cancellationToken);

        int used = lateCancelsThisMonth + (isLate ? 1 : 0);
        string message = isLate
            ? $"Đã hủy lượt {appointment.AppointmentCode}. Đây là lần hủy muộn thứ {used}/{_options.MaxLateCancelsPerMonth} trong tháng."
            : $"Đã hủy lượt {appointment.AppointmentCode}. Hủy trước giờ khám {_options.FreeCancelHours} giờ nên không tính vào hạn mức tháng.";

        return new CancelAppointmentResult(true, message, isLate, used);
    }

    /// <summary>
    /// Lịch hẹn web do chính tài khoản này đặt, cho màn "Lịch khám của tôi".
    /// Hủy muộn hay bị chặn đều tính trên booked_by nên danh sách phải lọc theo cùng cột đó.
    /// </summary>
    public async Task<List<BookingAppointmentDto>> GetAppointmentsByAccountAsync(
        int accountId,
        CancellationToken cancellationToken = default)
    {
        DateTime now = Now;

        var rows = await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Status)
            .Include(a => a.Department)
            .Include(a => a.Slot).ThenInclude(s => s!.Room)
            .Include(a => a.Slot).ThenInclude(s => s!.Doctor)
            .Where(a => a.BookedBy == accountId && a.BookingChannel == BookingChannel.Web)
            .OrderByDescending(a => a.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        return rows.Select(a => new BookingAppointmentDto
        {
            Id = a.Id,
            Code = a.AppointmentCode,
            Status = a.Status.Code,
            StatusLabel = a.Status.Name,
            PatientId = a.PatientId,
            PatientName = a.Patient.FullName,
            DepartmentName = a.Department?.Name ?? string.Empty,
            DoctorName = a.Slot?.Doctor?.FullName ?? string.Empty,
            RoomName = a.Slot?.Room?.RoomName ?? string.Empty,
            QueueNumber = a.QueueNumber ?? string.Empty,
            SlotStartTime = a.Slot?.StartTime,
            SlotEndTime = a.Slot?.EndTime,
            IsCancellable = (a.Status.Code == AppointmentStatus.Codes.Pending
                             || a.Status.Code == AppointmentStatus.Codes.Confirmed)
                            && (a.Slot is null || a.Slot.StartTime > now)
        }).ToList();
    }

    // Một khung giờ kèm số suất A còn lại, dùng chung cho màn ca khám, màn khung giờ và đếm chỗ trong tuần.
    private sealed record BookingFrame(
        int SlotId,
        int DoctorId,
        string DoctorName,
        string? DoctorSpecialty,
        string? RoomName,
        DateTime StartTime,
        DateTime EndTime,
        int Capacity,
        int OnlineQuota,
        int OnlineLeft);
}