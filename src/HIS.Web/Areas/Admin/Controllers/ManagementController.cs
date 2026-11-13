using System.Security.Claims;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Services;
using HIS.Domain;
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
    IHospitalCatalogService catalog,
    IDepartmentHeadService departmentHead,
    ISlotGenerationService slotGeneration,
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

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("Directory")]
    public Task<IActionResult> Directory(CancellationToken ct) => ExecuteAsync(async () => Ok(new
    {
        accounts = await staff.GetAdminAccountsAsync(ActorId, ct),
        staff = await staff.GetAdminStaffAsync(ActorId, ct),
        roles = await staff.GetAdminRolesAsync(ActorId, ct),
        doctors = await staff.GetDoctorsAsync(ct),
        departments = await departments.GetDepartmentsAsync(ct)
    }));

    [HttpGet("Report")]
    public Task<IActionResult> Report(DateOnly? from, DateOnly? to, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        return Ok(await reports.GetRevenueAsync(ActorId, from ?? new DateOnly(today.Year, today.Month, 1), to ?? today, ct));
    });

    [HttpGet("Revenue.csv")]
    public Task<IActionResult> Export(DateOnly from, DateOnly to, CancellationToken ct) => ExecuteAsync(async () =>
        File(await reports.ExportRevenueAsync(ActorId, from, to, ct), "text/csv; charset=utf-8", $"doanh-thu-{from:yyyyMMdd}-{to:yyyyMMdd}.csv"));

    [HttpGet("Requests")]
    public Task<IActionResult> Requests(CancellationToken ct) => ExecuteAsync(async () =>
        Ok(await schedules.GetRequestsAsync(ActorId, ct)));

    [HttpPost("Accounts")]
    public Task<IActionResult> CreateAccount([FromBody] CreateAdminAccountRequest request, CancellationToken ct) =>
        ExecuteAsync(async () => Ok(new
        {
            id = await staff.CreateAdminAccountAsync(ActorId, request, ct),
            message = "Đã tạo tài khoản và hồ sơ liên quan."
        }));

    [HttpPost("AccountStatus")]
    public Task<IActionResult> AccountStatus([FromBody] AccountActivationRequest request, CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            await staff.SetAccountActiveAsync(ActorId, request.AccountId, request.IsActive, ct);
            return Ok(new { message = "Đã cập nhật trạng thái tài khoản." });
        });

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

    [HttpGet("CatalogDirectory")]
    public Task<IActionResult> CatalogDirectory(CancellationToken ct) => ExecuteAsync(async () => Ok(new
    {
        rooms = await catalog.GetRoomsAsync(null, ct),
        services = await catalog.GetServicesAsync(null, ct),
        serviceTypes = await catalog.GetServiceTypesAsync(ct),
        medications = await catalog.GetMedicationsAsync(ct),
        departments = await departments.GetDepartmentsAsync(ct)
    }));

    [HttpPost("Rooms")]
    public Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest request, CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            AdminValidate(request);
            var id = await catalog.CreateRoomAsync(request, ct);
            return Ok(new { id, message = "Đã thêm phòng khám mới." });
        });

    [HttpPost("Rooms/Delete")]
    public Task<IActionResult> DeleteRoom([FromBody] DeleteRoomRequest request, CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var success = await catalog.DeleteRoomAsync(request.RoomId, ct);
            return success ? Ok(new { message = "Đã xóa phòng khám." }) : NotFound(new { message = "Phòng khám không tồn tại." });
        });

    [HttpPost("Services")]
    public Task<IActionResult> UpsertService([FromBody] UpsertServiceRequest request, CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            AdminValidate(request);
            var id = await catalog.UpsertServiceAsync(request, ct);
            return Ok(new { id, message = request.Id.HasValue ? "Đã cập nhật dịch vụ y tế." : "Đã thêm dịch vụ y tế mới." });
        });

    [HttpPost("Medications")]
    public Task<IActionResult> UpsertMedication([FromBody] UpsertMedicationRequest request, CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            AdminValidate(request);
            var id = await catalog.UpsertMedicationAsync(request, ct);
            return Ok(new { id, message = request.Id.HasValue ? "Đã cập nhật thuốc." : "Đã thêm thuốc vào danh mục." });
        });

    [HttpGet("AuditLogs")]
    // [FromQuery] bắt buộc: tham số tên "action" nếu không sẽ nhận route value "AuditLogs" và lọc mất toàn bộ nhật ký
    public Task<IActionResult> AuditLogs(DateOnly? from, DateOnly? to, [FromQuery(Name = "action")] string? actionCode, string? search, int limit = 200, CancellationToken ct = default) =>
        ExecuteAsync(async () => Ok(await reports.GetAuditLogsAsync(ActorId, from, to, actionCode, search, limit, ct)));

    [HttpGet("DepartmentStats")]
    public Task<IActionResult> DepartmentStats(DateOnly? from, DateOnly? to, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        return Ok(await reports.GetDepartmentStatsAsync(ActorId, from ?? new DateOnly(today.Year, today.Month, 1), to ?? today, ct));
    });

    [HttpGet("SchedulingOverview")]
    public Task<IActionResult> SchedulingOverview(int? departmentId, int weekOffset = 0, CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        var deptList = await departments.GetDepartmentsAsync(ct);
        int selectedDeptId;
        if (departmentId is > 0)
        {
            selectedDeptId = departmentId.Value;
        }
        else
        {
            var allDoctors = await departmentHead.GetDoctorsInDepartmentAsync(0, ct);
            var firstDeptWithDoctors = deptList.FirstOrDefault(d => allDoctors.Any(doc => doc.DepartmentName == d.Name));
            selectedDeptId = firstDeptWithDoctors?.Id ?? deptList.FirstOrDefault()?.Id ?? 0;
        }

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var daysFromMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var weekStart = today.AddDays(-daysFromMonday + weekOffset * 7);
        var weekEnd = weekStart.AddDays(6);

        var activePeriod = await departmentHead.GetActiveSchedulingPeriodAsync(selectedDeptId > 0 ? selectedDeptId : null, ct);
        var demandGrid = activePeriod != null && selectedDeptId > 0
            ? await departmentHead.GetDepartmentDemandGridAsync(selectedDeptId, activePeriod.Id, ct)
            : null;
        var deptSchedules = selectedDeptId > 0
            ? await departmentHead.GetSchedulesAsync(selectedDeptId, null, weekStart, weekEnd, ct)
            : [];
        var deptDutyShifts = selectedDeptId > 0
            ? (await departmentHead.GetDutyShiftsAsync(selectedDeptId, ct))
                .Where(ds => DateOnly.FromDateTime(ds.StartTime) >= weekStart && DateOnly.FromDateTime(ds.StartTime) <= weekEnd)
                .ToList()
            : [];
        var deptDoctors = selectedDeptId > 0
            ? await departmentHead.GetDoctorsInDepartmentAsync(selectedDeptId, ct)
            : [];
        var deptRooms = selectedDeptId > 0
            ? await departmentHead.GetRoomsInDepartmentAsync(selectedDeptId, ct)
            : [];
        var deptStaff = selectedDeptId > 0
            ? await departmentHead.GetStaffInDepartmentAsync(selectedDeptId, ct)
            : [];

        return Ok(new
        {
            selectedDeptId,
            today,
            weekStart,
            weekEnd,
            activePeriod,
            demandGrid,
            schedules = deptSchedules,
            dutyShifts = deptDutyShifts,
            doctors = deptDoctors,
            staff = deptStaff,
            rooms = deptRooms
        });
    });

    [HttpGet("DecisionMatrix")]
    public Task<IActionResult> DecisionMatrix(int departmentId, DateOnly workDate, ExamShift shift, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (departmentId <= 0) throw new AdminOperationException("Vui lòng chọn khoa chuyên môn.");
        return Ok(await departmentHead.GetDecisionMatrixAsync(departmentId, workDate, shift, ct));
    });

    [HttpPost("SchedulingPeriods")]
    public Task<IActionResult> CreateSchedulingPeriod([FromBody] AdminCreatePeriodRequest request, CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var result = await departmentHead.CreateSchedulingPeriodAsync(
                new CreateSchedulingPeriodRequest(
                    request.DepartmentId,
                    request.Name,
                    request.StartDate,
                    request.EndDate,
                    request.BiddingOpensAt,
                    request.BiddingClosesAt,
                    ActorId),
                ct);
            return UnwrapResult(result, id => new { id, message = "Đã tạo kỳ đăng ký ca khám mới." });
        });

    [HttpPost("PublishPeriod")]
    public Task<IActionResult> PublishPeriod([FromBody] AdminPublishPeriodRequest request, CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var result = await departmentHead.PublishSchedulingPeriodAsync(request.PeriodId, ActorId, ct);
            return UnwrapResult(result, slots => new { slots, message = $"Đã xuất bản kỳ lịch và tạo {slots} slot khám trực tuyến." });
        });

    [HttpPost("GenerateSlots")]
    public Task<IActionResult> GenerateSlots([FromBody] AdminGenerateSlotsRequest request, CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var result = await slotGeneration.GenerateSlotsAsync(request.StartDate, request.EndDate, request.DepartmentId, ct);
            return UnwrapResult(result, res => new
            {
                res.CreatedCount,
                res.ExistingCount,
                res.ExceptionSkippedCount,
                message = $"Đã tạo {res.CreatedCount} slot mới (đã có {res.ExistingCount} slot, bỏ qua {res.ExceptionSkippedCount} ca nghỉ)."
            });
        });

    [HttpPost("ApproveShift")]
    public Task<IActionResult> ApproveShift([FromBody] AdminApproveShiftRequest request, CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var result = await departmentHead.ApproveShiftAsync(new ApproveShiftRequest(request.ScheduleId, request.RoomId, ActorId), ct);
            if (!result.IsSuccess) throw new AdminOperationException(result.Error.Message);
            return Ok(new { message = "Đã duyệt ca khám và gán phòng thành công." });
        });

    [HttpPost("ReallocateShift")]
    public Task<IActionResult> ReallocateShift([FromBody] AdminReallocateShiftRequest request, CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var result = await departmentHead.ReallocateShiftAsync(
                new ReallocateShiftRequest(request.OldScheduleId, request.TargetDate, request.TargetShift, request.TargetRoomId, ActorId),
                ct);
            return UnwrapResult(result, id => new { id, message = "Đã điều chuyển ca khám thành công." });
        });

    [HttpPost("UpsertSchedule")]
    public Task<IActionResult> UpsertSchedule([FromBody] AdminUpsertScheduleRequest request, CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var start = request.Shift == ExamShift.Morning ? new TimeOnly(7, 30) : new TimeOnly(13, 0);
            var end = request.Shift == ExamShift.Morning ? new TimeOnly(11, 30) : new TimeOnly(16, 30);
            var result = await departmentHead.UpsertDoctorScheduleAsync(
                new UpsertDoctorScheduleRequest(
                    request.Id,
                    null,
                    request.DoctorId,
                    request.DepartmentId,
                    request.RoomId,
                    request.WorkDate,
                    request.Shift,
                    start,
                    end,
                    30,
                    request.MaxPatients > 0 ? request.MaxPatients : 40,
                    request.OnlineSlots >= 0 ? request.OnlineSlots : 28,
                    DoctorScheduleStatus.Approved,
                    request.DoctorNote),
                ct);
            return UnwrapResult(result, id => new { id, message = "Đã lưu phân công ca khám." });
        });

    [HttpPost("DeleteSchedule")]
    public Task<IActionResult> DeleteSchedule([FromBody] AdminDeleteScheduleRequest request, CancellationToken ct) =>
        ExecuteAsync(async () =>
        {
            var result = await departmentHead.DeleteDoctorScheduleAsync(request.ScheduleId, request.DepartmentId, ActorId, ct);
            if (!result.IsSuccess) throw new AdminOperationException(result.Error.Message);
            return Ok(new { message = "Đã hủy ca khám thành công." });
        });

    private IActionResult UnwrapResult<T>(Result<T> result, Func<T, object> okPayload)
    {
        if (!result.IsSuccess) throw new AdminOperationException(result.Error.Message);
        return Ok(okPayload(result.Value!));
    }

    public sealed record AdminCreatePeriodRequest(int? DepartmentId, string Name, DateOnly StartDate, DateOnly EndDate, DateTime BiddingOpensAt, DateTime BiddingClosesAt);
    public sealed record AdminPublishPeriodRequest(int PeriodId);
    public sealed record AdminGenerateSlotsRequest(DateOnly StartDate, DateOnly EndDate, int? DepartmentId);
    public sealed record AdminApproveShiftRequest(int ScheduleId, int RoomId);
    public sealed record AdminReallocateShiftRequest(int OldScheduleId, DateOnly TargetDate, ExamShift TargetShift, int TargetRoomId);
    public sealed record AdminUpsertScheduleRequest(int? Id, int DoctorId, int DepartmentId, int? RoomId, DateOnly WorkDate, ExamShift Shift, int MaxPatients, int OnlineSlots, string? DoctorNote);
    public sealed record AdminDeleteScheduleRequest(int ScheduleId, int DepartmentId);

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
