using System.Security.Claims;
using HIS.BLL.DTOs;
using HIS.BLL.Services;
using HIS.DAL;
using HIS.Domain;
using HIS.Web.Areas.Staff.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HIS.Web.Areas.Staff.Controllers;

[Area("Staff")]
[Authorize(Policy = "DoctorOnly")]
public class ConsultationController : Controller
{
    private readonly IDoctorService _doctorService;
    private readonly IQueueService _queueService;
    private readonly IDepartmentHeadService _departmentHeadService;
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConsultationController> _logger;

    public ConsultationController(
        IDoctorService doctorService,
        IQueueService queueService,
        IDepartmentHeadService departmentHeadService,
        AppDbContext context,
        TimeProvider timeProvider,
        ILogger<ConsultationController> logger)
    {
        _doctorService = doctorService;
        _queueService = queueService;
        _departmentHeadService = departmentHeadService;
        _context = context;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // 1. Màn hình Bác sĩ & Trưởng khoa: Phân luồng dữ liệu chuẩn theo phân quyền
    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? date, ExamShift? shift, string? tab, int? doctorId, int weekOffset = 0, CancellationToken ct = default)
    {
        try
        {
            var isDeptHead = User.IsInRole("DEPARTMENT_HEAD");
            var accountId = GetCurrentAccountId();

            int currentDoctorId = 0;
            int departmentId = 0;
            string departmentName = "";
            string currentDoctorName = "";
            string currentDoctorRoom = "";

            if (isDeptHead)
            {
                var deptHead = await _context.DepartmentHeads
                    .Include(dh => dh.Department)
                    .FirstOrDefaultAsync(dh => dh.AccountId == accountId, ct);

                if (deptHead != null)
                {
                    departmentId = deptHead.DepartmentId;
                    departmentName = deptHead.Department.Name;
                    currentDoctorName = deptHead.FullName;
                }
                else
                {
                    var docHead = await _context.Doctors.Include(d => d.Department).FirstOrDefaultAsync(d => d.AccountId == accountId, ct);
                    if (docHead != null)
                    {
                        departmentId = docHead.DepartmentId;
                        departmentName = docHead.Department.Name;
                        currentDoctorName = docHead.FullName;
                    }
                }

                if (departmentId == 0)
                {
                    departmentName = "Chưa gán khoa quản lý";
                }

                currentDoctorRoom = "P. Trưởng khoa";
            }
            else
            {
                // Bác sĩ thường: Chỉ phụ trách phòng và bệnh nhân của chính mình
                var doc = await _context.Doctors
                    .Include(d => d.Department)
                    .Include(d => d.Schedules).ThenInclude(s => s.Room)
                    .FirstOrDefaultAsync(d => d.AccountId == accountId, ct);

                if (doc != null)
                {
                    currentDoctorId = doc.Id;
                    departmentId = doc.DepartmentId;
                    departmentName = doc.Department.Name;
                    currentDoctorName = doc.FullName;
                    currentDoctorRoom = doc.Schedules.FirstOrDefault()?.Room?.RoomNumber ?? "Chưa phân phòng";
                }
                else
                {
                    currentDoctorId = await ResolveDoctorIdAsync(ct);
                    var fallbackDoc = currentDoctorId > 0
                        ? await _context.Doctors.Include(d => d.Department).FirstOrDefaultAsync(d => d.Id == currentDoctorId, ct)
                        : null;
                    departmentId = fallbackDoc?.DepartmentId ?? 0;
                    departmentName = fallbackDoc?.Department?.Name ?? "";
                    currentDoctorName = fallbackDoc?.FullName ?? "Bác sĩ";
                    currentDoctorRoom = "Chưa phân phòng";
                }
            }

            // Mọi mốc "hôm nay" lấy qua TimeProvider (giờ Việt Nam), không phụ thuộc múi giờ máy chủ
            var localNow = _timeProvider.GetLocalNow().DateTime;
            var today = DateOnly.FromDateTime(localNow);
            var todayStart = today.ToDateTime(TimeOnly.MinValue);
            var todayEnd = todayStart.AddDays(1);
            // Dùng để tính tuổi đúng (trừ 1 nếu chưa tới sinh nhật trong năm)
            var todayYear = today.Year;
            var todayMonth = today.Month;
            var todayDay = today.Day;

            var selectedDate = date ?? today;
            var selectedShift = shift ?? (localNow.Hour < 12 ? ExamShift.Morning : ExamShift.Afternoon);
            var activeTab = string.IsNullOrWhiteSpace(tab) ? "consultation" : tab.ToLowerInvariant();

            // 1. Hàng đợi bệnh nhân đang chờ khám từ Database
            var baseQueueQuery = _context.Appointments
                .AsNoTracking()
                .Include(a => a.Patient)
                .Include(a => a.VitalSigns)
                .Include(a => a.Doctor)
                .Include(a => a.Slot)
                // Chỉ lượt khám của HÔM NAY (theo giờ slot, hoặc giờ tạo với ca vãng lai không có slot)
                .Where(a => (a.Slot != null && a.Slot.StartTime >= todayStart && a.Slot.StartTime < todayEnd)
                            || (a.Slot == null && a.CreatedAt >= todayStart && a.CreatedAt < todayEnd));

            if (isDeptHead)
            {
                // Trưởng khoa: Xem bệnh nhân thuộc khoa mình
                baseQueueQuery = baseQueueQuery.Where(a => (a.Doctor != null && a.Doctor.DepartmentId == departmentId) || a.DepartmentId == departmentId);

                // Nếu trưởng khoa chọn lọc theo 1 bác sĩ cụ thể trong khoa:
                if (doctorId.HasValue && doctorId.Value > 0)
                {
                    baseQueueQuery = baseQueueQuery.Where(a => a.DoctorId == doctorId.Value);
                }
            }
            else
            {
                // Bác sĩ thường: CHỈ THẤY BỆNH NHÂN THEO PHÒNG / BÁC SĨ CỦA MÌNH
                baseQueueQuery = baseQueueQuery.Where(a => a.DoctorId == currentDoctorId);
            }

            IQueryable<ConsultationQueueItemDto> ProjectQueueItems(IQueryable<Appointment> query) =>
                query.Select(a => new ConsultationQueueItemDto(
                    a.Id,
                    a.AppointmentCode,
                    a.Patient.FullName,
                    todayYear - a.Patient.DateOfBirth.Year
                        - ((a.Patient.DateOfBirth.Month > todayMonth
                            || (a.Patient.DateOfBirth.Month == todayMonth && a.Patient.DateOfBirth.Day > todayDay)) ? 1 : 0),
                    a.Patient.Gender == Gender.Male ? "Nam" : "Nữ",
                    a.QueueNumber ?? "—",
                    a.PriorityLevel,
                    a.CheckedInAt,
                    a.Reason ?? "Khám bệnh theo lịch hẹn",
                    a.Patient.Allergies,
                    a.VitalSigns != null ? a.VitalSigns.Pulse : null,
                    a.VitalSigns != null && a.VitalSigns.SystolicBp.HasValue && a.VitalSigns.DiastolicBp.HasValue
                        ? $"{a.VitalSigns.SystolicBp}/{a.VitalSigns.DiastolicBp}"
                        : null,
                    a.VitalSigns != null ? a.VitalSigns.Temperature : null,
                    a.VitalSigns != null ? a.VitalSigns.Spo2 : null,
                    a.VitalSigns != null ? a.VitalSigns.Weight : null,
                    a.Doctor != null ? a.Doctor.FullName : "Chuyên khoa",
                    // Phòng của lượt khám; nếu chưa có thì lấy phòng của slot (không dùng số chứng chỉ hành nghề)
                    a.Room != null ? a.Room.RoomNumber : (a.Slot != null && a.Slot.Room != null ? a.Slot.Room.RoomNumber : ""),
                    a.StatusId
                ));

            var activePatient = await ProjectQueueItems(
                    baseQueueQuery
                        .Where(a => a.StatusId == (int)AppointmentStatusId.InProgress)
                        .OrderByDescending(a => a.UpdatedAt)
                        .ThenBy(a => a.QueueNumber))
                .FirstOrDefaultAsync(ct);

            var queue = await ProjectQueueItems(
                    baseQueueQuery
                        .Where(a => a.StatusId == (int)AppointmentStatusId.ReadyForDoctor ||
                                    a.StatusId == (int)AppointmentStatusId.CheckedIn)
                        // Ưu tiên giảm dần; cùng mức ưu tiên thì ai đến trước xếp trước (không sắp theo chuỗi số thứ tự)
                        .OrderByDescending(a => a.PriorityLevel)
                        .ThenBy(a => a.CheckedInAt ?? a.CreatedAt)
                        .ThenBy(a => a.Id))
                .ToListAsync(ct);

            // 2. Danh sách Chỉ định & kết quả từ Database
            var labsQuery = _context.LabOrders
                .AsNoTracking()
                .Include(l => l.Service)
                .Include(l => l.MedicalRecord).ThenInclude(r => r.Appointment).ThenInclude(a => a.Patient)
                .Include(l => l.MedicalRecord).ThenInclude(r => r.Doctor)
                .Include(l => l.LabResult)
                .AsQueryable();

            if (isDeptHead)
            {
                labsQuery = labsQuery.Where(l => l.MedicalRecord != null && l.MedicalRecord.Doctor != null && l.MedicalRecord.Doctor.DepartmentId == departmentId);
                if (doctorId.HasValue && doctorId.Value > 0)
                {
                    labsQuery = labsQuery.Where(l => l.MedicalRecord != null && l.MedicalRecord.DoctorId == doctorId.Value);
                }
            }
            else
            {
                labsQuery = labsQuery.Where(l => l.MedicalRecord != null && l.MedicalRecord.DoctorId == currentDoctorId);
            }

            var labs = await labsQuery
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => new ConsultationLabItemDto(
                    l.Id,
                    l.MedicalRecord.Appointment.Patient.FullName,
                    l.MedicalRecord.Appointment.AppointmentCode,
                    todayYear - l.MedicalRecord.Appointment.Patient.DateOfBirth.Year
                        - ((l.MedicalRecord.Appointment.Patient.DateOfBirth.Month > todayMonth
                            || (l.MedicalRecord.Appointment.Patient.DateOfBirth.Month == todayMonth
                                && l.MedicalRecord.Appointment.Patient.DateOfBirth.Day > todayDay)) ? 1 : 0),
                    l.Service.Name,
                    l.Note ?? "Chỉ định chuyên khoa",
                    l.CreatedAt,
                    l.Status == LabOrderStatus.Completed ? "Đã có kết quả" : "Chờ kết quả",
                    l.LabResult != null ? l.LabResult.Result : null
                ))
                .ToListAsync(ct);

            // 3. Danh sách Bệnh án trong khoa từ Database
            var recordsQuery = _context.MedicalRecords
                .AsNoTracking()
                .Include(r => r.Doctor).ThenInclude(d => d.Department)
                .Include(r => r.Appointment).ThenInclude(a => a.Patient)
                .AsQueryable();

            if (isDeptHead)
            {
                recordsQuery = recordsQuery.Where(r => r.Doctor != null && r.Doctor.DepartmentId == departmentId);
                if (doctorId.HasValue && doctorId.Value > 0)
                {
                    recordsQuery = recordsQuery.Where(r => r.DoctorId == doctorId.Value);
                }
            }
            else
            {
                recordsQuery = recordsQuery.Where(r => r.DoctorId == currentDoctorId);
            }

            var records = await recordsQuery
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new ConsultationRecordItemDto(
                    r.Id,
                    r.Appointment.Patient.FullName,
                    r.Appointment.AppointmentCode,
                    todayYear - r.Appointment.Patient.DateOfBirth.Year
                        - ((r.Appointment.Patient.DateOfBirth.Month > todayMonth
                            || (r.Appointment.Patient.DateOfBirth.Month == todayMonth
                                && r.Appointment.Patient.DateOfBirth.Day > todayDay)) ? 1 : 0),
                    r.Appointment.Patient.Gender == Gender.Male ? "Nam" : "Nữ",
                    r.CreatedAt,
                    r.Doctor.FullName,
                    r.Doctor.Department.Name,
                    r.Symptoms ?? "Không ghi nhận",
                    r.ClinicalExamination ?? "Không ghi nhận",
                    r.PrimaryDiagnosisCode ?? "—",
                    r.PrimaryDiagnosisDesc ?? "—",
                    r.Conclusion ?? "Đã hoàn tất khám"
                ))
                .ToListAsync(ct);

