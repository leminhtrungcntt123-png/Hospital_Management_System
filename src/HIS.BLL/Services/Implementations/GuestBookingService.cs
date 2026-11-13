using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HIS.BLL.Services;

public sealed class GuestBookingService(
    AppDbContext db,
    TimeProvider clock,
    IOptions<BookingOptions> options) : IGuestBookingService
{
    private DateTime Now => DateTime.SpecifyKind(clock.GetLocalNow().DateTime, DateTimeKind.Unspecified);
    private DateOnly Today => DateOnly.FromDateTime(Now);

    public async Task<Result<IReadOnlyList<GuestBookingSlot>>> GetSlotsAsync(
        int doctorId,
        DateOnly date,
        CancellationToken ct = default)
    {
        if (doctorId <= 0 || date < Today || date > Today.AddDays(options.Value.SlotGenerationDaysAhead))
        {
            return Result.Failure<IReadOnlyList<GuestBookingSlot>>("InvalidDate", "Chọn bác sĩ và ngày khám trong thời gian bệnh viện đang nhận lịch.");
        }

        var schedules = await db.DoctorSchedules
            .AsNoTracking()
            .Include(s => s.Room)
            .Include(s => s.Doctor)
            .Where(s => s.DoctorId == doctorId
                && s.Doctor.Account.IsActive
                && s.Doctor.Account.Role.Code == "DOCTOR"
                && s.RoomId.HasValue
                && s.Room != null
                && s.Room.DepartmentId == s.Doctor.DepartmentId
                && s.Status == DoctorScheduleStatus.Approved
                && s.WorkDate == date
                && !db.ScheduleExceptions.Any(e => e.DoctorId == doctorId
                    && e.Date == date
                    && e.Status == ScheduleExceptionStatus.Approved
                    && (e.Shift == null || e.Shift == s.Shift)))
            .OrderBy(s => s.StartTime)
            .ToListAsync(ct);
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var slots = await db.AppointmentSlots
            .AsNoTracking()
            .Where(s => s.DoctorId == doctorId && s.StartTime >= dayStart && s.StartTime < dayStart.AddDays(1))
            .ToListAsync(ct);
        var result = new List<GuestBookingSlot>();
        foreach (var schedule in schedules)
        {
            var start = date.ToDateTime(schedule.StartTime);
            var end = date.ToDateTime(schedule.EndTime);
            if (schedule.SlotDurationMinutes <= 0 || end <= start || schedule.OnlineSlots <= 0)
            {
                continue;
            }
            var count = Math.Min((int)((end - start).TotalMinutes / schedule.SlotDurationMinutes), schedule.OnlineSlots);
            if (count <= 0)
            {
                continue;
            }
            var booked = slots.Where(s => s.StartTime >= start && s.StartTime < end).Sum(s => s.BookedCount);
            var remaining = Math.Min(schedule.OnlineSlots, schedule.MaxPatients) - booked;
            if (remaining <= 0)
            {
                continue;
            }
            for (var index = 0; index < count; index++)
            {
                var time = start.AddMinutes(index * schedule.SlotDurationMinutes);
                if (time <= Now)
                {
                    continue;
                }
                var existing = slots.SingleOrDefault(s => s.StartTime == time);
                var capacity = schedule.OnlineSlots / count + (index < schedule.OnlineSlots % count ? 1 : 0);
                if (existing != null && (existing.Status != SlotStatus.Available || existing.RoomId != schedule.RoomId
                    || existing.EndTime != time.AddMinutes(schedule.SlotDurationMinutes)))
                {
                    continue;
                }
                var available = Math.Min(remaining, (existing?.Capacity ?? capacity) - (existing?.BookedCount ?? 0));
                if (available > 0 && result.All(s => s.StartTime != time))
                {
                    result.Add(new(schedule.Id, time, time.AddMinutes(schedule.SlotDurationMinutes), schedule.Room?.RoomNumber ?? string.Empty, available));
                }
            }
        }
        return Result.Success<IReadOnlyList<GuestBookingSlot>>(result);
    }

    public async Task<Result<GuestBookingReceipt>> ConfirmAsync(
        GuestBookingRequest request,
        CancellationToken ct = default)
    {
        var validation = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), validation, true)
            || string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length < 2
            || request.DateOfBirth > Today || request.DateOfBirth < Today.AddYears(-130)
            || request.StartTime.Kind != DateTimeKind.Unspecified)
        {
            return Result.Failure<GuestBookingReceipt>("InvalidInput", "Kiểm tra họ tên, ngày sinh, số điện thoại và giờ khám.");
        }

        var phone = request.PhoneNumber.StartsWith("+84", StringComparison.Ordinal) ? "0" + request.PhoneNumber[3..] : request.PhoneNumber;
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                // Serialize guest requests for one contact before checking duplicate/active bookings.
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({phone}))", ct);
                var schedule = await db.DoctorSchedules
                    .FromSqlInterpolated($"SELECT * FROM doctor_schedules WHERE id = {request.ScheduleId} FOR UPDATE")
                    .AsNoTracking()
                    .SingleOrDefaultAsync(ct);
                if (schedule == null)
                {
                    return Result.Failure<GuestBookingReceipt>("SlotUnavailable", "Lịch khám không còn khả dụng. Hãy chọn lại.");
                }
                var date = DateOnly.FromDateTime(request.StartTime);
                var available = await GetSlotsAsync(schedule.DoctorId, date, ct);
                var choice = available.Value?.SingleOrDefault(s => s.ScheduleId == request.ScheduleId && s.StartTime == request.StartTime);
                if (!available.IsSuccess || choice == null)
                {
                    return Result.Failure<GuestBookingReceipt>("SlotUnavailable", "Khung giờ khám đã trôi qua hoặc vừa hết chỗ. Hãy chọn lại khung giờ khác.");
                }

                var fullName = request.FullName.Trim();
                var active = db.Appointments.Where(a => a.Patient.PhoneNumber == phone
                    && a.StatusId >= 1 && a.StatusId <= 5
                    && a.Slot != null && a.Slot.StartTime > Now);
                if (await active.CountAsync(ct) >= options.Value.MaxActiveAppointmentsPerAccount
                    || await active.AnyAsync(a => a.Patient.FullName == fullName && a.Patient.DateOfBirth == request.DateOfBirth
                        && a.DoctorId == schedule.DoctorId && a.Slot!.StartTime >= date.ToDateTime(TimeOnly.MinValue)
                        && a.Slot.StartTime < date.AddDays(1).ToDateTime(TimeOnly.MinValue), ct))
                {
                    return Result.Failure<GuestBookingReceipt>("DuplicateBooking", "Thông tin này đã có lịch khám đang hiệu lực hoặc đạt giới hạn đặt khám. Vui lòng kiểm tra mã lịch đã lưu hoặc liên hệ quầy tiếp nhận.");
                }

                var slot = await db.AppointmentSlots.AsNoTracking().SingleOrDefaultAsync(s => s.DoctorId == schedule.DoctorId && s.StartTime == request.StartTime, ct);
                if (slot == null)
                {
                    slot = new AppointmentSlot
                    {
                        ScheduleId = schedule.Id,
                        DoctorId = schedule.DoctorId,
                        RoomId = schedule.RoomId ?? 0,
                        StartTime = choice.StartTime,
                        EndTime = choice.EndTime,
                        Capacity = choice.Remaining
                    };
                    db.AppointmentSlots.Add(slot);
                    await db.SaveChangesAsync(ct);
                    db.Entry(slot).State = EntityState.Detached;
                }
                var updated = await db.AppointmentSlots.Where(s => s.Id == slot.Id && s.Status == SlotStatus.Available && s.BookedCount < s.Capacity)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.BookedCount, s => s.BookedCount + 1)
                        .SetProperty(s => s.Status, s => s.BookedCount + 1 >= s.Capacity ? SlotStatus.Full : SlotStatus.Available)
                        .SetProperty(s => s.Version, s => s.Version + 1), ct);
                if (updated != 1)
                {
                    return Result.Failure<GuestBookingReceipt>("SlotUnavailable", "Khung giờ vừa hết chỗ. Hãy chọn lại.");
                }

                // Matching submitted identity never grants a guest access to the existing medical record.
                Patient? patient = null;
                if (request.PatientId.HasValue && request.PatientId.Value > 0 && request.BookedByAccountId.HasValue)
                {
                    var isAuthorized = await db.AccountPatientProfiles.AnyAsync(
                        link => link.AccountId == request.BookedByAccountId.Value
                             && link.PatientId == request.PatientId.Value
                             && link.Status == LinkStatus.Active, ct);

                    if (isAuthorized)
                    {
                        patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == request.PatientId.Value, ct);
                    }
                }
                if (patient == null)
                {
                    patient = await db.Patients.FirstOrDefaultAsync(p => p.FullName == fullName
                        && p.DateOfBirth == request.DateOfBirth && p.PhoneNumber == phone, ct);
                }
                if (patient == null)
                {
                    patient = new Patient
                    {
                        FullName = fullName,
                        DateOfBirth = request.DateOfBirth,
                        PhoneNumber = phone,
                        Gender = request.Gender,
                        NationalId = string.IsNullOrWhiteSpace(request.MedicalCode) ? null : request.MedicalCode.Trim(),
                        Address = request.Address?.Trim(),
                        CreatedAt = Now
                    };
                }
                var appointment = new Appointment
                {
                    AppointmentCode = "HIS-" + Today.ToString("yyyyMMdd") + "-" + RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 6),
                    Patient = patient,
                    SlotId = slot.Id,
                    DoctorId = schedule.DoctorId,
                    RoomId = schedule.RoomId,
                    DepartmentId = (await db.Doctors.AsNoTracking().SingleAsync(d => d.Id == schedule.DoctorId, ct)).DepartmentId,
                    StatusId = (int)AppointmentStatusId.Confirmed,
                    BookingChannel = BookingChannel.Web,
                    VisitType = VisitType.Scheduled,
                    BookedBy = request.BookedByAccountId,
                    Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
                    CreatedAt = Now,
                    UpdatedAt = Now
                };
                appointment.StatusHistory.Add(new AppointmentStatusHistory { NewStatusId = (int)AppointmentStatusId.Confirmed, ChangedAt = Now });
                db.Appointments.Add(appointment);
                await db.SaveChangesAsync(ct);
                var receipt = await db.Appointments.AsNoTracking().Where(a => a.Id == appointment.Id)
                    .Select(a => new GuestBookingReceipt(a.AppointmentCode, fullName, a.Doctor!.FullName, a.Department!.Name, a.Room!.RoomNumber, choice.StartTime, choice.EndTime))
                    .SingleAsync(ct);
                await transaction.CommitAsync(ct);
                return Result.Success(receipt);
            }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await transaction.RollbackAsync(ct);
                return Result.Failure<GuestBookingReceipt>("BookingConflict", "Lịch khám vừa thay đổi. Vui lòng tải lại khung giờ và thử lại.");
            }
        });
    }
}
