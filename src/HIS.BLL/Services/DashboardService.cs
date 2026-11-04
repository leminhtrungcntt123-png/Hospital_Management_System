using HIS.DAL;
using HIS.Domain;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _context;

    public DashboardService(AppDbContext context)
    {
        _context = context;
    }

    // Thống kê tổng quan hệ thống (bệnh nhân, bác sĩ, khoa phòng, lịch hẹn trong ngày)
    public async Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        return new DashboardStatsDto
        {
            TotalPatients = await _context.Patients.CountAsync(cancellationToken),
            TotalDoctors = await _context.Doctors.CountAsync(cancellationToken),
            TotalDepartments = await _context.Departments.CountAsync(cancellationToken),
            TodayAppointments = await _context.Appointments
                .CountAsync(a => a.Slot != null && a.Slot.StartTime >= today && a.Slot.StartTime < tomorrow, cancellationToken)
        };
    }

    // Lấy danh sách lượt khám trong ngày hôm nay
    public async Task<IReadOnlyList<TodayAppointmentItemDto>> GetTodayAppointmentsAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        // Đọc giá trị thô trước, dịch nhãn sau: EF không dịch được extension trong Select()
        var rows = await _context.Appointments
            .AsNoTracking()
            .Where(a => a.Slot != null && a.Slot.StartTime >= today && a.Slot.StartTime < tomorrow)
            .OrderBy(a => a.Slot!.StartTime)
            .Select(a => new
            {
                a.AppointmentCode,
                a.Patient.FullName,
                StartTime = a.Slot!.StartTime,
                StatusName = a.Status.Name,
                a.BookingChannel
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new TodayAppointmentItemDto(
                r.AppointmentCode,
                r.FullName,
                r.StartTime,
                r.StatusName,
                r.BookingChannel.ToLabel()))
            .ToList();
    }
}