            // 4. Lịch làm việc trong tuần & Ca trực đêm từ Database
            var schedules = await _context.DoctorSchedules
                .AsNoTracking()
                .Include(s => s.Doctor)
                .Include(s => s.Room)
                .Where(s => isDeptHead ? s.Doctor.DepartmentId == departmentId : s.DoctorId == currentDoctorId)
                .OrderBy(s => s.WorkDate)
                .ThenBy(s => s.Shift)
                .Select(s => new DoctorScheduleItemDto(
                    s.Id,
                    s.WorkDate,
                    s.Shift,
                    s.StartTime,
                    s.EndTime,
                    s.Doctor.FullName,
                    s.Room != null ? s.Room.RoomNumber : null,
                    s.Doctor.Specialty ?? "Chuyên khoa",
                    s.DoctorId,
                    s.Status,
                    s.IsHotShift,
                    s.DoctorNote
                ))
                .ToListAsync(ct);

            var dutyShifts = await _context.DutyShifts
                .AsNoTracking()
                .Include(d => d.Account).ThenInclude(a => a.Doctor)
                .Include(d => d.Account).ThenInclude(a => a.DepartmentHead)
                .Include(d => d.Account).ThenInclude(a => a.Nurse)
                .Where(d => d.DepartmentId == departmentId || d.AccountId == accountId)
                .OrderByDescending(d => d.StartTime)
                .Select(d => new DutyShiftItemDto(
                    d.Account.Doctor != null ? d.Account.Doctor.FullName :
                    d.Account.DepartmentHead != null ? d.Account.DepartmentHead.FullName :
                    d.Account.Nurse != null ? d.Account.Nurse.FullName : d.Account.Username,
                    d.StartTime,
                    d.EndTime,
                    d.Note ?? "Trực đêm tại khoa",
                    d.AccountId
                ))
                .ToListAsync(ct);

