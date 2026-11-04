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

    public BookingService(AppDbContext context, IOptions<BookingOptions> options, TimeProvider timeProvider)
    {
        _context = context;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    // Cùng nguồn giờ với updated_at do AppDbContext ghi để hạn mức hủy theo tháng dương lịch tính đúng.
    private DateTime Now => _timeProvider.GetLocalNow().DateTime;

    public async Task<List<BookingDepartmentDto>> GetActiveDepartmentsAsync()
    {
        var departments = await _context.Departments
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .ToListAsync();

        if (departments.Count == 0) return new List<BookingDepartmentDto>();

        var fromDate = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        var toDate = fromDate.AddDays(6);
        var fromDateTime = fromDate.ToDateTime(TimeOnly.MinValue);
        var toDateTime = toDate.ToDateTime(TimeOnly.MaxValue);

        // Lấy lịch làm việc của bác sĩ trong 7 ngày tới
        var schedules = await _context.DoctorSchedules
            .AsNoTracking()
            .Include(s => s.Doctor)
            .Where(s => s.IsActive
                        && s.EffectiveFrom <= toDate
                        && (s.EffectiveTo == null || s.EffectiveTo >= fromDate))
            .ToListAsync();

        // Thống kê số lượng lịch hẹn đã đặt trong tuần theo bác sĩ và ngày
        var bookedList = await _context.Appointments
            .AsNoTracking()
            .Where(a => a.Slot != null
                        && a.Slot.StartTime >= fromDateTime
                        && a.Slot.StartTime <= toDateTime
                        && a.Status.Code != AppointmentStatus.Codes.Cancelled
                        && a.Status.Code != AppointmentStatus.Codes.NoShow
                        && a.Status.Code != AppointmentStatus.Codes.Expired)
            .GroupBy(a => new { a.Slot!.DoctorId, Date = DateOnly.FromDateTime(a.Slot.StartTime) })
            .Select(g => new { g.Key.DoctorId, g.Key.Date, Count = g.Count() })
            .ToListAsync();

        var bookedMap = bookedList.ToDictionary(x => (x.DoctorId, x.Date), x => x.Count);

        var deptSlots = departments.ToDictionary(d => d.Id, _ => 0);

        for (int i = 0; i < 7; i++)
        {
            var curDate = fromDate.AddDays(i);
            if (curDate.DayOfWeek == DayOfWeek.Sunday) continue;

            var daySchedules = schedules.Where(s => s.DayOfWeek == curDate.DayOfWeek
                                                 && s.EffectiveFrom <= curDate
                                                 && (s.EffectiveTo == null || s.EffectiveTo >= curDate));

            foreach (var schedule in daySchedules)
            {
                if (schedule.Doctor == null) continue;

                int booked = bookedMap.GetValueOrDefault((schedule.DoctorId, curDate), 0);
                int totalMinutes = (int)(schedule.EndTime - schedule.StartTime).TotalMinutes;
                int maxCapacity = schedule.SlotDurationMinutes > 0 ? totalMinutes / schedule.SlotDurationMinutes : 15;
                int slotsLeft = Math.Max(0, maxCapacity - booked);

                if (deptSlots.ContainsKey(schedule.Doctor.DepartmentId))
                {
                    deptSlots[schedule.Doctor.DepartmentId] += slotsLeft;
                }
            }
        }

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

    // Tính tổng số chỗ còn trống trong 7 ngày tới theo chuyên khoa
    private async Task<int> CountSlotsThisWeekAsync(int departmentId)
    {
        var from = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        int total = 0;
        for (int i = 0; i < 7; i++)
        {
            var shifts = await GetShiftsAsync(departmentId, from.AddDays(i));
            total += shifts.Sum(s => s.SlotsLeft);
        }
        return total;
    }

    // Lấy danh sách ca khám và số chỗ trống theo chuyên khoa và ngày
    public async Task<List<BookingShiftDto>> GetShiftsAsync(int departmentId, DateOnly date)
    {
        if (date.DayOfWeek == DayOfWeek.Sunday)
            return new List<BookingShiftDto>();

        var schedules = await _context.DoctorSchedules
            .AsNoTracking()
            .Include(s => s.Doctor)
            .Include(s => s.Room)
            .Where(s => s.Doctor.DepartmentId == departmentId
                        && s.IsActive
                        && s.EffectiveFrom <= date
                        && (s.EffectiveTo == null || s.EffectiveTo >= date)
                        && s.DayOfWeek == date.DayOfWeek)
            .ToListAsync();

        if (!schedules.Any())
            return new List<BookingShiftDto>();

        var startDateTime = date.ToDateTime(TimeOnly.MinValue);
        var endDateTime = date.ToDateTime(TimeOnly.MaxValue);
        var doctorIds = schedules.Select(s => s.DoctorId).Distinct().ToList();

        // Đếm số lịch hẹn đã đặt theo từng bác sĩ trong ngày
        var bookedCounts = await _context.Appointments
            .AsNoTracking()
            .Where(a => a.Slot != null
                        && doctorIds.Contains(a.Slot.DoctorId)
                        && a.Slot.StartTime >= startDateTime
                        && a.Slot.StartTime <= endDateTime
                        && a.Status.Code != AppointmentStatus.Codes.Cancelled
                        && a.Status.Code != AppointmentStatus.Codes.NoShow
                        && a.Status.Code != AppointmentStatus.Codes.Expired)
            .GroupBy(a => a.Slot!.DoctorId)
            .Select(g => new { DoctorId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.DoctorId, g => g.Count);

        var result = new List<BookingShiftDto>();

        foreach (var schedule in schedules)
        {
            int bookedCount = bookedCounts.GetValueOrDefault(schedule.DoctorId, 0);

            // Tính số lượng chỗ khám tối đa của ca làm việc
            int totalMinutes = (int)(schedule.EndTime - schedule.StartTime).TotalMinutes;
            int maxCapacity = schedule.SlotDurationMinutes > 0 ? totalMinutes / schedule.SlotDurationMinutes : 15;

            int slotsLeft = maxCapacity - bookedCount;
            if (slotsLeft < 0) slotsLeft = 0;

            result.Add(new BookingShiftDto
            {
                Key = schedule.Shift.ToDatabaseCode(),
                Label = schedule.Shift.ToLabel(),
                TimeRange = $"{schedule.StartTime:hh\\:mm} – {schedule.EndTime:hh\\:mm}",
                SlotsLeft = slotsLeft,
                Status = slotsLeft > 0 ? "AVAILABLE" : "FULL",
                DoctorId = schedule.DoctorId,
                DoctorName = schedule.Doctor?.FullName,
                DoctorTitle = schedule.Doctor?.Specialty,
                RoomName = schedule.Room?.RoomName
            });
        }

        return result;
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

    public async Task<IEnumerable<Patient>> GetPatientsByAccountIdAsync(int accountId)
    {
        return await _context.AccountPatientProfiles
            .AsNoTracking()
            .Where(l => l.AccountId == accountId && l.Status != LinkStatus.Revoked)
            .Select(l => l.Patient)
            .ToListAsync();
    }

    // Tạo hồ sơ mới cho chính tài khoản: Patient + link SELF (ACTIVE, được xem hồ sơ) trong một transaction
    public async Task<Patient> CreatePatientAsync(int accountId, string fullName, DateOnly dateOfBirth, string? gender, string phone, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var patient = new Patient
        {
            FullName = fullName,
            DateOfBirth = dateOfBirth,
            Gender = EnumCodes.ParseDatabaseCode<Gender>(gender),
            PhoneNumber = phone,
            CreatedAt = now
        };

        _context.Patients.Add(patient);
        await _context.SaveChangesAsync(cancellationToken);

        _context.AccountPatientProfiles.Add(new AccountPatientProfile
        {
            AccountId = accountId,
            PatientId = patient.Id,
            Relationship = PatientRelationship.Self,
            Status = LinkStatus.Active,
            CanViewRecords = true,
            CreatedBy = accountId,
            VerifiedBy = accountId,
            VerifiedAt = now,
            CreatedAt = now
        });
        await _context.SaveChangesAsync(cancellationToken);

        return patient;
    }

    // Lấy chi phí khám bệnh của chuyên khoa
    public async Task<decimal> GetExaminationFeeAsync(int departmentId)
    {
        var service = await _context.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.IsActive && s.ServiceTypeId == (int)ServiceTypeId.Examination);

        return service?.Price ?? 300000m;
    }

    // Tạo mới lịch hẹn khám
    public async Task<Appointment> CreateAppointmentAsync(
        int accountId,
        int patientId,
        int departmentId,
        DateOnly date,
        string shiftKey,
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

        if (account.BookingBlockedUntil is DateTime blockedUntil && blockedUntil > now)
        {
            throw new InvalidOperationException(
                $"Tài khoản đang bị chặn đặt lịch mới đến hết ngày {blockedUntil:dd/MM/yyyy} " +
                $"do đã đủ {_options.MaxNoShowsBeforeBlock} lần vắng mặt. Đăng nhập và xem lịch sử vẫn bình thường.");
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

        _context.Appointments.Add(appointment);
        await _context.SaveChangesAsync(cancellationToken);
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
}