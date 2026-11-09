using System.Security.Claims;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HIS.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "AdminOnly")]
[Route("Admin/Management")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ManagementController(
    IStaffService staff,
    IDepartmentService departments,
    IAdminReportService reports,
    IScheduleChangeService schedules,
    TimeProvider clock,
    ILogger<ManagementController> logger) : Controller
{
    private int ActorId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!await staff.IsActiveAdminAsync(ActorId, context.HttpContext.RequestAborted))
        {
            context.Result = Forbid();
            return;
        }

        await next();
    }

    /// <summary>
    /// Hiển thị trang quản trị vận hành trung tâm cho Quản trị viên.
    /// </summary>
    [HttpGet("")]
    public IActionResult Index() => View();

    /// <summary>
    /// API lấy danh bạ người dùng, nhân sự y tế, vai trò và chuyên khoa cho trang quản trị.
    /// </summary>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpGet("Directory")]
    public Task<IActionResult> Directory(CancellationToken ct) => ExecuteAsync(async () => Ok(new
    {
        accounts = await staff.GetAdminAccountsAsync(ActorId, ct),
        staff = await staff.GetAdminStaffAsync(ActorId, ct),
        roles = await staff.GetAdminRolesAsync(ActorId, ct),
        doctors = await staff.GetDoctorsAsync(ct),
        departments = await departments.GetDepartmentsAsync(ct)
    }));

    /// <summary>
    /// API tổng hợp báo cáo tài chính doanh thu cho trang quản trị.
    /// </summary>
    /// <param name="from">Ngày bắt đầu.</param>
    /// <param name="to">Ngày kết thúc.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpGet("Report")]
    public Task<IActionResult> Report(DateOnly? from, DateOnly? to, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        return Ok(await reports.GetRevenueAsync(ActorId, from ?? new DateOnly(today.Year, today.Month, 1), to ?? today, ct));
    });

    /// <summary>
    /// Xuất tệp báo cáo doanh thu tài chính định dạng CSV.
    /// </summary>
    /// <param name="from">Ngày bắt đầu.</param>
    /// <param name="to">Ngày kết thúc.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpGet("Revenue.csv")]
    public Task<IActionResult> Export(DateOnly from, DateOnly to, CancellationToken ct) => ExecuteAsync(async () =>
        File(await reports.ExportRevenueAsync(ActorId, from, to, ct), "text/csv; charset=utf-8", $"doanh-thu-{from:yyyyMMdd}-{to:yyyyMMdd}.csv"));

    /// <summary>
    /// API lấy danh sách yêu cầu nghỉ phép và đổi ca chờ duyệt.
    /// </summary>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpGet("Requests")]
    public Task<IActionResult> Requests(CancellationToken ct) => ExecuteAsync(async () =>
        Ok(await schedules.GetRequestsAsync(ActorId, ct)));

    /// <summary>
    /// API tạo tài khoản người dùng và hồ sơ nhân sự mới.
    /// </summary>
    /// <param name="request">Yêu cầu tạo tài khoản.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost("Accounts")]
    public Task<IActionResult> CreateAccount([FromBody] CreateAdminAccountRequest request, CancellationToken ct) =>
        ExecuteAsync(async () => Ok(new
        {
            id = await staff.CreateAdminAccountAsync(ActorId, request, ct),
            message = "Đã tạo tài khoản và hồ sơ liên quan."
        }));

    /// <summary>
    /// API kích hoạt hoặc vô hiệu hóa tài khoản người dùng.
    /// </summary>
    /// <param name="request">Yêu cầu cập nhật trạng thái.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost("AccountStatus")]
    public Task<IActionResult> AccountStatus([FromBody] AccountActivationRequest request, CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            await staff.SetAccountActiveAsync(ActorId, request.AccountId, request.IsActive, ct);
            return Ok(new { message = "Đã cập nhật trạng thái tài khoản." });
        });

    /// <summary>
    /// API thêm mới chuyên khoa phòng.
    /// </summary>
    /// <param name="request">Yêu cầu thêm khoa phòng.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost("Departments")]
    public Task<IActionResult> CreateDepartment([FromBody] DepartmentCreateRequest request, CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            AdminValidate(request);
            DepartmentListItemDto department;
            try
            {
                department = await departments.CreateDepartmentAsync(request.Name, request.Description, ct);
            }
            catch (InvalidOperationException error) when (error.Message == $"Khoa/phòng '{request.Name.Trim()}' đã tồn tại.")
            {
                throw new AdminOperationException(error.Message);
            }
            return Ok(new
            {
                id = department.Id,
                message = "Đã thêm phòng ban."
            });
        });

    /// <summary>
    /// API phê duyệt hoặc từ chối đề xuất đổi ca của bác sĩ.
    /// </summary>
    /// <param name="request">Quyết định phê duyệt.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    [HttpPost("Decision")]
    public Task<IActionResult> Decision([FromBody] ScheduleDecisionRequest request, CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var result = await schedules.ReviewAsync(ActorId, request, ct);
            var message = request.Approve
                ? $"Đã duyệt: chuyển {result.Transferred} lịch, {result.NeedsRebooking} lịch cần đặt lại, giải phóng {result.ExpiredHolds} lượt giữ chỗ."
                : "Đã từ chối đề nghị. Lịch khám giữ nguyên.";
            return Ok(new
            {
                message,
                result
            });
        });

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> operation)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { message = "Thông tin gửi lên chưa đúng định dạng hoặc thiếu trường bắt buộc." });
        }

        try
        {
            return await operation();
        }
        catch (Exception error) when (error is AdminOperationException or ArgumentException)
        {
            return BadRequest(new { message = error.Message });
        }
        catch (Exception error) when (error is DbUpdateException or PostgresException)
        {
            logger.LogWarning("Admin request rejected: {Type}", error.GetType().Name);
            return Conflict(new { message = "Dữ liệu vừa thay đổi hoặc bị trùng. Hãy tải lại danh sách trước khi thử lại." });
        }
        catch (InvalidOperationException error)
        {
            logger.LogWarning("Admin operation failed: {Type}", error.GetType().Name);
            return BadRequest(new { message = "Chưa thực hiện được thao tác. Hãy kiểm tra dữ liệu và thử lại." });
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError("Admin request failed: {Type}", error.GetType().Name);
            return StatusCode(503, new { message = "Chưa kết nối được dữ liệu quản trị. Vui lòng thử lại sau." });
        }
    }

    private static void AdminValidate(object request)
    {
        var errors = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(request,
            new System.ComponentModel.DataAnnotations.ValidationContext(request), errors, true))
        {
            throw new ArgumentException(string.Join(" ", errors.Select(error => error.ErrorMessage)));
        }
    }
}

