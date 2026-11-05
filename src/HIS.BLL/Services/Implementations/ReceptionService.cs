using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HIS.BLL.Services;

public sealed class ReceptionService(AppDbContext db, TimeProvider clock, IOptions<BookingOptions> options) : IReceptionService
{
    private DateTime Now => DateTime.SpecifyKind(clock.GetLocalNow().DateTime, DateTimeKind.Unspecified);
    private DateOnly Today => DateOnly.FromDateTime(Now);

    private async Task<Nurse?> NurseAsync(int accountId, CancellationToken ct) =>
        await db.Nurses.Include(n => n.Department).SingleOrDefaultAsync(n => n.AccountId == accountId
            && n.Account.IsActive && n.Account.Role.Code == "NURSE" && n.DepartmentId != null, ct);

    public async Task<Result<ReceptionDesk>> GetDeskAsync(int staffAccountId, string? search, CancellationToken ct = default)
    {
        var nurse = await NurseAsync(staffAccountId, ct);
        if (nurse == null) return Result.Failure<ReceptionDesk>("Forbidden", "Tài khoản chưa có quyền tiếp nhận trong khoa.");
        var start = Today.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        search = search?.Trim();
        var visits = db.Appointments.AsNoTracking().Where(a => a.DepartmentId == nurse.DepartmentId
            && ((a.Slot != null && a.Slot.StartTime >= start && a.Slot.StartTime < end)
                || (a.Slot == null && a.CreatedAt >= start && a.CreatedAt < end)));
        if (!string.IsNullOrEmpty(search)) visits = visits.Where(a => a.AppointmentCode == search || a.Patient.PhoneNumber == search || a.Patient.NationalId == search || a.QueueNumber == search);
        var rows = await visits.OrderByDescending(a => a.PriorityLevel).ThenBy(a => a.Slot != null ? a.Slot.StartTime : a.CreatedAt)
            .Select(a => new ReceptionVisit(a.Id, a.AppointmentCode, a.Patient.FullName, a.Patient.DateOfBirth,
                a.Patient.PhoneNumber, a.Doctor != null ? a.Doctor.FullName : "Chưa phân công",
                a.Room != null ? a.Room.RoomNumber : "", a.Slot != null ? a.Slot.StartTime : a.CreatedAt,
                a.BookingChannel, a.StatusId, a.QueueNumber, a.PriorityLevel)).Take(100).ToListAsync(ct);
        var schedules = await db.DoctorSchedules.AsNoTracking().Include(s => s.Doctor).Include(s => s.Room)
            .Where(s => s.Doctor.DepartmentId == nurse.DepartmentId && s.Doctor.Account.IsActive && s.IsActive
                && s.DayOfWeek == Today.DayOfWeek && s.EffectiveFrom <= Today && (s.EffectiveTo == null || s.EffectiveTo >= Today)
                && !db.ScheduleExceptions.Any(e => e.DoctorId == s.DoctorId && e.Date == Today
                    && e.Status == ScheduleExceptionStatus.Approved && (e.Shift == null || e.Shift == s.Shift)))
            .OrderBy(s => s.StartTime).ThenBy(s => s.Doctor.FullName).ToListAsync(ct);
        var choices = schedules.Where(s => Today.ToDateTime(s.StartTime) <= Now && Today.ToDateTime(s.EndTime) >= Now)
            .Select(s => new ReceptionSchedule(s.Id, $"{s.Doctor.FullName} · Phòng {s.Room.RoomNumber} · {s.StartTime:HH:mm}–{s.EndTime:HH:mm}")).ToList();
        var patients = !string.IsNullOrEmpty(search) && search.Length >= 9
            ? await db.Patients.AsNoTracking().Where(p => p.PhoneNumber == search || p.NationalId == search)
                .OrderBy(p => p.FullName).Select(p => new ReceptionPatient(p.Id, p.FullName, p.DateOfBirth, p.PhoneNumber)).Take(20).ToListAsync(ct)
            : [];
        return Result.Success(new ReceptionDesk(nurse.Department!.Name, Today, rows, choices, patients));
    }

