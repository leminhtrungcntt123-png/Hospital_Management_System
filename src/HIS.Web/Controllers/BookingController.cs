using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace HIS.Web.Controllers;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class BookingController(
    IGuestBookingService booking,
    TimeProvider clock,
    IOptions<BookingOptions> options,
    ILogger<BookingController> logger) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        ViewData["BookingMinDate"] = today.ToString("yyyy-MM-dd");
        ViewData["BookingMaxDate"] = today.AddDays(options.Value.SlotGenerationDaysAhead).ToString("yyyy-MM-dd");
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Slots(int doctorId, DateOnly date, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Ngày khám không hợp lệ." });
        var result = await booking.GetSlotsAsync(doctorId, date, ct);
        return result.IsSuccess ? Json(new { slots = result.Value }) : BadRequest(new { message = result.Error.Message });
    }

    [HttpPost, EnableRateLimiting("guest-booking"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Confirm([FromBody] GuestBookingRequest? request, CancellationToken ct)
    {
        if (!ModelState.IsValid || request == null) return BadRequest(new { message = "Kiểm tra họ tên, ngày sinh và số điện thoại người khám." });
        try
        {
            var result = await booking.ConfirmAsync(request, ct);
            return result.IsSuccess ? Json(new { receipt = result.Value }) : BadRequest(new { message = result.Error.Message });
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError("Guest booking failed: {Type}", error.GetType().Name);
            return StatusCode(503, new { message = "Chưa thể xác nhận lịch. Vui lòng thử lại sau hoặc đến quầy tiếp nhận." });
        }
    }
}