            // 5. Đề nghị nghỉ / đổi ca từ Database
            var exceptionsQuery = _context.ScheduleExceptions
                .AsNoTracking()
                .Include(e => e.ReplacementDoctor)
                .Include(e => e.Doctor)
                .AsQueryable();

            if (isDeptHead)
            {
                exceptionsQuery = exceptionsQuery.Where(e => e.Doctor.DepartmentId == departmentId);
            }
            else
            {
                exceptionsQuery = exceptionsQuery.Where(e => e.DoctorId == currentDoctorId || (e.ReplacementDoctorId.HasValue && e.ReplacementDoctorId.Value == currentDoctorId));
            }

            var exceptions = await exceptionsQuery
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => new ScheduleExceptionItemDto(
                    e.Id,
                    e.Date,
                    e.Shift,
                    e.Type.ToString(),
                    e.Status == ScheduleExceptionStatus.Pending ? "Chờ duyệt" : e.Status == ScheduleExceptionStatus.Approved ? "Đã duyệt" : "Từ chối",
                    e.Reason ?? "Nghỉ việc riêng",
                    e.ReplacementDoctor != null ? e.ReplacementDoctor.FullName : null,
                    e.DoctorId,
                    e.ReplacementDoctorId
                ))
                .ToListAsync(ct);

            var availableDoctors = await _context.Doctors
                .AsNoTracking()
                .Where(d => d.DepartmentId == departmentId)
                .OrderBy(d => d.FullName)
                .Select(d => new DoctorSelectItemDto(d.Id, d.FullName, d.Specialty ?? "Chuyên khoa", d.LicenseNumber))
                .ToListAsync(ct);

            var availableServices = await _context.Services
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .Select(s => new ClinicalServiceOptionDto(s.Id, s.Name, s.Price))
                .ToListAsync(ct);

            var availableMedications = await _context.Medications
                .AsNoTracking()
                .Where(m => m.IsActive)
                .OrderBy(m => m.Name)
                .Select(m => new MedicationOptionDto(m.Id, m.Name, m.Strength, m.Unit, m.Price))
                .ToListAsync(ct);

            // Tải thông tin kỳ đăng ký ca khám & bảng độ cạnh tranh
            var activePeriod = await _departmentHeadService.GetActiveSchedulingPeriodAsync(departmentId, ct);
            DepartmentDemandGridDto? demandGrid = null;
            if (activePeriod != null)
            {
                demandGrid = await _departmentHeadService.GetDepartmentDemandGridAsync(departmentId, activePeriod.Id, ct);
            }

            int minShifts = 2; // Tối thiểu 2 ca
            int maxShifts = 10; // Tối đa toàn bộ ca trong tuần
            if (activePeriod != null)
            {
                int totalDays = (activePeriod.EndDate.DayNumber - activePeriod.StartDate.DayNumber) + 1;
                maxShifts = Math.Max(10, totalDays * 2);
            }
            if (currentDoctorId > 0)
            {
                var docQuota = await _context.Doctors
                    .AsNoTracking()
                    .Where(d => d.Id == currentDoctorId)
                    .Select(d => new { d.MinShiftsPerWeek, d.MaxShiftsPerWeek })
                    .FirstOrDefaultAsync(ct);
                if (docQuota != null)
                {
                    minShifts = docQuota.MinShiftsPerWeek <= 2 ? docQuota.MinShiftsPerWeek : 2;
                    maxShifts = Math.Max(maxShifts, docQuota.MaxShiftsPerWeek);
                }
            }

            var vm = new ConsultationViewModel
            {
                ActiveTab = activeTab,
                WeekOffset = weekOffset,
                SelectedDate = selectedDate,
                SelectedShift = selectedShift,
                IsDepartmentHead = isDeptHead,
                SelectedDoctorId = doctorId,
                DepartmentId = departmentId,
                DepartmentName = departmentName,
                CurrentDoctorName = currentDoctorName,
                CurrentDoctorRoom = currentDoctorRoom,
                CurrentDoctorId = currentDoctorId,
                MinShiftsPerWeek = minShifts,
                MaxShiftsPerWeek = maxShifts,
                QueuePatients = queue,
                // Ưu tiên mở lại ca bác sĩ đang khám dở; nếu không có thì mở ca đầu hàng
                ActivePatient = activePatient ?? queue.FirstOrDefault(),
                LabOrders = labs,
                MedicalRecords = records,
                DoctorSchedules = schedules,
                DutyShifts = dutyShifts,
                Exceptions = exceptions,
                AvailableDoctors = availableDoctors,
                AvailableServices = availableServices,
                AvailableMedications = availableMedications,
                ActivePeriod = activePeriod,
                DemandGrid = demandGrid
            };

            return View(vm);
        }
        catch (OperationCanceledException)
        {
            // Trình duyệt reload/chuyển trang làm hủy request - tránh Visual Studio debug exception popup
            return new EmptyResult();
        }
    }

    // 2. Bác sĩ gọi bệnh nhân tiếp theo vào phòng khám
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CallNext(int roomId, CancellationToken ct)
    {
        var accountId = GetCurrentAccountId();
        var result = await _queueService.CallNextPatientAsync(roomId, accountId, ct);
        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["SuccessMessage"] = $"Đã gọi lượt khám số {result.Value}.";
        }
        return RedirectToAction(nameof(Index));
    }

    // 2.5. Bác sĩ bắt đầu khám bệnh cho lượt khám (ReadyForDoctor -> InProgress)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartExam([FromBody] StartExamRequest request, CancellationToken ct)
    {
        var accountId = GetCurrentAccountId();
        var result = await _doctorService.StartConsultationAsync(accountId, request.AppointmentId, ct);
        if (!result.IsSuccess)
        {
            return BadRequest(new { success = false, message = result.Error.Message });
        }
        return Ok(new { success = true });
    }

    // 3. Hoàn tất lượt khám và tạo hồ sơ bệnh án
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRecord([FromForm] CreateMedicalRecordRequest request, CancellationToken ct)
    {
        try
        {
            var doctorId = await ResolveDoctorIdAsync(ct);
            if (doctorId <= 0)
            {
                TempData["ErrorMessage"] = "Tài khoản hiện tại chưa được liên kết với hồ sơ bác sĩ.";
                return RedirectToAction(nameof(Index));
            }

            var recordRequest = request with
            {
                DoctorId = doctorId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            };
            await _doctorService.CreateMedicalRecordAsync(recordRequest, ct);
            TempData["SuccessMessage"] = "Đã lưu hồ sơ bệnh án và hoàn thành lượt khám.";
        }
        catch (Exception ex) when (ex is InvalidOperationException)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi lưu hồ sơ bệnh án.");
            TempData["ErrorMessage"] = "Đã xảy ra lỗi trong quá trình lưu bệnh án. Vui lòng thử lại sau.";
        }

        return RedirectToAction(nameof(Index));
    }

    // 4. Bác sĩ gửi đề nghị nghỉ / đổi ca
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateException(
        string? date,
        ExamShift? shift,
        ScheduleExceptionType type,
        string reason,
        int? replacementDoctorId,
        CancellationToken ct)
    {
        try
        {
            var doctorId = await ResolveDoctorIdAsync(ct);
            if (doctorId <= 0)
            {
                TempData["ErrorMessage"] = "Tài khoản hiện tại chưa được liên kết với hồ sơ bác sĩ.";
                return RedirectToAction(nameof(Index), new { tab = "exceptions" });
            }

            // Parse date hỗ trợ cả yyyy-MM-dd (chuẩn HTML5) và dd/MM/yyyy
            DateOnly targetDate;
            if (string.IsNullOrWhiteSpace(date) ||
                (!DateOnly.TryParse(date, out targetDate) &&
                 !DateOnly.TryParseExact(date, ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out targetDate)))
            {
                TempData["ErrorMessage"] = "Ngày xin nghỉ / đổi ca không đúng định dạng.";
                return RedirectToAction(nameof(Index), new { tab = "exceptions" });
            }

            if (targetDate < DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime))
            {
                TempData["ErrorMessage"] = "Không thể gửi đề nghị nghỉ hoặc đổi ca cho ngày trong quá khứ.";
                return RedirectToAction(nameof(Index), new { tab = "exceptions" });
            }

            if (type == ScheduleExceptionType.ShiftSwap)
            {
                if (!replacementDoctorId.HasValue || replacementDoctorId.Value <= 0 || replacementDoctorId.Value == doctorId)
                {
                    TempData["ErrorMessage"] = "Vui lòng chọn bác sĩ nhận đổi ca hợp lệ.";
                    return RedirectToAction(nameof(Index), new { tab = "exceptions" });
                }

                // Kiểm tra bác sĩ nhận đổi ca phải thuộc cùng khoa chuyên môn
                var currentDoctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == doctorId, ct);
                var repDoctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == replacementDoctorId.Value, ct);

                if (currentDoctor == null || repDoctor == null || currentDoctor.DepartmentId != repDoctor.DepartmentId)
                {
                    TempData["ErrorMessage"] = "Chỉ được phép đổi ca với bác sĩ thuộc cùng khoa chuyên môn.";
                    return RedirectToAction(nameof(Index), new { tab = "exceptions" });
                }
            }

            var now = _timeProvider.GetLocalNow().DateTime;

            var exception = new ScheduleException
            {
                DoctorId = doctorId,
                Date = targetDate,
                Shift = shift,
                Type = type,
                Status = ScheduleExceptionStatus.Pending,
                ReplacementDoctorId = (type == ScheduleExceptionType.ShiftSwap && replacementDoctorId.HasValue && replacementDoctorId.Value > 0)
                    ? replacementDoctorId.Value
                    : null,
                Reason = string.IsNullOrWhiteSpace(reason) ? "Nghỉ việc riêng" : reason.Trim(),
                CreatedAt = now
            };

            _context.ScheduleExceptions.Add(exception);
            await _context.SaveChangesAsync(ct);
            TempData["SuccessMessage"] = "Đã gửi đề nghị nghỉ / đổi ca thành công. Đang chờ trưởng khoa phê duyệt.";
        }
        catch (Exception ex) when (ex is InvalidOperationException)
        {
            TempData["ErrorMessage"] = "Không thể gửi đề nghị: " + ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi gửi đề nghị nghỉ/đổi ca.");
            TempData["ErrorMessage"] = "Đã xảy ra lỗi trong quá trình gửi đề nghị. Vui lòng thử lại sau.";
        }

        return RedirectToAction(nameof(Index), new { tab = "exceptions" });
    }

    // 5. Bác sĩ tự đăng ký ca khám (Shift Bidding)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterShiftBid(int periodId, DateOnly workDate, ExamShift shift, string? doctorNote, CancellationToken ct)
    {
        var doctorId = await ResolveDoctorIdAsync(ct);
        if (doctorId <= 0)
        {
            TempData["ErrorMessage"] = "Không tìm thấy hồ sơ bác sĩ của bạn.";
            return RedirectToAction(nameof(Index), new { tab = "schedule" });
        }

        if (periodId <= 0)
        {
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == doctorId, ct);
            if (doctor != null)
            {
                var activePeriod = await _departmentHeadService.GetActiveSchedulingPeriodAsync(doctor.DepartmentId, ct);
                if (activePeriod != null)
                {
                    periodId = activePeriod.Id;
                }
            }
        }

        var request = new RegisterShiftBidRequest(doctorId, periodId, workDate, shift, doctorNote);
        var result = await _departmentHeadService.RegisterShiftBidAsync(request, ct);

        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["SuccessMessage"] = $"Đã đăng ký ca khám ngày {workDate:dd/MM/yyyy} ({shift}) thành công!";
        }

        return RedirectToAction(nameof(Index), new { tab = "schedule" });
    }

    // 6. Bác sĩ hủy ca đăng ký (khi còn ở trạng thái BIDDING)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelShiftBid(int scheduleId, CancellationToken ct)
    {
        var doctorId = await ResolveDoctorIdAsync(ct);
        var result = await _departmentHeadService.CancelShiftBidAsync(doctorId, scheduleId, ct);

        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error.Message;
        }
        else
        {
            TempData["SuccessMessage"] = "Đã hủy ca đăng ký thành công.";
        }

        return RedirectToAction(nameof(Index), new { tab = "schedule" });
    }

    private int GetCurrentAccountId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : 0;
    }

    private async Task<int> ResolveDoctorIdAsync(CancellationToken ct = default)
    {
        var claim = User.FindFirst("DoctorId")?.Value;
        if (int.TryParse(claim, out var doctorId) && doctorId > 0)
        {
            return doctorId;
        }

        var accountId = GetCurrentAccountId();
        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.AccountId == accountId, ct);
        if (doctor != null)
        {
            return doctor.Id;
        }

        return 0;
    }
}
