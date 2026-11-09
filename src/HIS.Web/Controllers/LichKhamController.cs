using HIS.BLL.Interfaces;
using HIS.Web.Helpers;
using HIS.Web.Realtime;
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
    private readonly QueueChangeNotifier _realtime;

    public LichKhamController(IBookingService bookingService, QueueChangeNotifier realtime)
    {
        _bookingService = bookingService;
        _realtime = realtime;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (RequireAccount() is { } signIn) return signIn;

        var appointments = await _bookingService.GetAppointmentsByAccountAsync(AccountId);

        return View(new MyAppointmentsViewModel
        {
            Appointments = appointments.Select(a => a.ToVm()).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> HuyLich(int appointmentId)
    {
        if (RequireAccount() is { } signIn) return signIn;

        var result = await _bookingService.CancelAppointmentAsync(appointmentId, AccountId);
        TempData[result.Success ? "Message" : "Error"] = result.Message;

        if (result.Success)
        {
            // Suất vừa nhả ra và số thứ tự đổi chỗ -> báo quầy nạp lại.
            _realtime.Publish(QueueChangeKinds.AppointmentCancelled);
        }

        return RedirectToAction(nameof(Index));
    }
}