    public Task<Result<string>> CheckInAsync(int staffAccountId, int appointmentId, bool identityVerified, CancellationToken ct = default) =>
        WriteAsync(staffAccountId, async nurse =>
        {
            Require(identityVerified, "Cần đối chiếu danh tính người khám trước khi tiếp nhận.");
            var a = await db.Appointments.Include(a => a.Slot).Include(a => a.Patient)
                .SingleOrDefaultAsync(a => a.Id == appointmentId && a.DepartmentId == nurse.DepartmentId, ct);
            Require(a != null, "Không tìm thấy lịch trong khoa phụ trách.");
            if (a!.StatusId is >= 3 and <= 6 && a.CheckedInAt != null && a.QueueNumber != null) return a.QueueNumber;
            Require(a.StatusId == (int)AppointmentStatusId.Confirmed && a.BookingChannel == BookingChannel.Web
                && a.Slot != null && a.RoomId != null, "Chỉ tiếp nhận lịch web đã xác nhận và có phòng khám.");
            Require(a.Slot!.Status != SlotStatus.Blocked, "Ca khám đã bị khóa. Vui lòng kiểm tra lịch điều chỉnh.");
            var schedule = await db.DoctorSchedules.Where(s => s.DoctorId == a.DoctorId && s.RoomId == a.RoomId
                && s.Doctor.Account.IsActive && s.Doctor.DepartmentId == nurse.DepartmentId && s.Room.DepartmentId == nurse.DepartmentId
                && s.DayOfWeek == Today.DayOfWeek && s.IsActive && s.EffectiveFrom <= Today
                && (s.EffectiveTo == null || s.EffectiveTo >= Today) && s.Shift == (a.Slot.StartTime.Hour < 12 ? ExamShift.Morning : ExamShift.Afternoon))
                .OrderByDescending(s => s.EffectiveFrom).FirstOrDefaultAsync(ct);
            Require(schedule != null && CanCheckIn(Now, a.Slot.StartTime, Today.ToDateTime(schedule.EndTime), options.Value.CheckInEarlyMinutes),
                "Lịch không thuộc hôm nay, chưa đến thời gian tiếp nhận hoặc đã hết ca khám.");
            Require(!await db.ScheduleExceptions.AnyAsync(e => e.DoctorId == a.DoctorId && e.Date == Today
                && e.Status == ScheduleExceptionStatus.Approved && (e.Shift == null || e.Shift == schedule!.Shift), ct), "Bác sĩ đã có thay đổi lịch được duyệt.");
            var link = await db.AccountPatientProfiles.SingleOrDefaultAsync(l => l.AccountId == a.BookedBy && l.PatientId == a.PatientId, ct);
            if (link?.Status == LinkStatus.Pending)
            {
                link.Status = LinkStatus.Active; link.VerifiedBy = staffAccountId; link.VerifiedAt = Now;
                // Identity verification does not grant access to another adult's medical records.
                link.CanViewRecords = link.Relationship == PatientRelationship.Self
                    || link.Relationship == PatientRelationship.Child && a.Patient.DateOfBirth.AddYears(18) > Today;
            }
            a.QueueNumber = await IssueNumberAsync(a.RoomId!.Value, schedule!.Shift, BookingChannel.Web, ct);
            a.CheckedInAt = Now; a.StatusId = (int)AppointmentStatusId.CheckedIn;
            db.AppointmentStatusHistories.Add(new AppointmentStatusHistory { Appointment = a, OldStatusId = 2, NewStatusId = 3, PerformedBy = staffAccountId, ChangedAt = Now });
            Audit(staffAccountId, a.PatientId, "CHECK_IN_WEB");
            return a.QueueNumber;
        }, ct);

