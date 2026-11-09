using HIS.BLL.Common;
using HIS.BLL.Interfaces;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class QueueService : IQueueService
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public QueueService(AppDbContext context, TimeProvider? timeProvider = null)
    {
        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Thực hiện tiếp nhận (check-in) và phát số thứ tự khám cho người bệnh theo thứ tự tiếp nhận trong ngày.
    /// </summary>
    /// <param name="appointmentId">Mã lượt khám.</param>
    /// <param name="staffAccountId">Mã tài khoản nhân viên tiếp nhận.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    /// <returns>Số thứ tự khám (STT).</returns>
    public async Task<Result<string>> CheckInAndIssueQueueNumberAsync(int appointmentId, int staffAccountId, CancellationToken ct = default)
    {
        var app = await _context.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId, ct);
        if (app == null) return Result.Failure<string>("NOT_FOUND", "Không tìm thấy lượt hẹn.");

        if (!AppointmentStateMachine.CanTransition((AppointmentStatusId)app.StatusId, AppointmentStatusId.CheckedIn))
        {
            return Result.Failure<string>("INVALID_STATUS", "Trạng thái lịch hẹn không hợp lệ để tiếp nhận check-in.");
        }

        var now = DateTime.SpecifyKind(_timeProvider.GetLocalNow().DateTime, DateTimeKind.Unspecified);
        app.StatusId = (int)AppointmentStatusId.CheckedIn;
        app.CheckedInAt = now;
        app.UpdatedAt = now;

        if (string.IsNullOrEmpty(app.QueueNumber))
        {
            var startOfDay = now.Date;
            var endOfDay = startOfDay.AddDays(1);
            var countToday = await _context.Appointments.CountAsync(a =>
                a.DepartmentId == app.DepartmentId &&
                a.Id != app.Id &&
                a.CheckedInAt.HasValue &&
                a.CheckedInAt.Value >= startOfDay &&
                a.CheckedInAt.Value < endOfDay &&
                !string.IsNullOrEmpty(a.QueueNumber), ct);
            app.QueueNumber = (countToday + 1).ToString("D3");
        }

        await _context.SaveChangesAsync(ct);
        return Result<string>.Success(app.QueueNumber);
    }

    /// <summary>
    /// Bác sĩ gọi bệnh nhân kế tiếp vào phòng khám theo độ ưu tiên và số thứ tự, chuyển trạng thái sang InProgress.
    /// </summary>
    /// <param name="roomId">Mã phòng khám.</param>
    /// <param name="doctorAccountId">Mã tài khoản bác sĩ gọi khám.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    /// <returns>Số thứ tự của bệnh nhân vừa được gọi vào khám.</returns>
    public async Task<Result<int>> CallNextPatientAsync(int roomId, int doctorAccountId, CancellationToken ct = default)
    {
        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.AccountId == doctorAccountId, ct);
        var deptHead = await _context.DepartmentHeads.FirstOrDefaultAsync(dh => dh.AccountId == doctorAccountId, ct);

        int? departmentId = deptHead?.DepartmentId ?? doctor?.DepartmentId;

        var query = _context.Appointments
            .Include(a => a.Patient)
            .Where(a => a.StatusId == (int)AppointmentStatusId.ReadyForDoctor || a.StatusId == (int)AppointmentStatusId.CheckedIn);

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
            .OrderBy(a => a.PriorityLevel)
            .ThenBy(a => a.QueueNumber)
            .ThenBy(a => a.CheckedInAt)
            .FirstOrDefaultAsync(ct);

        if (nextApp == null)
        {
            return Result.Failure<int>("NO_PATIENT", "Hiện không có bệnh nhân nào đang chờ gọi vào phòng khám.");
        }

        var now = DateTime.SpecifyKind(_timeProvider.GetLocalNow().DateTime, DateTimeKind.Unspecified);
        nextApp.StatusId = (int)AppointmentStatusId.InProgress;
        nextApp.UpdatedAt = now;

        await _context.SaveChangesAsync(ct);

        int displayNum = nextApp.Id;
        if (!string.IsNullOrWhiteSpace(nextApp.QueueNumber))
        {
            var digits = new string(nextApp.QueueNumber.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var parsed)) displayNum = parsed;
        }

        return Result<int>.Success(displayNum);
    }
}

