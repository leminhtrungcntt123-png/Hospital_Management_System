using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.Web.Helpers;
using HIS.Web.ViewModels.Booking;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Controllers;

/// <summary>
/// Màn "Lịch khám của tôi": bệnh nhân xem lại các lượt hẹn mình đặt trên web và tự hủy
/// khi còn trong hạn mức. Mọi kiểm tra nghiệp vụ (quyền, hạn mức hủy muộn, tài khoản bị chặn)
/// nằm trong BookingService nên không lặp lại ở đây.
/// </summary>
public class LichKhamController : PatientPageController
{
    private readonly IBookingService _bookingService;

    public LichKhamController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (RequireAccount() is { } signIn) return signIn;

        var appointments = await _bookingService.GetAppointmentsByAccountAsync(AccountId);

        return View(new MyAppointmentsViewModel
        {
            Appointments = appointments.Select(ToVm).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> HuyLich(int appointmentId)
    {
        if (RequireAccount() is { } signIn) return signIn;

        var result = await _bookingService.CancelAppointmentAsync(appointmentId, AccountId);
        TempData[result.Success ? "Message" : "Error"] = result.Message;

        return RedirectToAction(nameof(Index));
    }

    private static AppointmentVm ToVm(BookingAppointmentDto a) => new()
    {
        Id = a.Id,
        Code = a.Code,
        Status = a.Status,
        StatusLabel = a.StatusLabel,
        IsCancellable = a.IsCancellable,
        PatientName = a.PatientName,
        DepartmentName = a.DepartmentName,
        DateLabel = a.SlotStartTime is { } start ? VietnameseFormat.FullDate(DateOnly.FromDateTime(start)) : "Chưa xác định",
        TimeLabel = a.SlotStartTime is { } s && a.SlotEndTime is { } e ? $"{s:HH:mm} – {e:HH:mm}" : string.Empty,
        DoctorName = a.DoctorName,
        Room = a.RoomName,
        QueueNumber = a.QueueNumber
    };
}
