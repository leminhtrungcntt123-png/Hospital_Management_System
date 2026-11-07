using System.Data;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class ScheduleChangeService(AppDbContext db, TimeProvider clock) : IScheduleChangeService
{
    private DateTime Now => DateTime.SpecifyKind(clock.GetLocalNow().DateTime, DateTimeKind.Unspecified);

    public Task<Result> ProcessDoctorLeaveAsync(int doctorId, DateOnly date, string? shift, int? replacementDoctorId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public async Task<IReadOnlyList<ScheduleRequestRow>> GetRequestsAsync(int actorId, CancellationToken ct = default)
    {
        await AdminRules.RequireAdminAsync(db, actorId, ct);
        var requests = await db.ScheduleExceptions.AsNoTracking()
            .Include(request => request.Doctor).ThenInclude(doctor => doctor.Department)
            .Include(request => request.ReplacementDoctor)
            .OrderBy(request => request.Status != ScheduleExceptionStatus.Pending)
            .ThenByDescending(request => request.Date)
            .Take(200).ToListAsync(ct);

        var doctors = await db.Doctors.AsNoTracking()
            .Where(doctor => doctor.Account.IsActive)
            .OrderBy(doctor => doctor.FullName).ToListAsync(ct);

        return requests.Select(request => new ScheduleRequestRow(
            request.Id, request.Doctor.FullName, request.Doctor.Department.Name,
            request.Date, ShiftName(request.Shift), TypeName(request.Type), request.Reason,
            request.Status.ToString(), request.ReplacementDoctor?.FullName,
            request.Status == ScheduleExceptionStatus.Pending && StartOfChange(request) > Now,
            request.Status == ScheduleExceptionStatus.Pending,
            doctors.Where(doctor => doctor.Id != request.DoctorId && doctor.DepartmentId == request.Doctor.DepartmentId)
                .Select(doctor => new AdminOption(doctor.Id, doctor.FullName)).ToList())).ToList();
    }

    public async Task<ScheduleDecisionResult> ReviewAsync(
        int actorId, ScheduleDecisionRequest request, CancellationToken ct = default)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            await AdminRules.RequireAdminAsync(db, actorId, ct);
            if (db.Database.IsRelational())
            {
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(11028, 1)", ct);
            }

            var change = await db.ScheduleExceptions.Include(c => c.Doctor)
                .SingleOrDefaultAsync(c => c.Id == request.ExceptionId, ct);
            AdminRules.Require(change != null, "Không tìm thấy đề nghị nghỉ/đổi ca.");
            AdminRules.Require(change!.Status == ScheduleExceptionStatus.Pending, "Đề nghị đã được xử lý; hãy tải lại danh sách.");

            var result = new ScheduleDecisionResult(0, 0, 0);

            if (request.Approve)
            {
                AdminRules.Require(StartOfChange(change) > Now, "Không thay đổi ca đã bắt đầu hoặc đã qua.");
                AdminRules.Require(!await db.ScheduleExceptions.AnyAsync(other => other.Id != change.Id
                    && other.DoctorId == change.DoctorId && other.Date == change.Date
                    && other.Status == ScheduleExceptionStatus.Approved
                    && (other.Shift == null || change.Shift == null || other.Shift == change.Shift), ct),
                    "Ca này đã có điều chỉnh được duyệt.");
                var replacementId = request.ReplacementDoctorId ?? change.ReplacementDoctorId;
                if (replacementId.HasValue)
                {
                    AdminRules.Require(replacementId != change.DoctorId
                        && await db.Doctors.AnyAsync(doctor => doctor.Id == replacementId
                            && doctor.DepartmentId == change.Doctor.DepartmentId && doctor.Account.IsActive, ct),
                        "Bác sĩ thay thế phải đang hoạt động và thuộc cùng khoa.");
                }

                change.ReplacementDoctorId = replacementId;
                result = await MoveAppointmentsAsync(change, actorId, ct);
                change.Status = ScheduleExceptionStatus.Approved;
            }
            else
            {
                change.Status = ScheduleExceptionStatus.Rejected;
            }

            change.ApprovedBy = actorId;
            change.ApprovedAt = Now;
            db.AccessLogs.Add(new AccessLog
            {
                AccountId = actorId,
                OccurredAt = Now,
                Action = request.Approve ? "ADMIN_APPROVE_SCHEDULE" : "ADMIN_REJECT_SCHEDULE"
            });

            await db.SaveChangesAsync(ct);
            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }
            return result;
        });
    }

    private async Task<ScheduleDecisionResult> MoveAppointmentsAsync(ScheduleException change, int actorId, CancellationToken ct)
    {
        var day = change.Date.ToDateTime(TimeOnly.MinValue);
        var end = day.AddDays(1);
        var afternoon = day.AddHours(12);
        var from = change.Shift == ExamShift.Afternoon ? afternoon : day;
        var until = change.Shift == ExamShift.Morning ? afternoon : end;

        var slots = await db.AppointmentSlots
            .Where(slot => slot.DoctorId == change.DoctorId && slot.StartTime >= from && slot.StartTime < until)
            .Include(slot => slot.Appointments).ThenInclude(appointment => appointment.Patient)
            .OrderBy(slot => slot.StartTime).ToListAsync(ct);

        var hasReceivedVisits = await db.Appointments.AnyAsync(appointment =>
            appointment.DoctorId == change.DoctorId
            && appointment.StatusId >= 3 && appointment.StatusId <= 6
            && ((appointment.Slot != null && appointment.Slot.StartTime >= from && appointment.Slot.StartTime < until)
                || (appointment.Slot == null && appointment.CreatedAt >= from && appointment.CreatedAt < until)), ct);

        AdminRules.Require(!hasReceivedVisits,
            "Ca đã có người khám được tiếp nhận; cần xử lý riêng trước khi điều chỉnh.");

        var moved = 0;
        var rebook = 0;
        var expired = 0;

        foreach (var source in slots)
        {
            foreach (var appointment in source.Appointments.ToList())
            {
                if (appointment.StatusId == (int)AppointmentStatusId.Pending)
                {
                    ChangeStatus(appointment, AppointmentStatusId.Expired, actorId);
                    appointment.HoldUntil = null;
                    Notify(appointment, "Giữ chỗ đã được giải phóng do bác sĩ thay đổi ca. Vui lòng chọn lịch mới.");
                    expired++;
                    continue;
                }

                if (appointment.StatusId != (int)AppointmentStatusId.Confirmed)
                {
                    continue;
                }

                var candidates = db.AppointmentSlots.AsNoTracking()
                    .Where(slot => slot.DoctorId != change.DoctorId && slot.Doctor.DepartmentId == change.Doctor.DepartmentId
                        && slot.Doctor.Account.IsActive && slot.StartTime >= from && slot.StartTime < until
                        && slot.StartTime > Now && slot.Status == SlotStatus.Available && slot.BookedCount < slot.Capacity
                        && !db.Appointments.Any(existing => existing.PatientId == appointment.PatientId
                            && existing.SlotId == slot.Id && existing.StatusId >= 1 && existing.StatusId <= 5)
                        && (!change.ReplacementDoctorId.HasValue || slot.DoctorId == change.ReplacementDoctorId)
                        && !db.ScheduleExceptions.Any(exception => exception.Id != change.Id
                            && exception.DoctorId == slot.DoctorId && exception.Date == change.Date
                            && exception.Status == ScheduleExceptionStatus.Approved
                            && (exception.Shift == null || exception.Shift == (slot.StartTime.Hour < 12 ? ExamShift.Morning : ExamShift.Afternoon)))
                        && db.DoctorSchedules.Any(schedule => schedule.DoctorId == slot.DoctorId && schedule.RoomId == slot.RoomId
                            && schedule.IsActive && schedule.DayOfWeek == change.Date.DayOfWeek
                            && schedule.EffectiveFrom <= change.Date && (schedule.EffectiveTo == null || schedule.EffectiveTo >= change.Date)
                            && schedule.StartTime <= TimeOnly.FromDateTime(slot.StartTime) && schedule.EndTime >= TimeOnly.FromDateTime(slot.EndTime)))
                    .OrderBy(slot => slot.StartTime == source.StartTime ? 0 : 1)
                    .ThenBy(slot => slot.BookedCount).ThenBy(slot => slot.StartTime);

                var target = await candidates.FirstOrDefaultAsync(ct);

                if (target == null)
                {
                    appointment.Slot = null;
                    appointment.SlotId = null;
                    appointment.DoctorId = null;
                    appointment.RoomId = null;
                    ChangeStatus(appointment, AppointmentStatusId.RescheduleRequired, actorId);
                    Notify(appointment, "Không còn slot thay thế trong khoa. Vui lòng chọn lịch khám mới.");
                    rebook++;
                }
                else
                {
                    if (db.Database.IsRelational())
                    {
                        var reserved = await db.Database.ExecuteSqlInterpolatedAsync($"""
                            UPDATE appointment_slots
                            SET booked_count = booked_count + 1,
                                version = version + 1,
                                status = CASE WHEN booked_count + 1 >= capacity THEN 'FULL' ELSE 'AVAILABLE' END
                            WHERE id = {target.Id} AND booked_count < capacity AND status = 'AVAILABLE'
                            """, ct);
                        AdminRules.Require(reserved == 1, "Slot thay thế vừa hết chỗ. Vui lòng thử lại.");
                    }
                    else
                    {
                        var slotToUpdate = await db.AppointmentSlots.FindAsync([target.Id], ct);
                        if (slotToUpdate == null || slotToUpdate.BookedCount >= slotToUpdate.Capacity || slotToUpdate.Status != SlotStatus.Available)
                        {
                            throw new AdminOperationException("Slot thay thế vừa hết chỗ. Vui lòng thử lại.");
                        }
                        slotToUpdate.BookedCount++;
                        slotToUpdate.Version++;
                        if (slotToUpdate.BookedCount >= slotToUpdate.Capacity)
                            slotToUpdate.Status = SlotStatus.Full;
                    }

                    appointment.Slot = await db.AppointmentSlots.FindAsync([target.Id], ct);
                    appointment.DoctorId = target.DoctorId;
                    appointment.RoomId = target.RoomId;
                    db.AppointmentStatusHistories.Add(new AppointmentStatusHistory
                    {
                        Appointment = appointment,
                        OldStatusId = 2,
                        NewStatusId = 2,
                        PerformedBy = actorId,
                        ChangedAt = Now
                    });
                    Notify(appointment, $"Lịch khám đã chuyển sang bác sĩ khác trong khoa lúc {target.StartTime:HH:mm dd/MM/yyyy}.");
                    moved++;
                }

                await db.SaveChangesAsync(ct);
            }

            // Keep referenced slots for historical dates; prevent further bookings into the changed shift.
            source.BookedCount = 0;
            source.OnlineBooked = 0;
            source.Status = SlotStatus.Blocked;
            source.Version++;
        }

        return new ScheduleDecisionResult(moved, rebook, expired);
    }

    private void ChangeStatus(Appointment appointment, AppointmentStatusId next, int actorId)
    {
        AdminRules.Require(AppointmentStateMachine.CanTransition((AppointmentStatusId)appointment.StatusId, next),
            "Trạng thái lịch hẹn đã thay đổi.");
        db.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            Appointment = appointment,
            OldStatusId = appointment.StatusId,
            NewStatusId = (int)next,
            PerformedBy = actorId,
            ChangedAt = Now
        });
        appointment.StatusId = (int)next;
    }

    private void Notify(Appointment appointment, string content)
    {
        if (appointment.BookedBy.HasValue)
        {
            db.Notifications.Add(new Notification
            {
                AccountId = appointment.BookedBy.Value,
                Appointment = appointment,
                Title = "Điều chỉnh lịch khám",
                Content = (appointment.Patient?.FullName ?? "Bệnh nhân") + ": " + content,
                CreatedAt = Now
            });
        }
    }

    private static DateTime StartOfChange(ScheduleException change) =>
        change.Date.ToDateTime(change.Shift == ExamShift.Afternoon ? new TimeOnly(13, 0) : new TimeOnly(7, 30));

    private static string ShiftName(ExamShift? shift) => shift == null ? "Cả ngày"
        : shift == ExamShift.Morning ? "Sáng" : "Chiều";

    private static string TypeName(ScheduleExceptionType type) => type switch
    {
        ScheduleExceptionType.Leave => "Nghỉ phép",
        ScheduleExceptionType.Training => "Đào tạo",
        ScheduleExceptionType.Surgery => "Phẫu thuật",
        _ => "Đổi ca"
    };
}