    public Task<Result<string>> RegisterWalkInAsync(int staffAccountId, WalkInReceptionRequest request, CancellationToken ct = default) =>
        WriteAsync(staffAccountId, async nurse =>
        {
            Require(Validator.TryValidateObject(request, new ValidationContext(request), [], true), "Kiểm tra lại thông tin đăng ký.");
            Require(request.IdentityVerified, "Cần đối chiếu danh tính người khám trước khi tiếp nhận.");
            var schedule = await db.DoctorSchedules.Include(s => s.Doctor).SingleOrDefaultAsync(s => s.Id == request.ScheduleId
                && s.Doctor.DepartmentId == nurse.DepartmentId && s.Room.DepartmentId == nurse.DepartmentId
                && s.Doctor.Account.IsActive && s.IsActive, ct);
            Require(schedule != null && schedule.DayOfWeek == Today.DayOfWeek && schedule.EffectiveFrom <= Today
                && (schedule.EffectiveTo == null || schedule.EffectiveTo >= Today)
                && Now >= Today.ToDateTime(schedule.StartTime) && Now <= Today.ToDateTime(schedule.EndTime), "Ca khám không còn tiếp nhận trong khoa này.");
            Require(!await db.ScheduleExceptions.AnyAsync(e => e.DoctorId == schedule!.DoctorId && e.Date == Today
                && e.Status == ScheduleExceptionStatus.Approved && (e.Shift == null || e.Shift == schedule.Shift), ct), "Bác sĩ đã có thay đổi lịch được duyệt.");
            var start = Today.ToDateTime(schedule!.StartTime); var end = Today.ToDateTime(schedule.EndTime);
            var visits = db.Appointments.Where(a => a.DoctorId == schedule.DoctorId && a.StatusId >= 1 && a.StatusId <= 6
                && ((a.Slot != null && a.Slot.StartTime >= start && a.Slot.StartTime < end)
                    || (a.Slot == null && a.CreatedAt >= start && a.CreatedAt <= end)));
            Require(await visits.CountAsync(ct) < schedule.MaxPatients
                && await visits.CountAsync(a => a.BookingChannel == BookingChannel.OnSite, ct) < schedule.MaxPatients - schedule.OnlineSlots,
                "Ca khám đã hết hạn mức khách vãng lai.");
            Patient? patient;
            if (request.PatientId.HasValue)
            {
                patient = await db.Patients.SingleOrDefaultAsync(p => p.Id == request.PatientId, ct);
                Require(patient != null, "Hồ sơ người khám không tồn tại.");
            }
            else
            {
                Require(!string.IsNullOrWhiteSpace(request.FullName) && request.FullName.Trim().Length >= 2
                    && !request.FullName.Contains('<') && !request.FullName.Contains('>'), "Nhập họ tên người khám hợp lệ.");
                Require(request.DateOfBirth.HasValue && request.DateOfBirth <= Today && request.DateOfBirth >= Today.AddYears(-130), "Ngày sinh không hợp lệ.");
                Require(Regex.IsMatch(request.PhoneNumber?.Trim() ?? "", @"^[0-9+ ()-]{9,15}$"), "Số điện thoại không hợp lệ.");
                Require(request.Gender == null || Enum.IsDefined(request.Gender.Value), "Giới tính không hợp lệ.");
                var nationalId = string.IsNullOrWhiteSpace(request.NationalId) ? null : request.NationalId.Trim();
                Require(nationalId == null || !await db.Patients.AnyAsync(p => p.NationalId == nationalId, ct), "CCCD đã có hồ sơ. Hãy tìm và chọn hồ sơ sẵn có.");
                Require(!await db.Patients.AnyAsync(p => p.FullName == request.FullName!.Trim()
                    && p.DateOfBirth == request.DateOfBirth && p.PhoneNumber == request.PhoneNumber!.Trim(), ct), "Người khám đã có hồ sơ. Hãy tìm và chọn hồ sơ sẵn có.");
                patient = new Patient { FullName = request.FullName!.Trim(), DateOfBirth = request.DateOfBirth!.Value,
                    PhoneNumber = request.PhoneNumber!.Trim(), NationalId = nationalId, Address = request.Address?.Trim(), Gender = request.Gender, CreatedAt = Now };
                db.Patients.Add(patient);
            }
            var dayStart = Today.ToDateTime(TimeOnly.MinValue); var dayEnd = dayStart.AddDays(1);
            Require(patient!.Id == 0 || !await db.Appointments.AnyAsync(a => a.PatientId == patient.Id && a.DepartmentId == nurse.DepartmentId
                && a.StatusId >= 1 && a.StatusId <= 5 && ((a.Slot != null && a.Slot.StartTime >= dayStart && a.Slot.StartTime < dayEnd)
                    || a.Slot == null && a.CreatedAt >= dayStart && a.CreatedAt < dayEnd), ct), "Người khám đã có lượt đang hiệu lực hôm nay tại khoa.");
            var a = new Appointment { AppointmentCode = "HIS-" + Today.ToString("yyyyMMdd") + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)),
                Patient = patient, DepartmentId = nurse.DepartmentId, DoctorId = schedule.DoctorId, RoomId = schedule.RoomId,
                BookingChannel = BookingChannel.OnSite, VisitType = VisitType.WalkIn, StatusId = 3, CheckedInAt = Now,
                QueueNumber = await IssueNumberAsync(schedule.RoomId, schedule.Shift, BookingChannel.OnSite, ct),
                PriorityLevel = request.PriorityLevel, Reason = request.Reason?.Trim(), CreatedAt = Now, UpdatedAt = Now };
            db.Appointments.Add(a);
            db.AppointmentStatusHistories.Add(new AppointmentStatusHistory { Appointment = a, NewStatusId = 3, PerformedBy = staffAccountId, ChangedAt = Now });
            await db.SaveChangesAsync(ct);
            Audit(staffAccountId, patient.Id, "REGISTER_WALK_IN");
            return a.QueueNumber;
        }, ct);

    public static bool CanCheckIn(DateTime now, DateTime slot, DateTime shiftEnd, int earlyMinutes) =>
        now.Date == slot.Date && now >= slot.AddMinutes(-earlyMinutes) && now <= shiftEnd;

    private async Task<string> IssueNumberAsync(int roomId, ExamShift shift, BookingChannel channel, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({roomId}, {Today.DayNumber * 10 + (int)shift * 2 + (int)channel})", ct);
        var counter = await db.QueueCounters.SingleOrDefaultAsync(q => q.QueueDate == Today && q.RoomId == roomId && q.Shift == shift && q.Channel == channel, ct);
        if (counter == null) { counter = new QueueCounter { QueueDate = Today, RoomId = roomId, Shift = shift, Channel = channel }; db.QueueCounters.Add(counter); }
        counter.LastNumber++;
        return (channel == BookingChannel.Web ? "O" : "W") + counter.LastNumber.ToString("D2");
    }

    private void Audit(int accountId, int patientId, string action) => db.AccessLogs.Add(new AccessLog { AccountId = accountId, PatientId = patientId, Action = action, OccurredAt = Now });
    private sealed class ReceptionException(string message) : Exception(message);
    private static void Require(bool valid, string message) { if (!valid) throw new ReceptionException(message); }

    private async Task<Result<string>> WriteAsync(int accountId, Func<Nurse, Task<string>> action, CancellationToken ct)
    {
        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var nurse = await NurseAsync(accountId, ct);
                Require(nurse != null, "Tài khoản chưa có quyền tiếp nhận trong khoa.");
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({nurse!.DepartmentId!.Value}, {Today.DayNumber})", ct);
                var number = await action(nurse);
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return Result.Success(number);
            });
        }
        catch (ReceptionException e) { db.ChangeTracker.Clear(); return Result.Failure<string>("Reception", e.Message); }
    }
}
