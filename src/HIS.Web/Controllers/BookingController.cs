using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HIS.Web.Controllers;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class BookingController(
    IGuestBookingService booking,
    TimeProvider clock,
    IOptions<BookingOptions> options,
    IEmailSender emailSender,
    IMemoryCache cache,
    IWebHostEnvironment env,
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
    public async Task<IActionResult> AvailableDates(int doctorId, int days = 7, CancellationToken ct = default)
    {
        if (doctorId <= 0) return BadRequest(new { message = "Bác sĩ không hợp lệ." });
        try
        {
            var result = await booking.GetAvailableDatesAsync(doctorId, days, ct);
            if (!result.IsSuccess) return BadRequest(new { message = result.Error.Message });
            var dates = result.Value!.Select(d => d.ToString("yyyy-MM-dd")).ToList();
            return Json(new { dates });
        }
        catch (OperationCanceledException)
        {
            return new EmptyResult();
        }
    }

    [HttpGet]
    public async Task<IActionResult> Slots(int doctorId, DateOnly date, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Ngày khám không hợp lệ." });
        try
        {
            var result = await booking.GetSlotsAsync(doctorId, date, ct);
            return result.IsSuccess ? Json(new { slots = result.Value }) : BadRequest(new { message = result.Error.Message });
        }
        catch (OperationCanceledException)
        {
            return new EmptyResult();
        }
    }

    [HttpPost, EnableRateLimiting("guest-booking"), RequestSizeLimit(2048)]
    public async Task<IActionResult> SendOtp([FromBody] SendBookingOtpRequest? request, CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@') || request.ScheduleId <= 0)
        {
            return BadRequest(new { message = "Vui lòng nhập địa chỉ email hợp lệ để nhận mã xác thực." });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var slotCheck = await booking.GetSlotRemainingAsync(request.ScheduleId, request.StartTime, ct);
        if (!slotCheck.IsSuccess)
        {
            return BadRequest(new { message = slotCheck.Error.Message });
        }

        var holdKey = SlotHoldKey(request.ScheduleId, request.StartTime);
        var holds = cache.GetOrCreate(holdKey, entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromMinutes(10);
            return new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        })!;

        var utcNow = DateTime.UtcNow;
        foreach (var kvp in holds)
        {
            if (kvp.Value <= utcNow)
            {
                holds.TryRemove(kvp.Key, out _);
            }
        }

        var otherHolds = holds.Count(kvp => !string.Equals(kvp.Key, normalizedEmail, StringComparison.OrdinalIgnoreCase));
        if (slotCheck.Value - otherHolds <= 0)
        {
            return BadRequest(new { message = "Khung giờ này vừa có người giữ chỗ chờ xác thực OTP. Vui lòng chọn khung giờ khác hoặc thử lại sau ít phút." });
        }

        holds[normalizedEmail] = utcNow.AddMinutes(5);

        var otpCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var cacheKey = $"booking_otp:{normalizedEmail}";

        cache.Set(cacheKey, otpCode, TimeSpan.FromMinutes(5));

        var emailBody = $@"
<div style=""font-family: Arial, sans-serif; max-width: 540px; margin: 0 auto; padding: 25px; border: 1px solid #cbe4ee; border-radius: 12px; background: #ffffff;"">
    <div style=""text-align: center; margin-bottom: 20px;"">
        <h2 style=""color: #007d9f; margin: 0;"">Bệnh viện Đa khoa MediCare</h2>
        <p style=""color: #557185; font-size: 13px; margin-top: 5px;"">Mã xác thực đăng ký lịch khám bệnh</p>
    </div>
    <div style=""padding: 20px; background: #f0f8fb; border-radius: 8px; text-align: center; margin-bottom: 20px;"">
        <p style=""font-size: 14px; color: #15334d; margin: 0 0 10px 0;"">Xin chào <strong>{System.Net.WebUtility.HtmlEncode(request.FullName)}</strong>,</p>
        <p style=""font-size: 13px; color: #4b677a; margin: 0 0 15px 0;"">Mã xác thực (OTP) của bạn để hoàn tất đặt lịch khám là:</p>
        <div style=""font-size: 32px; font-weight: bold; letter-spacing: 6px; color: #007d9f; padding: 10px 0;"">{otpCode}</div>
        <p style=""font-size: 12px; color: #7f99a9; margin: 10px 0 0 0;"">Mã có hiệu lực trong vòng 5 phút. Vui lòng không chia sẻ mã này cho bất kỳ ai.</p>
    </div>
    <p style=""font-size: 12px; color: #8ba2b1; text-align: center; margin: 0;"">© 2026 MediCare · Hệ thống thông tin bệnh viện trực tuyến</p>
</div>";

        try
        {
            await emailSender.SendEmailAsync(normalizedEmail, "Mã xác thực đặt lịch khám bệnh — MediCare", emailBody, ct);
            logger.LogInformation("Đã gửi email OTP đặt lịch thành công tới {Email}", normalizedEmail);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Gửi email OTP thất bại tới {Email}: {Message}", normalizedEmail, ex.Message);
            return StatusCode(500, new { message = "Không thể gửi email chứa mã OTP. Vui lòng kiểm tra lại địa chỉ email hoặc thử lại sau." });
        }

        return Ok(new
        {
            success = true,
            message = $"Đã gửi mã xác thực tới {normalizedEmail}. Vui lòng kiểm tra hộp thư đến (và cả mục Spam/Thư rác nếu không thấy)."
        });
    }

    [HttpPost, EnableRateLimiting("guest-booking"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Confirm([FromBody] GuestBookingRequest? request, CancellationToken ct)
    {
        if (!ModelState.IsValid || request == null) return BadRequest(new { message = "Kiểm tra họ tên, ngày sinh và số điện thoại người khám." });

        int? authenticatedAccountId = null;
        if (User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedAccountId))
        {
            authenticatedAccountId = parsedAccountId;
        }
        var isAuthenticated = authenticatedAccountId.HasValue;
        request.BookedByAccountId = authenticatedAccountId;

        var isTestEnvironment = env.IsEnvironment("Testing");
        string? cacheKey = null;
        if (!isAuthenticated && (!isTestEnvironment || !string.IsNullOrWhiteSpace(request.Email) || !string.IsNullOrWhiteSpace(request.OtpCode)))
        {
            if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
            {
                return BadRequest(new { message = "Vui lòng nhập địa chỉ email để xác thực đặt lịch." });
            }

            if (string.IsNullOrWhiteSpace(request.OtpCode) || request.OtpCode.Trim().Length != 6)
            {
                return BadRequest(new { message = "Vui lòng nhập đủ 6 chữ số mã xác thực OTP đã gửi đến email của bạn." });
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            cacheKey = $"booking_otp:{normalizedEmail}";

            if (!cache.TryGetValue<string>(cacheKey, out var expectedOtp))
            {
                return BadRequest(new { message = "Mã OTP đã hết hạn hoặc chưa được gửi. Vui lòng bấm gửi lại mã." });
            }

            if (request.OtpCode.Trim() != expectedOtp)
            {
                return BadRequest(new { message = "Mã OTP không chính xác. Vui lòng kiểm tra lại email của bạn." });
            }
        }

        try
        {
            var result = await booking.ConfirmAsync(request, ct);
            if (result.IsSuccess)
            {
                if (cacheKey != null)
                {
                    cache.Remove(cacheKey);
                }

                if (!string.IsNullOrWhiteSpace(request.Email))
                {
                    var holdKey = SlotHoldKey(request.ScheduleId, request.StartTime);
                    if (cache.TryGetValue<ConcurrentDictionary<string, DateTime>>(holdKey, out var holds) && holds != null)
                    {
                        holds.TryRemove(request.Email.Trim().ToLowerInvariant(), out _);
                    }
                }

                var receipt = result.Value!;
                var receiptEmail = !string.IsNullOrWhiteSpace(request.Email)
                    ? request.Email.Trim()
                    : User.FindFirstValue(ClaimTypes.Email);

                if (!string.IsNullOrWhiteSpace(receiptEmail) && !isTestEnvironment)
                {
                    var receiptBody = $@"
<div style=""font-family: Arial, sans-serif; max-width: 560px; margin: 0 auto; padding: 24px; border: 1px solid #cbe4ee; border-radius: 12px; background: #ffffff;"">
    <h2 style=""color: #007d9f; margin: 0 0 8px 0;"">Phiếu xác nhận đặt lịch khám — MediCare</h2>
    <p style=""color: #4b677a; font-size: 14px; margin: 0 0 16px 0;"">Lịch khám của bạn đã được ghi nhận thành công.</p>
    <div style=""background: #f0f8fb; padding: 16px; border-radius: 8px; margin-bottom: 16px;"">
        <p style=""margin: 0 0 6px 0;""><strong>Mã lịch hẹn:</strong> <span style=""color: #007d9f; font-size: 18px;"">{System.Net.WebUtility.HtmlEncode(receipt.AppointmentCode)}</span></p>
        <p style=""margin: 0 0 6px 0;""><strong>Người khám:</strong> {System.Net.WebUtility.HtmlEncode(receipt.PatientName)}</p>
        <p style=""margin: 0 0 6px 0;""><strong>Bác sĩ:</strong> {System.Net.WebUtility.HtmlEncode(receipt.DoctorName)} ({System.Net.WebUtility.HtmlEncode(receipt.Department)})</p>
        <p style=""margin: 0 0 6px 0;""><strong>Thời gian:</strong> {receipt.StartTime:HH:mm} – {receipt.EndTime:HH:mm} ngày {receipt.StartTime:dd/MM/yyyy}</p>
        <p style=""margin: 0;""><strong>Phòng khám:</strong> {System.Net.WebUtility.HtmlEncode(receipt.Room)}</p>
    </div>
    <p style=""font-size: 12px; color: #627d92; margin: 0;"">Vui lòng mang theo giấy tờ tùy thân / BHYT và cung cấp mã lịch hẹn tại quầy tiếp nhận khi đến khám.</p>
</div>";
                    try
                    {
                        await emailSender.SendEmailAsync(receiptEmail, $"[MediCare] Phiếu hẹn khám {receipt.AppointmentCode}", receiptBody, ct);
                    }
                    catch (Exception emailEx) when (emailEx is not OperationCanceledException)
                    {
                        logger.LogWarning(emailEx, "Không thể gửi email phiếu hẹn {AppointmentCode} tới {Email}", receipt.AppointmentCode, receiptEmail);
                    }
                }

                return Json(new { receipt });
            }
            return BadRequest(new { message = result.Error.Message });
        }
        catch (OperationCanceledException)
        {
            return new EmptyResult();
        }
        catch (Exception error)
        {
            logger.LogError(error, "Guest booking failed: {Message}", error.Message);
            var msg = env.IsDevelopment()
                ? $"Chưa thể xác nhận lịch ({error.GetType().Name}): {error.Message}"
                : "Chưa thể xác nhận lịch. Vui lòng thử lại sau hoặc đến quầy tiếp nhận.";
            return StatusCode(503, new { message = msg });
        }
    }

    private static string SlotHoldKey(int scheduleId, DateTime startTime) =>
        $"slot_hold:{scheduleId}:{DateTime.SpecifyKind(startTime, DateTimeKind.Unspecified):yyyyMMddHHmm}";
}
