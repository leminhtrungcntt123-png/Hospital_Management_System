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

    // Màn đặt lịch luôn báo số chỗ của cả cửa sổ đặt được (MaxAdvanceDays ngày), bắt đầu từ ngày đặt được sớm nhất.

    /// <summary>Ngày khám sớm nhất tài khoản web còn đặt được: thường là ngày mai, nhưng sau cutoff thì nhảy sang ngày kia.</summary>
    public DateOnly FirstBookableDate
    {
        get
        {
            DateOnly tomorrow = DateOnly.FromDateTime(Now).AddDays(1);
            return IsBookableDate(tomorrow) ? tomorrow : tomorrow.AddDays(1);
        }
    }

    /// <summary>Ngày khám xa nhất còn đặt được (hôm nay + MaxAdvanceDays).</summary>
    public DateOnly LastBookableDate => DateOnly.FromDateTime(Now).AddDays(_options.MaxAdvanceDays);

    /// <summary>Số ngày nguyên nằm trong cửa sổ đặt được, dùng để vẽ lịch.</summary>
    public int BookableDayCount => LastBookableDate.DayNumber - FirstBookableDate.DayNumber + 1;

    /// <summary>Ngày D chỉ đặt được khi chưa quá cutoff của ngày liền trước D và còn trong MaxAdvanceDays.</summary>
    public bool IsBookableDate(DateOnly date) => date <= LastBookableDate && Now < BookingDeadline(date);

    // Chủ nhật bệnh viện không nhận khám qua web; khung đã qua giờ bắt đầu hoặc thuộc ngày đã đóng cutoff thì loại.
    private bool IsFrameBookable(DateTime startTime) =>
        DateOnly.FromDateTime(startTime).DayOfWeek != DayOfWeek.Sunday
        && startTime > Now
        && IsBookableDate(DateOnly.FromDateTime(startTime));

    public async Task<List<BookingDepartmentDto>> GetActiveDepartmentsAsync()
    {
        var departments = await _context.Departments
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .ToListAsync();

        if (departments.Count == 0) return new List<BookingDepartmentDto>();

        DateOnly fromDate = FirstBookableDate;
        DateTime fromDateTime = fromDate.ToDateTime(TimeOnly.MinValue);
        DateTime afterLastDate = LastBookableDate.ToDateTime(TimeOnly.MaxValue);

        // Một lượt tải khung giờ cho cả cửa sổ đặt được, thay vì đi từ lịch trực rồi COUNT lượt hẹn.
        var frames = await _context.AppointmentSlots
            .AsNoTracking()
            .Where(s => s.StartTime >= fromDateTime && s.StartTime <= afterLastDate
                        && s.Status != SlotStatus.Blocked)
            .Select(s => new { s.StartTime, s.OnlineQuota, s.OnlineBooked, DepartmentId = s.Doctor.DepartmentId })
            .ToListAsync();

        var deptSlots = frames
            .Where(f => IsFrameBookable(f.StartTime))
            .GroupBy(f => f.DepartmentId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(f => Math.Max(0, f.OnlineQuota - f.OnlineBooked)));

        return departments.Select(d => new BookingDepartmentDto
        {
            Id = d.Id,
            Name = d.Name,
            Description = d.Description,
            SlotsAvailable = deptSlots.GetValueOrDefault(d.Id, 0)
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
            SlotsAvailable = await CountBookableSlotsAsync(d.Id)
        };
    }

    /// <summary>
    /// Màn "chọn chuyên khoa" của luồng theo ngày: chỉ khoa nào còn mở khung đặt online
    /// trong đúng ngày đã chọn mới có SlotsAvailable lớn hơn 0.
    /// </summary>
    public async Task<List<BookingDepartmentDto>> GetDepartmentsOnDateAsync(
        DateOnly date, CancellationToken cancellationToken = default)
    {
        var departments = await _context.Departments
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        if (departments.Count == 0) return new List<BookingDepartmentDto>();

        var frames = await LoadFramesAsync(date, 1, cancellationToken: cancellationToken);

        var slotsOnDate = frames
            .GroupBy(f => f.DepartmentId)
            .ToDictionary(g => g.Key, g => g.Sum(f => f.OnlineLeft));

        return departments.Select(d => new BookingDepartmentDto
        {
            Id = d.Id,
            Name = d.Name,
            Description = d.Description,
            SlotsAvailable = slotsOnDate.GetValueOrDefault(d.Id, 0)
        }).ToList();
    }

    // Tổng suất đặt online còn trống trong cả cửa sổ đặt được theo chuyên khoa
    private async Task<int> CountBookableSlotsAsync(int departmentId)
    {
        var frames = await LoadFramesAsync(FirstBookableDate, BookableDayCount, departmentId: departmentId);

        return frames.Sum(f => f.OnlineLeft);
    }

    /// <summary>
    /// Khung giờ khám trong vài ngày tới, kèm số suất A còn lại, lọc tùy chọn theo chuyên khoa
    /// và theo bác sĩ (bỏ trống cả hai = toàn viện, dùng cho lịch ngày của luồng "theo ngày").
    /// Chỗ trống đọc thẳng từ appointment_slots (online_quota − online_booked) vì đó là con số
    /// QueuePositionAllocator giữ khi cấp/thu hồi chỗ; đếm lượt hẹn sẽ tính lệch suất T và suất đã giải phóng.
    /// </summary>
    private async Task<List<BookingFrame>> LoadFramesAsync(
        DateOnly fromDate,
        int dayCount,
        int? departmentId = null,
        int? doctorId = null,
        CancellationToken cancellationToken = default)
    {
        var fromDateTime = fromDate.ToDateTime(TimeOnly.MinValue);
        var afterLastDate = fromDate.AddDays(dayCount).ToDateTime(TimeOnly.MinValue);

        var query = _context.AppointmentSlots
            .AsNoTracking()
            .Where(s => s.StartTime >= fromDateTime
                        && s.StartTime < afterLastDate
                        && s.Status != SlotStatus.Blocked);

        if (departmentId is int deptId) query = query.Where(s => s.Doctor.DepartmentId == deptId);
        if (doctorId is int docId) query = query.Where(s => s.DoctorId == docId);

        var rows = await query
            .OrderBy(s => s.StartTime)
            .ThenBy(s => s.Id)
            .Select(s => new
            {
                s.Id,
                s.DoctorId,
                DoctorName = s.Doctor.FullName,
                DoctorSpecialty = s.Doctor.Specialty,
                DepartmentId = s.Doctor.DepartmentId,
                RoomName = s.Room.RoomName,
                s.StartTime,
                s.EndTime,
                s.Capacity,
                s.OnlineQuota,
                s.OnlineBooked
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => r.OnlineQuota > 0 && IsFrameBookable(r.StartTime))
            .Select(r => new BookingFrame(
                r.Id,
                r.DoctorId,
                r.DoctorName,
                r.DoctorSpecialty,
                r.DepartmentId,
                r.RoomName,
                r.StartTime,
                r.EndTime,
                r.Capacity,
                r.OnlineQuota,
                Math.Max(0, r.OnlineQuota - r.OnlineBooked)))
            .ToList();
    }

    private static BookingSlotDto ToSlotDto(BookingFrame f) => new()
    {
        Id = f.SlotId,
        Label = $"{f.StartTime:HH\\:mm} – {f.EndTime:HH\\:mm}",
        ShiftLabel = ExamShifts.For(f.StartTime).ToLabel(),
        StartTime = f.StartTime,
        EndTime = f.EndTime,
        Capacity = f.Capacity,
        OnlineQuota = f.OnlineQuota,
        SlotsLeft = f.OnlineLeft,
        Status = f.OnlineLeft > 0 ? "AVAILABLE" : "FULL",
        DepartmentId = f.DepartmentId,
        DoctorId = f.DoctorId,
        DoctorName = f.DoctorName,
        DoctorTitle = f.DoctorSpecialty,
        RoomName = f.RoomName
    };

    /// <summary>
    /// Mọi khung giờ còn mở trong một ngày (kể cả khung đã kín, trả về với Status = "FULL" để UI ghi
    /// "Đã đầy"), lọc theo chuyên khoa và/hoặc bác sĩ. Màn hình tự nhóm theo Ca sáng / Ca chiều
    /// nên người bệnh không còn phải chọn ca riêng như một bước.
    /// </summary>
    public async Task<List<BookingSlotDto>> GetSlotsAsync(
        DateOnly date,
        int? departmentId = null,
        int? doctorId = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsBookableDate(date)) return new List<BookingSlotDto>();

        var frames = await LoadFramesAsync(date, 1, departmentId, doctorId, cancellationToken);

        return frames.Select(ToSlotDto).ToList();
    }

    /// <summary>
    /// Đối chiếu lại một khung giờ ngay trước khi lưu: null nếu không tồn tại, đã Block,
    /// hoặc rơi vào ngày đã quá cutoff/ngoài cửa sổ đặt được.
    /// </summary>
    public async Task<BookingSlotDto?> GetSlotByIdAsync(int slotId, CancellationToken cancellationToken = default)
    {
        var row = await _context.AppointmentSlots
            .AsNoTracking()
            .Where(s => s.Id == slotId && s.Status != SlotStatus.Blocked && s.OnlineQuota > 0)
            .Select(s => new
            {
                s.Id,
                s.DoctorId,
                DoctorName = s.Doctor.FullName,
                DoctorSpecialty = s.Doctor.Specialty,
                DepartmentId = s.Doctor.DepartmentId,
                RoomName = s.Room.RoomName,
                s.StartTime,
                s.EndTime,
                s.Capacity,
                s.OnlineQuota,
                s.OnlineBooked
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null || !IsFrameBookable(row.StartTime)) return null;

        return ToSlotDto(new BookingFrame(
            row.Id,
            row.DoctorId,
            row.DoctorName,
            row.DoctorSpecialty,
            row.DepartmentId,
            row.RoomName,
            row.StartTime,
            row.EndTime,
            row.Capacity,
            row.OnlineQuota,
            Math.Max(0, row.OnlineQuota - row.OnlineBooked)));
    }

    /// <summary>
    /// Lịch đặt được của toàn viện / một khoa / một bác sĩ. Đọc khung giờ của cả vùng ngày
    /// trong một lần truy vấn rồi nhóm theo ngày, thay vì hỏi từng ngày một.
    /// </summary>
    public async Task<List<BookingDayDto>> GetDaysAsync(
        DateOnly from,
        int count,
        int? departmentId = null,
        int? doctorId = null,
        CancellationToken cancellationToken = default)
    {
        var frames = await LoadFramesAsync(from, count, departmentId, doctorId, cancellationToken);

        var byDate = frames
            .GroupBy(f => DateOnly.FromDateTime(f.StartTime))
            .ToDictionary(g => g.Key, g => g.Sum(f => f.OnlineLeft));

        var days = new List<BookingDayDto>(count);
        for (int i = 0; i < count; i++)
        {
            var date = from.AddDays(i);

            if (!IsBookableDate(date))
            {
                // Ngày đã qua cutoff 15:00 hôm trước hoặc ngoài cửa sổ 30 ngày: web không nhận đặt.
                days.Add(new BookingDayDto { Date = date, State = "closed" });
                continue;
            }

            if (!byDate.TryGetValue(date, out int slotsLeft))
            {
                // Không có ca trực, hoặc bác sĩ nghỉ ngày đó.
                days.Add(new BookingDayDto { Date = date, State = "off" });
                continue;
            }

            days.Add(new BookingDayDto
            {
                Date = date,
                State = slotsLeft == 0 ? "full" : slotsLeft <= FewLeftThreshold ? "few" : "available"
            });
        }

        return days;
    }

    /// <summary>Ngày còn ít hơn mốc này thì màn lịch tô vàng "Sắp hết".</summary>
    private const int FewLeftThreshold = 6;

    /// <summary>
    /// Danh sách bác sĩ cho màn "chọn bác sĩ". Kèm các ngày trực đang hiệu lực và số suất
    /// đặt online còn lại (cả cửa sổ, hoặc riêng ngày được truyền vào để chặn bác sĩ nghỉ hôm đó).
    /// </summary>
    public async Task<List<BookingDoctorDto>> GetDoctorsAsync(
        int? departmentId = null,
        DateOnly? date = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Doctor> query = _context.Doctors.AsNoTracking();
        if (departmentId is int deptFilter) query = query.Where(d => d.DepartmentId == deptFilter);

        var doctors = await query
            .OrderBy(d => d.FullName)
            .Select(d => new
            {
                d.Id,
                d.FullName,
                d.Specialty,
                d.DepartmentId,
                DepartmentName = d.Department.Name
            })
            .ToListAsync(cancellationToken);

        if (doctors.Count == 0) return new List<BookingDoctorDto>();

        DateOnly from = date ?? FirstBookableDate;
        DateOnly to = date ?? LastBookableDate;

        var frames = await LoadFramesAsync(
            from, date is null ? BookableDayCount : 1, departmentId, cancellationToken: cancellationToken);

        var byDoctor = frames
            .GroupBy(f => f.DoctorId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    SlotsLeft = g.Sum(f => f.OnlineLeft),
                    NextAvailable = g.Where(f => f.OnlineLeft > 0).Min(f => (DateTime?)f.StartTime)
                });

        var dutyRows = await _context.DoctorSchedules
            .AsNoTracking()
            .Where(s => s.IsActive
                        && s.EffectiveFrom <= to
                        && (s.EffectiveTo == null || s.EffectiveTo >= from))
            .Select(s => new { s.DoctorId, s.DayOfWeek })
            .ToListAsync(cancellationToken);

        var dutyByDoctor = dutyRows
            .GroupBy(r => r.DoctorId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.DayOfWeek).Distinct().OrderBy(WeekdayOrder).ToList());

        var result = new List<BookingDoctorDto>();

        foreach (var d in doctors)
        {
            int slotsLeft = 0;
            DateOnly? nextAvailable = null;

            if (byDoctor.TryGetValue(d.Id, out var availability))
            {
                slotsLeft = availability.SlotsLeft;
                nextAvailable = availability.NextAvailable is { } next ? DateOnly.FromDateTime(next) : null;
            }
            // Ngày cụ thể (luồng "theo ngày"): chỉ hiện bác sĩ có ca trực hôm đó, kể cả khi đã kín suất.
            else if (date is not null) continue;

            result.Add(new BookingDoctorDto
            {
                Id = d.Id,
                FullName = d.FullName,
                Specialty = d.Specialty ?? string.Empty,
                DepartmentId = d.DepartmentId,
                DepartmentName = d.DepartmentName,
                DutyDays = FormatDutyDays(dutyByDoctor.GetValueOrDefault(d.Id)),
                SlotsAvailable = slotsLeft,
                NextAvailableDate = nextAvailable
            });
        }

        // Người còn lịch đặt hiện trước; đã chọn ngày thì xếp theo số suất còn để bệnh nhân dễ thấy.
        return date is null
            ? result.OrderByDescending(d => d.NextAvailableDate is null).ThenBy(d => d.NextAvailableDate).ToList()
            : result.OrderByDescending(d => d.SlotsAvailable).ThenBy(d => d.FullName).ToList();
    }

    /// <summary>
    /// Màn "Bác sĩ & khung giờ" của luồng theo ngày: mỗi bác sĩ của khoa trong ngày đã chọn
    /// kèm trọn danh sách khung giờ của người đó (khung kín vẫn về để UI ghi "Đã đầy").
    /// </summary>
    public async Task<List<BookingDoctorSlotsDto>> GetDoctorSlotsAsync(
        int departmentId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        if (!IsBookableDate(date)) return new List<BookingDoctorSlotsDto>();

        var frames = await LoadFramesAsync(date, 1, departmentId, cancellationToken: cancellationToken);

        return frames
            .GroupBy(f => f.DoctorId)
            .Select(g => new BookingDoctorSlotsDto
            {
                DoctorId = g.Key,
                DoctorName = g.First().DoctorName,
                Specialty = g.First().DoctorSpecialty ?? string.Empty,
                RoomName = g.First().RoomName ?? string.Empty,
                Slots = g.Select(ToSlotDto).ToList()
            })
            .OrderByDescending(d => d.SlotsLeft)
            .ThenBy(d => d.DoctorName)
            .ToList();
    }

    // Thứ tự hiển thị ngày trong tuần kiểu lịch bệnh viện: T2 … T7 rồi Chủ nhật.
    private static int WeekdayOrder(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;

    private static string FormatDutyDays(List<DayOfWeek>? days)
        => days is null || days.Count == 0
            ? "Chưa có lịch trực"
            : string.Join(" · ", days.Select(d => d == DayOfWeek.Sunday ? "CN" : $"T{(int)d + 1}"));

    // Lấy chi phí khám bệnh của chuyên khoa
    public async Task<decimal> GetExaminationFeeAsync(int departmentId)
    {
        var service = await _context.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.IsActive && s.ServiceTypeId == (int)ServiceTypeId.Examination);

        return service?.Price ?? 300000m;
    }

    /// <summary>
    /// Hạn chót đặt online cho ngày khám D: BookingCutoffHour của ngày liền trước D
    /// (mặc định 15:00 D-1). Cùng mốc này dùng cho cả lịch web lẫn màn chọn ngày.
    /// </summary>
    private DateTime BookingDeadline(DateOnly visitDate) =>
        visitDate.ToDateTime(TimeOnly.MinValue).AddDays(-1).AddHours(_options.BookingCutoffHour);

    private string DeadlineText(DateOnly visitDate) =>
        $"{_options.BookingCutoffHour:00}:00 ngày {BookingDeadline(visitDate):dd/MM}";

    /// <summary>
    /// Hạn mức một ngày của tài khoản (00:00-23:59): tối đa MaxBookingsPerDay lượt đăng ký và
    /// MaxRelativesPerDay hồ sơ khác nhau. Chỉ tính lượt còn hiệu lực nên hủy lượt nào là giải phóng
    /// ngay chỗ đó cho lượt đặt lại trong cùng ngày.
    /// </summary>
    private async Task EnsureDailyLimitsAsync(
        int accountId, int patientId, DateTime now, CancellationToken cancellationToken)
    {
        DateTime dayStart = now.Date;
        DateTime dayEnd = dayStart.AddDays(1);

        var bookedPatientIds = await _context.Appointments
            .AsNoTracking()
            .Where(a => a.BookedBy == accountId
                        && a.CreatedAt >= dayStart
                        && a.CreatedAt < dayEnd
                        && a.Status.Code != AppointmentStatus.Codes.Cancelled)
            .Select(a => a.PatientId)
            .ToListAsync(cancellationToken);

        if (bookedPatientIds.Count >= _options.MaxBookingsPerDay)
        {
            throw new InvalidOperationException(
                $"Hôm nay tài khoản đã dùng hết {_options.MaxBookingsPerDay} lượt đăng ký. " +
                "Vui lòng thử lại từ 00:00 ngày mai.");
        }

        bool patientAlreadyToday = bookedPatientIds.Contains(patientId);
        if (!patientAlreadyToday
            && bookedPatientIds.Distinct().Count() >= _options.MaxRelativesPerDay)
        {
            throw new InvalidOperationException(
                $"Mỗi tài khoản chỉ đăng ký được tối đa {_options.MaxRelativesPerDay} người thân trong một ngày. " +
                $"Hôm nay đã đăng ký cho {bookedPatientIds.Distinct().Count()} người.");
        }
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
        bool hasHealthInsurance = false,
        bool hasPrivateInsurance = false,
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

        if (date > DateOnly.FromDateTime(now).AddDays(_options.MaxAdvanceDays))
        {
            throw new InvalidOperationException(
                $"Chỉ được đặt khám trước tối đa {_options.MaxAdvanceDays} ngày. " +
                $"Ngày {date:dd/MM/yyyy} nằm ngoài khoảng này.");
        }

        if (now >= BookingDeadline(date))
        {
            throw new InvalidOperationException(
                $"Lượt khám ngày {date:dd/MM/yyyy} chỉ đặt được trước {DeadlineText(date)}. " +
                "Vui lòng chọn ngày khám xa hơn.");
        }

        await EnsureDailyLimitsAsync(accountId, patientId, now, cancellationToken);

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
            HasHealthInsurance = hasHealthInsurance,
            HasPrivateInsurance = hasPrivateInsurance,
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
    /// Bệnh nhân tự hủy lịch hẹn web trên tài khoản của mình, chỉ đến 23:59 ngày liền trước ngày khám.
    /// Qua mốc đó phải ra quầy; không còn phạt hủy trễ vì hủy là hoàn lại lượt trong ngày.
    /// Suất đang giữ trong khung giờ được trả lại ngay.
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

        if (appointment.Slot is { } slot && !IsCancelAllowed(slot.StartTime, now))
        {
            return new CancelAppointmentResult(
                false,
                $"Lượt {appointment.AppointmentCode} khám ngày {DateOnly.FromDateTime(slot.StartTime):dd/MM/yyyy} " +
                "đã quá hạn hủy trên web (chỉ hủy đến 23:59 ngày hôm trước). Vui lòng đến quầy để bỏ lượt.");
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
            ChangedAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);

        return new CancelAppointmentResult(
            true,
            $"Đã hủy lượt {appointment.AppointmentCode}. Lượt này được hoàn lại nên bạn vẫn còn chỉ tiêu đăng ký trong hôm nay.");
    }

    /// <summary>Hủy chỉ được đến hết 23:59 ngày liền trước ngày khám, tức là trước 00:00 ngày khám.</summary>
    private static bool IsCancelAllowed(DateTime slotStartTime, DateTime now) =>
        now < DateOnly.FromDateTime(slotStartTime).ToDateTime(TimeOnly.MinValue);

    /// <summary>
    /// Lịch hẹn web do chính tài khoản này đặt, cho màn "Lịch khám của tôi".
    /// Nút hủy chỉ hiện khi còn trong hạn hủy (trước 00:00 ngày khám) nên cột này tính bằng
    /// IsCancelAllowed, không phải "chưa tới giờ khám".
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
            HasHealthInsurance = a.HasHealthInsurance,
            HasPrivateInsurance = a.HasPrivateInsurance,
            SlotStartTime = a.Slot?.StartTime,
            SlotEndTime = a.Slot?.EndTime,
            IsCancellable = (a.Status.Code == AppointmentStatus.Codes.Pending
                             || a.Status.Code == AppointmentStatus.Codes.Confirmed)
                            && (a.Slot is null || IsCancelAllowed(a.Slot.StartTime, now))
        }).ToList();
    }

    // Một khung giờ kèm số suất A còn lại, dùng chung cho màn chọn ngày, màn khung giờ và đếm chỗ.
    private sealed record BookingFrame(
        int SlotId,
        int DoctorId,
        string DoctorName,
        string? DoctorSpecialty,
        int DepartmentId,
        string? RoomName,
        DateTime StartTime,
        DateTime EndTime,
        int Capacity,
        int OnlineQuota,
        int OnlineLeft);
}