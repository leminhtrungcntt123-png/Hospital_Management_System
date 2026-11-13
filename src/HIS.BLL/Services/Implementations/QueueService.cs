using HIS.BLL.Common;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class QueueService : IQueueService
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public QueueService(AppDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<Result<string>> CheckInAndIssueQueueNumberAsync(int appointmentId, int staffAccountId, CancellationToken ct = default)
    {
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            var app = await _context.Appointments
                .Include(a => a.Slot)
                .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);
            if (app == null) return Result.Failure<string>("NOT_FOUND", "Không tìm thấy lượt hẹn.");

            var oldStatus = (AppointmentStatusId)app.StatusId;
            if (!AppointmentStateMachine.CanTransition(oldStatus, AppointmentStatusId.CheckedIn))
            {
                return Result.Failure<string>("INVALID_STATUS", "Trạng thái lịch hẹn không hợp lệ để tiếp nhận check-in.");
            }

            var now = DateTime.SpecifyKind(_timeProvider.GetLocalNow().DateTime, DateTimeKind.Unspecified);
            var today = DateOnly.FromDateTime(now);
            app.StatusId = (int)AppointmentStatusId.CheckedIn;
            app.CheckedInAt = now;
            app.UpdatedAt = now;

            if (string.IsNullOrEmpty(app.QueueNumber))
            {
                var roomId = app.RoomId
                    ?? (app.DoctorId.HasValue
                        ? await _context.DoctorSchedules
                            .Where(s => s.DoctorId == app.DoctorId.Value
                                && s.WorkDate == today
                                && s.Status == DoctorScheduleStatus.Approved
                                && s.RoomId.HasValue)
                            .Select(s => s.RoomId!.Value)
                            .FirstOrDefaultAsync(ct)
                        : 0);

                if (roomId <= 0)
                {
                    roomId = await _context.ClinicRooms
                        .Where(r => r.IsActive && (app.DepartmentId == null || r.DepartmentId == app.DepartmentId))
                        .Select(r => r.Id)
                        .FirstOrDefaultAsync(ct);
                    if (roomId <= 0) roomId = 1;
                }
                var shift = (app.Slot != null && app.Slot.StartTime.Hour >= 12) ? ExamShift.Afternoon : ExamShift.Morning;
                var channel = app.BookingChannel;

                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({roomId}, {today.DayNumber * 10 + (int)shift * 2 + (int)channel})",
                    ct);

                var counter = await _context.QueueCounters
                    .SingleOrDefaultAsync(q => q.QueueDate == today
                        && q.RoomId == roomId
                        && q.Shift == shift
                        && q.Channel == channel, ct);

                if (counter == null)
                {
                    counter = new QueueCounter
                    {
                        QueueDate = today,
                        RoomId = roomId,
                        Shift = shift,
                        Channel = channel,
                        LastNumber = 0
                    };
                    _context.QueueCounters.Add(counter);
                }

                counter.LastNumber++;
                app.QueueNumber = (channel == BookingChannel.Web ? "O" : "W") + counter.LastNumber.ToString("D2");
            }

            _context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
            {
                AppointmentId = app.Id,
                OldStatusId = (int)oldStatus,
                NewStatusId = (int)AppointmentStatusId.CheckedIn,
                PerformedBy = staffAccountId,
                ChangedAt = now
            });

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Result<string>.Success(app.QueueNumber);
        });
    }

    public async Task<Result<int>> CallNextPatientAsync(int roomId, int doctorAccountId, CancellationToken ct = default)
    {
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.AccountId == doctorAccountId, ct);
            var deptHead = await _context.DepartmentHeads.FirstOrDefaultAsync(dh => dh.AccountId == doctorAccountId, ct);

            int? departmentId = deptHead?.DepartmentId ?? doctor?.DepartmentId;
            int lockTarget = departmentId ?? (roomId > 0 ? roomId : 1);

            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(11029, {lockTarget})", ct);

            var query = _context.Appointments
                .Include(a => a.Patient)
                .Where(a => a.StatusId == (int)AppointmentStatusId.ReadyForDoctor);

            if (doctor != null)
            {
                query = query.Where(a => a.DoctorId == doctor.Id);
                if (roomId > 0)
                {
                    query = query.Where(a => a.RoomId == roomId);
                }
            }
            else if (departmentId.HasValue && departmentId.Value > 0)
            {
                query = query.Where(a => a.DepartmentId == departmentId.Value || (a.Doctor != null && a.Doctor.DepartmentId == departmentId.Value));
            }
            else if (roomId > 0)
            {
                query = query.Where(a => a.RoomId == roomId);
            }

            var nextApp = await query
                .OrderByDescending(a => a.PriorityLevel)
                .ThenBy(a => a.QueueNumber)
                .ThenBy(a => a.CheckedInAt)
                .FirstOrDefaultAsync(ct);

            if (nextApp == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<int>("NO_PATIENT", "Hiện không có bệnh nhân nào đang chờ gọi vào phòng khám.");
            }

            var now = DateTime.SpecifyKind(_timeProvider.GetLocalNow().DateTime, DateTimeKind.Unspecified);
            var oldStatus = nextApp.StatusId;
            nextApp.StatusId = (int)AppointmentStatusId.InProgress;
            nextApp.UpdatedAt = now;

            _context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
            {
                AppointmentId = nextApp.Id,
                OldStatusId = oldStatus,
                NewStatusId = (int)AppointmentStatusId.InProgress,
                PerformedBy = doctorAccountId,
                ChangedAt = now
            });

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            int displayNum = nextApp.Id;
            if (!string.IsNullOrWhiteSpace(nextApp.QueueNumber))
            {
                var digits = new string(nextApp.QueueNumber.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var parsed)) displayNum = parsed;
            }

            return Result<int>.Success(displayNum);
        });
    }
}
