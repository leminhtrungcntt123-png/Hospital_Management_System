using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class DepartmentHeadService : IDepartmentHeadService
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public DepartmentHeadService(AppDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<int> ResolveDepartmentIdByAccountIdAsync(int accountId, CancellationToken ct = default)
    {
        var head = await _context.DepartmentHeads
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.AccountId == accountId, ct);

        if (head != null)
        {
            return head.DepartmentId;
        }

        // Fallback: nếu tài khoản là bác sĩ thuộc khoa
        var doctor = await _context.Doctors
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.AccountId == accountId, ct);

        if (doctor != null)
        {
            return doctor.DepartmentId;
        }

        // Fallback: nếu tài khoản là điều dưỡng thuộc khoa
        var nurse = await _context.Nurses
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.AccountId == accountId, ct);

        if (nurse != null && nurse.DepartmentId.HasValue && nurse.DepartmentId.Value > 0)
        {
            return nurse.DepartmentId.Value;
        }

        return 0;
    }

    // 1. Quản lý đề nghị ngoại lệ (Nghỉ phép / Đổi ca)
    public async Task<IReadOnlyList<DepartmentExceptionItemDto>> GetExceptionsAsync(int departmentId, ScheduleExceptionStatus? status = null, CancellationToken ct = default)
    {
        var query = _context.ScheduleExceptions
            .AsNoTracking()
            .Include(e => e.Doctor)
            .Include(e => e.ReplacementDoctor)
            .Include(e => e.ApprovedByAccount)
            .Where(e => e.Doctor.DepartmentId == departmentId);

        if (status.HasValue)
        {
            query = query.Where(e => e.Status == status.Value);
        }

        return await query
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new DepartmentExceptionItemDto(
                e.Id,
                e.DoctorId,
                e.Doctor.FullName,
                e.Date,
                e.Shift,
                e.Type,
                e.Status,
                e.ReplacementDoctorId,
                e.ReplacementDoctor != null ? e.ReplacementDoctor.FullName : null,
                e.Reason ?? "Không ghi rõ",
                e.CreatedAt,
                e.ApprovedByAccount != null ? e.ApprovedByAccount.Username : null,
                e.ApprovedAt
            ))
            .ToListAsync(ct);
    }

    public async Task<Result> ApproveExceptionAsync(ApproveExceptionRequest request, CancellationToken ct = default)
    {
        var ex = await _context.ScheduleExceptions
            .Include(e => e.Doctor)
            .FirstOrDefaultAsync(e => e.Id == request.ExceptionId, ct);

        if (ex == null)
            return Result.Failure("NOT_FOUND", "Không tìm thấy đề nghị ngoại lệ.");

        if (!request.DepartmentId.HasValue || request.DepartmentId.Value <= 0 || ex.Doctor == null || ex.Doctor.DepartmentId != request.DepartmentId.Value)
            return Result.Failure("FORBIDDEN", "Bạn chỉ có thể phê duyệt đề nghị của bác sĩ thuộc khoa mình quản lý.");

        if (ex.Status != ScheduleExceptionStatus.Pending)
            return Result.Failure("INVALID_STATUS", "Chỉ có thể phê duyệt đề nghị đang ở trạng thái chờ duyệt.");

        using var tx = await _context.Database.BeginTransactionAsync(ct);

        var now = _timeProvider.GetLocalNow().DateTime;

        ex.Status = ScheduleExceptionStatus.Approved;
        ex.ApprovedBy = request.DepartmentHeadAccountId;
        ex.ApprovedAt = now;

        var startOfDay = ex.Date.ToDateTime(new TimeOnly(0, 0));
        var endOfDay = ex.Date.ToDateTime(new TimeOnly(23, 59, 59));

        var slotsQuery = _context.AppointmentSlots
            .Where(s => s.DoctorId == ex.DoctorId && s.StartTime >= startOfDay && s.StartTime <= endOfDay);

        if (ex.Shift == ExamShift.Morning)
            slotsQuery = slotsQuery.Where(s => s.StartTime.Hour < 12);
        else if (ex.Shift == ExamShift.Afternoon)
            slotsQuery = slotsQuery.Where(s => s.StartTime.Hour >= 12);

        var slots = await slotsQuery.ToListAsync(ct);

        var appointmentsQuery = _context.Appointments
            .Include(a => a.Patient)
            .Where(a => a.DoctorId == ex.DoctorId &&
                        a.Slot != null &&
                        a.Slot.StartTime >= startOfDay &&
                        a.Slot.StartTime <= endOfDay &&
                        (a.StatusId == (int)AppointmentStatusId.Confirmed || a.StatusId == (int)AppointmentStatusId.Pending));

        if (ex.Shift == ExamShift.Morning)
            appointmentsQuery = appointmentsQuery.Where(a => a.Slot!.StartTime.Hour < 12);
        else if (ex.Shift == ExamShift.Afternoon)
            appointmentsQuery = appointmentsQuery.Where(a => a.Slot!.StartTime.Hour >= 12);

        var affectedApps = await appointmentsQuery.ToListAsync(ct);

        // Trường hợp 1: Có bác sĩ thay thế (Đổi ca / Nghỉ có người trực thay)
        if (ex.ReplacementDoctorId.HasValue && ex.ReplacementDoctorId.Value > 0)
        {
            var repDoctorId = ex.ReplacementDoctorId.Value;
            var repDoctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == repDoctorId, ct);

            var repExistingSlots = await _context.AppointmentSlots
                .Where(s => s.DoctorId == repDoctorId && s.StartTime >= startOfDay && s.StartTime <= endOfDay)
                .ToListAsync(ct);

            var slotMapping = new Dictionary<int, int>();

            foreach (var slot in slots)
            {
                var existingRepSlot = repExistingSlots.FirstOrDefault(s => s.StartTime == slot.StartTime);
                if (existingRepSlot != null)
                {
                    existingRepSlot.Capacity += slot.Capacity;
                    slot.Status = SlotStatus.Blocked;
                    slotMapping[slot.Id] = existingRepSlot.Id;
                }
                else
                {
                    slot.DoctorId = repDoctorId;
                    slotMapping[slot.Id] = slot.Id;
                }
            }

            foreach (var app in affectedApps)
            {
                app.DoctorId = repDoctorId;
                if (app.SlotId.HasValue && slotMapping.TryGetValue(app.SlotId.Value, out var newSlotId))
                {
                    app.SlotId = newSlotId;
                }
                app.UpdatedAt = now;

                if (app.BookedBy.HasValue)
                {
                    _context.Notifications.Add(new Notification
                    {
                        AccountId = app.BookedBy.Value,
                        AppointmentId = app.Id,
                        Title = "Thay đổi bác sĩ khám",
                        Content = $"Lịch khám của bệnh nhân {app.Patient.FullName} ngày {ex.Date:dd/MM/yyyy} đã được điều phối sang BS. {repDoctor?.FullName ?? "chuyên khoa"}.",
                        CreatedAt = now
                    });
                }
            }
        }
        else
        {
            // Trường hợp 2: Không có bác sĩ thay thế (Nghỉ không người trực thay)
            // Đóng các slot khám của ca
            foreach (var slot in slots)
            {
                slot.Status = SlotStatus.Blocked;
            }

            // Chuyển các lịch hẹn đã đặt sang trạng thái RescheduleRequired và thông báo in-app
            foreach (var app in affectedApps)
            {
                app.StatusId = (int)AppointmentStatusId.RescheduleRequired;
                app.UpdatedAt = now;

                if (app.BookedBy.HasValue)
                {
                    _context.Notifications.Add(new Notification
                    {
                        AccountId = app.BookedBy.Value,
                        AppointmentId = app.Id,
                        Title = "Lịch khám cần đặt lại",
                        Content = $"Bác sĩ phụ trách ngày {ex.Date:dd/MM/yyyy} của bệnh nhân {app.Patient.FullName} có lịch nghỉ đột xuất. Vui lòng chọn lại thời gian hoặc bác sĩ khám.",
                        CreatedAt = now
                    });
                }
            }
        }

        await _context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RejectExceptionAsync(RejectExceptionRequest request, CancellationToken ct = default)
    {
        var ex = await _context.ScheduleExceptions
            .Include(e => e.Doctor)
            .FirstOrDefaultAsync(e => e.Id == request.ExceptionId, ct);
        if (ex == null)
            return Result.Failure("NOT_FOUND", "Không tìm thấy đề nghị.");

        if (!request.DepartmentId.HasValue || request.DepartmentId.Value <= 0 || ex.Doctor == null || ex.Doctor.DepartmentId != request.DepartmentId.Value)
            return Result.Failure("FORBIDDEN", "Bạn chỉ có thể từ chối đề nghị của bác sĩ thuộc khoa mình quản lý.");

        if (ex.Status != ScheduleExceptionStatus.Pending)
            return Result.Failure("INVALID_STATUS", "Chỉ có thể từ chối đề nghị đang ở trạng thái chờ duyệt.");

        using var tx = await _context.Database.BeginTransactionAsync(ct);

        ex.Status = ScheduleExceptionStatus.Rejected;
        ex.ApprovedBy = request.DepartmentHeadAccountId;
        ex.ApprovedAt = _timeProvider.GetLocalNow().DateTime;

        if (!string.IsNullOrWhiteSpace(request.RejectReason))
        {
            ex.Reason = (ex.Reason ?? "") + $" [Từ chối: {request.RejectReason.Trim()}]";
        }

        await _context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Result.Success();
    }

    // 2. Thiết lập lịch khám mẫu & hạn mức
    public async Task<IReadOnlyList<DepartmentScheduleItemDto>> GetSchedulesAsync(int departmentId, CancellationToken ct = default)
    {
        return await _context.DoctorSchedules
            .AsNoTracking()
            .Include(s => s.Doctor)
            .Include(s => s.Room)
            .Where(s => s.Doctor.DepartmentId == departmentId)
            .OrderBy(s => s.Doctor.FullName)
            .ThenBy(s => s.DayOfWeek)
            .ThenBy(s => s.Shift)
            .Select(s => new DepartmentScheduleItemDto(
                s.Id,
                s.DoctorId,
                s.Doctor.FullName,
                s.RoomId,
                s.Room.RoomNumber,
                s.DayOfWeek,
                s.Shift,
                s.StartTime,
                s.EndTime,
                s.SlotDurationMinutes,
                s.MaxPatients,
                s.OnlineSlots,
                s.IsActive
            ))
            .ToListAsync(ct);
    }

    public async Task<Result<int>> UpsertDoctorScheduleAsync(UpsertDoctorScheduleRequest request, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        if (request.EffectiveFrom < today)
            return Result.Failure<int>("PAST_DATE", "Không thể thiết lập lịch khám cho ngày trong quá khứ. Chỉ được phép thiết lập từ ngày hiện tại hoặc tương lai.");

        if (request.OnlineSlots < 0 || request.OnlineSlots > request.MaxPatients)
            return Result.Failure<int>("INVALID_QUOTA", "Số lượng slot trực tuyến phải từ 0 đến tổng số bệnh nhân.");

        if (request.EndTime <= request.StartTime)
            return Result.Failure<int>("INVALID_TIME", "Giờ kết thúc ca khám phải sau giờ bắt đầu.");

        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == request.DoctorId, ct);
        if (doctor == null)
            return Result.Failure<int>("NOT_FOUND", "Không tìm thấy bác sĩ.");

        var room = await _context.ClinicRooms.FirstOrDefaultAsync(r => r.Id == request.RoomId, ct);
        if (room == null || room.DepartmentId != doctor.DepartmentId)
            return Result.Failure<int>("INVALID_ROOM", "Phòng khám không thuộc khoa của bác sĩ.");

        DoctorSchedule? schedule = null;

        if (request.ScheduleId.HasValue && request.ScheduleId.Value > 0)
        {
            schedule = await _context.DoctorSchedules
                .Include(s => s.Doctor)
                .FirstOrDefaultAsync(s => s.Id == request.ScheduleId.Value, ct);

            if (schedule != null && schedule.Doctor.DepartmentId != doctor.DepartmentId)
            {
                return Result.Failure<int>("FORBIDDEN", "Không thể chỉnh sửa lịch khám thuộc khoa khác.");
            }
        }

        if (schedule == null)
        {
            schedule = await _context.DoctorSchedules.FirstOrDefaultAsync(s =>
                s.DoctorId == request.DoctorId &&
                s.DayOfWeek == request.DayOfWeek &&
                s.Shift == request.Shift, ct);
        }

        if (schedule != null)
        {
            schedule.DoctorId = request.DoctorId;
            schedule.RoomId = request.RoomId;
            schedule.DayOfWeek = request.DayOfWeek;
            schedule.Shift = request.Shift;
            schedule.StartTime = request.StartTime;
            schedule.EndTime = request.EndTime;
            schedule.SlotDurationMinutes = request.SlotDurationMinutes;
            schedule.MaxPatients = request.MaxPatients;
            schedule.OnlineSlots = request.OnlineSlots;
            schedule.EffectiveFrom = request.EffectiveFrom;
            schedule.EffectiveTo = request.EffectiveTo;
            schedule.IsActive = true;
        }
        else
        {
            schedule = new DoctorSchedule
            {
                DoctorId = request.DoctorId,
                RoomId = request.RoomId,
                DayOfWeek = request.DayOfWeek,
                Shift = request.Shift,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                SlotDurationMinutes = request.SlotDurationMinutes,
                MaxPatients = request.MaxPatients,
                OnlineSlots = request.OnlineSlots,
                EffectiveFrom = request.EffectiveFrom,
                EffectiveTo = request.EffectiveTo,
                IsActive = true
            };
            _context.DoctorSchedules.Add(schedule);
        }

        await _context.SaveChangesAsync(ct);
        return Result<int>.Success(schedule.Id);
    }

    public async Task<Result> DeleteDoctorScheduleAsync(int scheduleId, int departmentId, CancellationToken ct = default)
    {
        var schedule = await _context.DoctorSchedules
            .Include(s => s.Doctor)
            .FirstOrDefaultAsync(s => s.Id == scheduleId, ct);

        if (schedule == null)
            return Result.Failure("NOT_FOUND", "Không tìm thấy lịch khám.");

        if (schedule.Doctor.DepartmentId != departmentId)
            return Result.Failure("FORBIDDEN", "Bạn chỉ có thể xóa lịch khám thuộc khoa của mình.");

        _context.DoctorSchedules.Remove(schedule);
        await _context.SaveChangesAsync(ct);
        return Result.Success();
    }

    // 3. Phân công ca trực đêm nhân sự
    public async Task<IReadOnlyList<DepartmentDutyShiftDto>> GetDutyShiftsAsync(int departmentId, CancellationToken ct = default)
    {
        return await _context.DutyShifts
            .AsNoTracking()
            .Include(d => d.Account)
                .ThenInclude(a => a.Role)
            .Include(d => d.Account.Doctor)
            .Include(d => d.Account.Nurse)
            .Include(d => d.AssignedByAccount)
                .ThenInclude(a => a!.DepartmentHead)
            .Where(d => d.DepartmentId == departmentId)
            .OrderByDescending(d => d.StartTime)
            .Select(d => new DepartmentDutyShiftDto(
                d.Id,
                d.AccountId,
                d.Account.Doctor != null ? d.Account.Doctor.FullName : (d.Account.Nurse != null ? d.Account.Nurse.FullName : d.Account.Username),
                d.Account.Role.Name,
                d.StartTime,
                d.EndTime,
                d.Note,
                d.AssignedByAccount != null ? (d.AssignedByAccount.DepartmentHead != null ? d.AssignedByAccount.DepartmentHead.FullName : d.AssignedByAccount.Username) : null
            ))
            .ToListAsync(ct);
    }

    public async Task<Result<int>> AssignDutyShiftAsync(AssignDutyShiftRequest request, CancellationToken ct = default)
    {
        var now = _timeProvider.GetLocalNow().DateTime;
        if (request.StartTime < now)
            return Result.Failure<int>("PAST_TIME", "Không thể phân công ca trực trong quá khứ. Vui lòng chọn thời gian từ hiện tại hoặc tương lai.");

        if (request.EndTime <= request.StartTime)
            return Result.Failure<int>("INVALID_TIME", "Giờ kết thúc ca trực phải sau giờ bắt đầu.");

        // Kiểm tra nhân sự đã có ca trực trong đêm/khoảng thời gian này chưa
        bool hasConflict = await _context.DutyShifts
            .AnyAsync(d => d.AccountId == request.AccountId &&
                           ((d.StartTime < request.EndTime && d.EndTime > request.StartTime) ||
                            (d.StartTime.Date == request.StartTime.Date)), ct);

        if (hasConflict)
        {
            return Result.Failure<int>("DUPLICATE_DUTY", "Nhân sự này đã có ca trực trong đêm/ngày này rồi. Không được phép phân công trùng ca.");
        }

        var duty = new DutyShift
        {
            AccountId = request.AccountId,
            DepartmentId = request.DepartmentId,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Note = string.IsNullOrWhiteSpace(request.Note) ? "Trực đêm tại khoa" : request.Note.Trim(),
            AssignedBy = request.AssignedByAccountId,
            CreatedAt = now
        };

        _context.DutyShifts.Add(duty);
        await _context.SaveChangesAsync(ct);
        return Result<int>.Success(duty.Id);
    }

    public async Task<Result> DeleteDutyShiftAsync(int dutyShiftId, int departmentId, CancellationToken ct = default)
    {
        var duty = await _context.DutyShifts.FirstOrDefaultAsync(d => d.Id == dutyShiftId, ct);
        if (duty == null)
            return Result.Failure("NOT_FOUND", "Không tìm thấy ca trực.");

        if (duty.DepartmentId != departmentId)
            return Result.Failure("FORBIDDEN", "Bạn chỉ có thể xóa ca trực thuộc khoa của mình.");

        _context.DutyShifts.Remove(duty);
        await _context.SaveChangesAsync(ct);
        return Result.Success();
    }

    // 4. Giám sát bệnh án trong khoa
    public async Task<IReadOnlyList<DepartmentMedicalRecordDto>> GetMedicalRecordsAsync(int departmentId, int viewerAccountId, string? ipAddress = null, CancellationToken ct = default)
    {
        var records = await _context.MedicalRecords
            .AsNoTracking()
            .Include(r => r.Doctor)
            .Include(r => r.Appointment)
                .ThenInclude(a => a.Patient)
            .Where(r => r.Doctor.DepartmentId == departmentId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new DepartmentMedicalRecordDto(
                r.Id,
                r.AppointmentId,
                r.Appointment.AppointmentCode,
                r.Appointment.Patient.FullName,
                DateTime.Today.Year - r.Appointment.Patient.DateOfBirth.Year,
                r.Appointment.Patient.Gender == Gender.Male ? "Nam" : "Nữ",
                r.Doctor.FullName,
                r.CreatedAt,
                r.PrimaryDiagnosisCode ?? "—",
                r.PrimaryDiagnosisDesc ?? "—",
                r.Conclusion ?? "Đã hoàn thành"
            ))
            .ToListAsync(ct);

        // Ghi AccessLog cho hành vi Trưởng khoa xem bệnh án khoa
        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = viewerAccountId,
            Action = "VIEW_RECORD",
            OccurredAt = _timeProvider.GetLocalNow().DateTime,
            IpAddress = ipAddress
        });
        await _context.SaveChangesAsync(ct);

        return records;
    }

    // 5. Tổng quan vận hành khoa
    public async Task<DepartmentOverviewDto> GetDepartmentOverviewAsync(int departmentId, CancellationToken ct = default)
    {
        var dept = await _context.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == departmentId, ct);
        var deptName = dept?.Name ?? "Khoa chuyên môn";

        var totalDoctors = await _context.Doctors.CountAsync(d => d.DepartmentId == departmentId, ct);
        var totalRooms = await _context.ClinicRooms.CountAsync(r => r.DepartmentId == departmentId, ct);
        var pendingExceptions = await _context.ScheduleExceptions
            .CountAsync(e => e.Doctor.DepartmentId == departmentId && e.Status == ScheduleExceptionStatus.Pending, ct);

        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        var startOfDay = today.ToDateTime(new TimeOnly(0, 0));
        var endOfDay = today.ToDateTime(new TimeOnly(23, 59, 59));

        var todayApps = await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Room)
            .Include(a => a.VitalSigns)
            .Include(a => a.Slot)
            .Where(a => (a.DepartmentId == departmentId || (a.Doctor != null && a.Doctor.DepartmentId == departmentId))
                        && ((a.Slot != null && a.Slot.StartTime >= startOfDay && a.Slot.StartTime <= endOfDay)
                            || (a.CheckedInAt.HasValue && a.CheckedInAt.Value >= startOfDay && a.CheckedInAt.Value <= endOfDay)
                            || (a.CreatedAt >= startOfDay && a.CreatedAt <= endOfDay)))
            .ToListAsync(ct);

        var totalToday = todayApps.Count;
        var completed = todayApps.Count(a => a.StatusId == (int)AppointmentStatusId.Completed);
        var inProgress = todayApps.Count(a => a.StatusId == (int)AppointmentStatusId.InProgress || a.StatusId == (int)AppointmentStatusId.ReadyForDoctor);
        var waitingReception = todayApps.Count(a => a.StatusId == (int)AppointmentStatusId.Confirmed || a.StatusId == (int)AppointmentStatusId.Pending);
        var needVitalSigns = todayApps.Count(a => a.StatusId == (int)AppointmentStatusId.CheckedIn && (a.VitalSigns == null || !a.VitalSigns.SystolicBp.HasValue));

        var todayAppIds = todayApps.Select(a => a.Id).ToList();
        var unpaidInvoices = await _context.Invoices
            .AsNoTracking()
            .Where(i => todayAppIds.Contains(i.AppointmentId) && i.Status != InvoiceStatus.Paid)
            .ToListAsync(ct);
        var unpaidCount = unpaidInvoices.Count;
        var unpaidAmount = unpaidInvoices.Sum(i => i.PatientAmount);

        var activeQueue = todayApps
            .Where(a => a.StatusId == (int)AppointmentStatusId.ReadyForDoctor ||
                        a.StatusId == (int)AppointmentStatusId.CheckedIn ||
                        a.StatusId == (int)AppointmentStatusId.InProgress ||
                        a.StatusId == (int)AppointmentStatusId.Confirmed)
            .OrderBy(a => a.PriorityLevel)
            .ThenBy(a => a.QueueNumber)
            .Select(a => new DepartmentQueueItemDto(
                a.Id,
                a.AppointmentCode,
                a.Patient.FullName,
                DateTime.Today.Year - a.Patient.DateOfBirth.Year,
                a.Patient.Gender == Gender.Male ? "Nam" : "Nữ",
                a.QueueNumber ?? "—",
                a.StatusId,
                a.StatusId == (int)AppointmentStatusId.ReadyForDoctor ? "Chờ bác sĩ" :
                a.StatusId == (int)AppointmentStatusId.InProgress ? "Đang khám" :
                a.StatusId == (int)AppointmentStatusId.CheckedIn ? "Đã tiếp nhận" :
                a.StatusId == (int)AppointmentStatusId.Confirmed ? "Đã đặt hẹn" : "Chờ xử lý",
                a.Room != null ? a.Room.RoomNumber : (a.Slot != null && a.Slot.Room != null ? a.Slot.Room.RoomNumber : "P.201"),
                a.Doctor != null ? a.Doctor.FullName : "BS. Chuyên khoa",
                a.Slot != null ? a.Slot.StartTime : (a.CheckedInAt ?? a.CreatedAt),
                a.Reason ?? "Khám chuyên khoa theo hẹn"
            ))
            .ToList();

        return new DepartmentOverviewDto(
            departmentId,
            deptName,
            totalDoctors,
            totalRooms,
            pendingExceptions,
            totalToday,
            completed,
            inProgress,
            waitingReception,
            needVitalSigns,
            unpaidCount,
            unpaidAmount,
            activeQueue
        );
    }

    // 6. Danh mục phụ trợ
    public async Task<IReadOnlyList<DoctorListItemDto>> GetDoctorsInDepartmentAsync(int departmentId, CancellationToken ct = default)
    {
        return await _context.Doctors
            .AsNoTracking()
            .Where(d => d.DepartmentId == departmentId)
            .OrderBy(d => d.FullName)
            .Select(d => new DoctorListItemDto(d.Id, d.FullName, d.Specialty ?? "Chuyên khoa", d.Department.Name, d.LicenseNumber ?? "", d.AccountId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StaffListItemDto>> GetStaffInDepartmentAsync(int departmentId, CancellationToken ct = default)
    {
        // Lấy danh sách account là bác sĩ hoặc y tá thuộc khoa
        var doctorAccountIds = await _context.Doctors
            .Where(d => d.DepartmentId == departmentId)
            .Select(d => d.AccountId)
            .ToListAsync(ct);

        var nurseAccountIds = await _context.Nurses
            .Where(n => n.DepartmentId == departmentId)
            .Select(n => n.AccountId)
            .ToListAsync(ct);

        var allAccountIds = doctorAccountIds.Concat(nurseAccountIds).Distinct().ToList();

        return await _context.Accounts
            .AsNoTracking()
            .Include(a => a.Role)
            .Include(a => a.Doctor)
            .Include(a => a.Nurse)
            .Where(a => allAccountIds.Contains(a.Id))
            .OrderBy(a => a.Username)
            .Select(a => new StaffListItemDto(
                a.Id,
                a.Username,
                a.Doctor != null ? a.Doctor.FullName : (a.Nurse != null ? a.Nurse.FullName : a.Username),
                a.Role.Name,
                a.Role.Code,
                a.IsActive
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ClinicRoomDto>> GetRoomsInDepartmentAsync(int departmentId, CancellationToken ct = default)
    {
        return await _context.ClinicRooms
            .AsNoTracking()
            .Where(r => r.DepartmentId == departmentId)
            .OrderBy(r => r.RoomNumber)
            .Select(r => new ClinicRoomDto(r.Id, r.DepartmentId, r.RoomName, r.RoomNumber))
            .ToListAsync(ct);
    }
}
