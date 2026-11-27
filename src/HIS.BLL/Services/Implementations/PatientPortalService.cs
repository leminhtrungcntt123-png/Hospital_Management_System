using System.Data;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HIS.BLL.Services;

public sealed class PatientPortalService(
    AppDbContext db,
    TimeProvider clock,
    IOptions<BookingOptions> options) : IPatientPortalService
{
    private DateTime Now => DateTime.SpecifyKind(clock.GetLocalNow().DateTime, DateTimeKind.Unspecified);

    public async Task<Result<IReadOnlyList<PatientAppointmentItemDto>>> GetAppointmentsAsync(int accountId, CancellationToken ct = default)
    {
        if (accountId <= 0)
        {
            return Result.Failure<IReadOnlyList<PatientAppointmentItemDto>>("UNAUTHORIZED", "Vui lòng đăng nhập.");
        }

        var linkedPatientIds = await db.AccountPatientProfiles
            .AsNoTracking()
            .Where(l => l.AccountId == accountId && l.Status == LinkStatus.Active)
            .Select(l => l.PatientId)
            .ToListAsync(ct);

        var rows = await db.Appointments
            .AsNoTracking()
            .Where(a => a.BookedBy == accountId || linkedPatientIds.Contains(a.PatientId))
            .OrderByDescending(a => a.Slot != null ? a.Slot.StartTime : a.CreatedAt)
            .Take(100)
            .Select(a => new
            {
                a.Id,
                a.AppointmentCode,
                PatientName = a.Patient.FullName,
                DoctorName = a.Doctor != null ? a.Doctor.FullName : "Chưa phân công",
                DepartmentName = a.Department != null ? a.Department.Name : "—",
                RoomNumber = a.Room != null ? a.Room.RoomNumber : "—",
                StartTime = a.Slot != null ? (DateTime?)a.Slot.StartTime : null,
                EndTime = a.Slot != null ? (DateTime?)a.Slot.EndTime : null,
                a.StatusId,
                a.Reason,
                a.CreatedAt
            })
            .ToListAsync(ct);

        var cutoff = Now.AddMinutes(options.Value.CancelCutoffMinutes);
        var items = rows.Select(a =>
        {
            var canCancel = a.StatusId is (int)AppointmentStatusId.Pending or (int)AppointmentStatusId.Confirmed or (int)AppointmentStatusId.RescheduleRequired
                && (a.StatusId == (int)AppointmentStatusId.RescheduleRequired || !a.StartTime.HasValue || a.StartTime.Value > cutoff);

            return new PatientAppointmentItemDto(
                a.Id,
                a.AppointmentCode,
                a.PatientName,
                a.DoctorName,
                a.DepartmentName,
                a.RoomNumber,
                a.StartTime,
                a.EndTime,
                a.StatusId,
                StatusLabel(a.StatusId),
                a.Reason,
                canCancel,
                a.CreatedAt);
        }).ToList();

        return Result.Success<IReadOnlyList<PatientAppointmentItemDto>>(items);
    }

    public async Task<Result> CancelAppointmentAsync(int accountId, int appointmentId, CancellationToken ct = default)
    {
        if (accountId <= 0 || appointmentId <= 0)
        {
            return Result.Failure("INVALID_INPUT", "Thông tin lịch hẹn không hợp lệ.");
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            var appointment = await db.Appointments
                .Include(a => a.Slot)
                .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);

            if (appointment == null)
            {
                return Result.Failure("NOT_FOUND", "Không tìm thấy lịch hẹn.");
            }

            var hasAccess = appointment.BookedBy == accountId || await db.AccountPatientProfiles
                .AnyAsync(l => l.AccountId == accountId && l.PatientId == appointment.PatientId && l.Status == LinkStatus.Active, ct);

            if (!hasAccess)
            {
                return Result.Failure("FORBIDDEN", "Bạn không có quyền thao tác trên lịch hẹn này.");
            }

            var currentStatus = (AppointmentStatusId)appointment.StatusId;
            if (!AppointmentStateMachine.CanTransition(currentStatus, AppointmentStatusId.Cancelled))
            {
                return Result.Failure("INVALID_STATUS", "Trạng thái hiện tại của lịch hẹn không thể hủy.");
            }

            if (currentStatus != AppointmentStatusId.RescheduleRequired
                && appointment.Slot != null
                && appointment.Slot.StartTime <= Now.AddMinutes(options.Value.CancelCutoffMinutes))
            {
                return Result.Failure("CANCEL_CUTOFF",
                    $"Chỉ có thể hủy lịch trước giờ khám tối thiểu {options.Value.CancelCutoffMinutes} phút.");
            }

            var oldStatusId = appointment.StatusId;
            appointment.StatusId = (int)AppointmentStatusId.Cancelled;
            appointment.HoldUntil = null;
            appointment.UpdatedAt = Now;

            db.AppointmentStatusHistories.Add(new AppointmentStatusHistory
            {
                AppointmentId = appointment.Id,
                OldStatusId = oldStatusId,
                NewStatusId = (int)AppointmentStatusId.Cancelled,
                PerformedBy = accountId,
                ChangedAt = Now
            });

            if (appointment.SlotId.HasValue)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE appointment_slots
                    SET booked_count = GREATEST(0, booked_count - 1),
                        status = CASE
                            WHEN status = 'FULL' AND GREATEST(0, booked_count - 1) < capacity THEN 'AVAILABLE'
                            ELSE status
                        END,
                        version = version + 1
                    WHERE id = {appointment.SlotId.Value}
                    """, ct);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result.Success();
        });
    }

    public async Task<Result<PatientNotificationsDto>> GetNotificationsAsync(int accountId, CancellationToken ct = default)
    {
        if (accountId <= 0)
        {
            return Result.Failure<PatientNotificationsDto>("UNAUTHORIZED", "Vui lòng đăng nhập.");
        }

        var items = await db.Notifications
            .AsNoTracking()
            .Where(n => n.AccountId == accountId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new PatientNotificationItemDto(n.Id, n.AppointmentId, n.Title, n.Content, n.IsRead, n.CreatedAt))
            .ToListAsync(ct);

        var unreadCount = await db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.AccountId == accountId && !n.IsRead, ct);

        return Result.Success(new PatientNotificationsDto(unreadCount, items));
    }

    public async Task<Result> MarkNotificationsReadAsync(int accountId, int? notificationId = null, CancellationToken ct = default)
    {
        if (accountId <= 0)
        {
            return Result.Failure("UNAUTHORIZED", "Vui lòng đăng nhập.");
        }

        var query = db.Notifications.Where(n => n.AccountId == accountId && !n.IsRead);
        if (notificationId.HasValue)
        {
            query = query.Where(n => n.Id == notificationId.Value);
        }

        await query.ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, true), ct);
        return Result.Success();
    }

    private static string StatusLabel(int statusId) => (AppointmentStatusId)statusId switch
    {
        AppointmentStatusId.Pending => "Chờ xác nhận",
        AppointmentStatusId.Confirmed => "Đã xác nhận",
        AppointmentStatusId.CheckedIn => "Đã tiếp nhận",
        AppointmentStatusId.ReadyForDoctor => "Chờ bác sĩ",
        AppointmentStatusId.InProgress => "Đang khám",
        AppointmentStatusId.Completed => "Đã hoàn thành",
        AppointmentStatusId.Cancelled => "Đã hủy",
        AppointmentStatusId.NoShow => "Không đến",
        AppointmentStatusId.Expired => "Hết hạn",
        AppointmentStatusId.RescheduleRequired => "Cần đặt lại lịch",
        _ => "Không xác định"
    };
}
