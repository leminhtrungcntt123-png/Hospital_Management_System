using System.Data;
using HIS.BLL.Common;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public partial class ScheduleChangeService(
    HIS.DAL.AppDbContext db,
    TimeProvider clock,
    INotificationService? notificationService = null) : IScheduleChangeService
{
    public async Task<Result> ProcessDoctorLeaveAsync(int doctorId, DateOnly date, string? shift, int? replacementDoctorId, CancellationToken ct = default)
    {
        if (doctorId <= 0)
        {
            return Result.Failure("INVALID_DOCTOR", "Bác sĩ không hợp lệ.");
        }

        ExamShift? parsedShift = null;
        if (!string.IsNullOrWhiteSpace(shift))
        {
            var normalized = shift.Trim().ToUpperInvariant();
            parsedShift = normalized switch
            {
                "MORNING" or "SÁNG" => ExamShift.Morning,
                "AFTERNOON" or "CHIỀU" => ExamShift.Afternoon,
                _ => null
            };
        }

        var pendingEmails = new List<(string Email, string Subject, string Body)>();

        try
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                pendingEmails.Clear();
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(11028, 1)", ct);

                var doctor = await db.Doctors
                    .Include(d => d.Account)
                    .FirstOrDefaultAsync(d => d.Id == doctorId, ct);
                if (doctor == null)
                {
                    throw new InvalidOperationException("Không tìm thấy bác sĩ.");
                }

                if (replacementDoctorId.HasValue)
                {
                    var repValid = await db.Doctors.AnyAsync(d => d.Id == replacementDoctorId.Value
                        && d.DepartmentId == doctor.DepartmentId
                        && d.Account.IsActive
                        && d.Id != doctorId, ct);
                    if (!repValid)
                    {
                        throw new InvalidOperationException("Bác sĩ thay thế phải đang hoạt động và thuộc cùng khoa.");
                    }
                }

                var change = new ScheduleException
                {
                    DoctorId = doctorId,
                    Doctor = doctor,
                    Date = date,
                    Shift = parsedShift,
                    Type = ScheduleExceptionType.Leave,
                    Reason = "Điều phối nghỉ đột xuất",
                    Status = ScheduleExceptionStatus.Approved,
                    ReplacementDoctorId = replacementDoctorId,
                    CreatedAt = Now,
                    ApprovedAt = Now
                };

                db.ScheduleExceptions.Add(change);
                await MoveAppointmentsAsync(change, doctor.AccountId, pendingEmails, ct);

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            });

            if (notificationService != null)
            {
                foreach (var (email, subject, body) in pendingEmails)
                {
                    await notificationService.SendEmailNotificationAsync(email, subject, body, ct);
                }
            }

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure("SCHEDULE_LEAVE_FAILED", ex.Message);
        }
    }
}
