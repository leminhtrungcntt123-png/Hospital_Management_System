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

    public async Task<Result<int>> CallNextPatientAsync(int roomId, int doctorAccountId, CancellationToken ct = default)
    {
        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.AccountId == doctorAccountId, ct);
        var deptHead = await _context.DepartmentHeads.FirstOrDefaultAsync(dh => dh.AccountId == doctorAccountId, ct);

        int? departmentId = deptHead?.DepartmentId ?? doctor?.DepartmentId;

        // Truy vấn chọn ứng viên: không tracking để lần đọc lại sau khi lấy khóa luôn lấy dữ liệu mới từ DB.
        var query = _context.Appointments
            .AsNoTracking()
            .Where(a => a.StatusId == (int)AppointmentStatusId.ReadyForDoctor && a.VitalSigns != null);

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

        return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            // Ưu tiên giảm dần: 3 = khẩn cấp nhất.
            var nextAppId = await query
                .OrderByDescending(a => a.PriorityLevel)
                .ThenBy(a => a.QueueNumber)
                .ThenBy(a => a.CheckedInAt)
                .Select(a => (int?)a.Id)
                .FirstOrDefaultAsync(ct);

            if (nextAppId == null)
            {
                return Result.Failure<int>("NO_PATIENT", "Hiện không có bệnh nhân nào đủ điều kiện sinh hiệu đang chờ gọi vào phòng khám.");
            }

            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({nextAppId.Value})", ct);

            // Đọc lại SAU khi có khóa (có tracking) để kiểm tra trạng thái mới nhất.
            var lockedApp = await _context.Appointments
                .Include(a => a.VitalSigns)
                .SingleOrDefaultAsync(a => a.Id == nextAppId.Value, ct);

            if (lockedApp == null ||
                lockedApp.StatusId != (int)AppointmentStatusId.ReadyForDoctor ||
                lockedApp.VitalSigns == null)
            {
                return Result.Failure<int>("INVALID_STATE", "Lượt khám đã được xử lý bởi yêu cầu khác.");
            }

            var now = _timeProvider.GetLocalNow().DateTime;
            lockedApp.StatusId = (int)AppointmentStatusId.InProgress;
            lockedApp.CalledAt = now;
            lockedApp.UpdatedAt = now;

            _context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
            {
                AppointmentId = lockedApp.Id,
                OldStatusId = (int)AppointmentStatusId.ReadyForDoctor,
                NewStatusId = (int)AppointmentStatusId.InProgress,
                PerformedBy = doctorAccountId,
                ChangedAt = now
            });

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            int displayNum = lockedApp.Id;
            if (!string.IsNullOrWhiteSpace(lockedApp.QueueNumber))
            {
                var digits = new string(lockedApp.QueueNumber.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var parsed)) displayNum = parsed;
            }

            return Result<int>.Success(displayNum);
        });
    }
}
