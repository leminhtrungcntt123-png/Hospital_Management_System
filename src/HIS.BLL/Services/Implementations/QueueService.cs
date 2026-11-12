using HIS.BLL.Common;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class QueueService : IQueueService
{
    // Số ứng viên tối đa thử lần lượt khi ca đầu hàng đã bị bác sĩ khác gọi cùng lúc
    private const int MaxCandidates = 10;

    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public QueueService(AppDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<Result<int>> CallNextPatientAsync(int roomId, int doctorAccountId, CancellationToken ct = default)
    {
        var doctor = await _context.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.AccountId == doctorAccountId, ct);
        var deptHead = await _context.DepartmentHeads.AsNoTracking().FirstOrDefaultAsync(dh => dh.AccountId == doctorAccountId, ct);

        int? departmentId = deptHead?.DepartmentId ?? doctor?.DepartmentId;

        var todayStart = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime).ToDateTime(TimeOnly.MinValue);
        var todayEnd = todayStart.AddDays(1);

        // Truy vấn chọn ứng viên: không tracking để lần đọc lại sau khi lấy khóa luôn lấy dữ liệu mới từ DB.
        // Chỉ lấy lượt khám của HÔM NAY để không gọi nhầm ca tồn từ ngày trước.
        var query = _context.Appointments
            .AsNoTracking()
            .Where(a => a.StatusId == (int)AppointmentStatusId.ReadyForDoctor && a.VitalSigns != null)
            .Where(a => (a.Slot != null && a.Slot.StartTime >= todayStart && a.Slot.StartTime < todayEnd)
                        || (a.Slot == null && a.CreatedAt >= todayStart && a.CreatedAt < todayEnd));

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
            // Mỗi lần thử lại (lỗi tạm thời) bắt đầu với ChangeTracker sạch để không ghi lịch sử hai lần
            _context.ChangeTracker.Clear();
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            // Ưu tiên giảm dần (3 = khẩn cấp nhất), cùng mức ưu tiên thì ai đến trước được gọi trước.
            var candidateIds = await query
                .OrderByDescending(a => a.PriorityLevel)
                .ThenBy(a => a.CheckedInAt ?? a.CreatedAt)
                .ThenBy(a => a.Id)
                .Select(a => a.Id)
                .Take(MaxCandidates)
                .ToListAsync(ct);

            if (candidateIds.Count == 0)
            {
                return Result.Failure<int>("NO_PATIENT", "Hiện không có bệnh nhân nào đủ điều kiện sinh hiệu đang chờ gọi vào phòng khám.");
            }

            foreach (var candidateId in candidateIds)
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({candidateId})", ct);

                // Đọc lại SAU khi có khóa (có tracking) để kiểm tra trạng thái mới nhất.
                var lockedApp = await _context.Appointments
                    .Include(a => a.VitalSigns)
                    .SingleOrDefaultAsync(a => a.Id == candidateId, ct);

                if (lockedApp == null ||
                    lockedApp.StatusId != (int)AppointmentStatusId.ReadyForDoctor ||
                    lockedApp.VitalSigns == null)
                {
                    // Ca này vừa được bác sĩ khác gọi: thử ca kế tiếp trong hàng
                    continue;
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
            }

            return Result.Failure<int>("INVALID_STATE", "Các lượt khám đầu hàng vừa được gọi bởi yêu cầu khác. Vui lòng thử lại.");
        });
    }
}
