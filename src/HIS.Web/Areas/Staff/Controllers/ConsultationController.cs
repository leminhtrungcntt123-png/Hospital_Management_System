using System.Security.Claims;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
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
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ConsultationController(
        IDoctorService doctorService,
        IQueueService queueService,
        AppDbContext context,
        TimeProvider? timeProvider = null)
    {
        _doctorService = doctorService;
        _queueService = queueService;
        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    // 1. Màn hình Bác sĩ & Trưởng khoa: Phân luồng dữ liệu chuẩn theo phân quyền
    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? date, ExamShift? shift, string? tab, int? doctorId, int weekOffset = 0, CancellationToken ct = default)
    {
        try
        {
            var isDeptHead = User.IsInRole("DEPARTMENT_HEAD") || User.IsInRole("DEPT_HEAD");
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
                    .Include(d => d.DoctorSchedules).ThenInclude(s => s.Room)
                    .FirstOrDefaultAsync(d => d.AccountId == accountId, ct);

                if (doc != null)
                {
                    currentDoctorId = doc.Id;
                    departmentId = doc.DepartmentId;
                    departmentName = doc.Department.Name;
                    currentDoctorName = doc.FullName;
                    currentDoctorRoom = doc.DoctorSchedules.FirstOrDefault()?.Room?.RoomNumber ?? doc.LicenseNumber ?? "P.201";
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
                    currentDoctorRoom = fallbackDoc?.LicenseNumber ?? "";
                }
            }

            var selectedDate = date ?? DateOnly.FromDateTime(DateTime.Today);
            var selectedShift = shift ?? (DateTime.Now.Hour < 12 ? ExamShift.Morning : ExamShift.Afternoon);
            var activeTab = string.IsNullOrWhiteSpace(tab) ? "consultation" : tab.ToLowerInvariant();

            // 1. Hàng đợi bệnh nhân đang chờ khám từ Database
            var queueQuery = _context.Appointments
                .AsNoTracking()
                .Include(a => a.Patient)
                .Include(a => a.VitalSigns)
                .Include(a => a.Doctor)
                .Include(a => a.Slot)
                .Where(a => a.StatusId == (int)AppointmentStatusId.ReadyForDoctor ||
                            a.StatusId == (int)AppointmentStatusId.CheckedIn ||
                            a.StatusId == (int)AppointmentStatusId.InProgress);

            if (isDeptHead)
            {
                // Trưởng khoa: Xem bệnh nhân thuộc khoa mình
                queueQuery = queueQuery.Where(a => (a.Doctor != null && a.Doctor.DepartmentId == departmentId) || a.DepartmentId == departmentId);

                // Nếu trưởng khoa chọn lọc theo 1 bác sĩ cụ thể trong khoa:
                if (doctorId.HasValue && doctorId.Value > 0)
                {
                    queueQuery = queueQuery.Where(a => a.DoctorId == doctorId.Value);
                }
            }
            else
            {
                // Bác sĩ thường: CHỈ THẤY BỆNH NHÂN THEO PHÒNG / BÁC SĨ CỦA MÌNH
                queueQuery = queueQuery.Where(a => a.DoctorId == currentDoctorId);
            }

            var queue = await queueQuery
                .OrderBy(a => a.PriorityLevel)
                .ThenBy(a => a.QueueNumber)
                .Select(a => new ConsultationQueueItemDto(
                    a.Id,
                    a.AppointmentCode,
                    a.Patient.FullName,
                    DateTime.Today.Year - a.Patient.DateOfBirth.Year,
                    a.Patient.Gender == Gender.Male ? "Nam" : "Nữ",
                    a.QueueNumber ?? "—",
                    a.PriorityLevel,
                    a.CheckedInAt,
                    a.Reason ?? "Khám bệnh theo lịch hẹn",
                    (string?)null,
                    a.VitalSigns != null ? a.VitalSigns.Pulse : null,
                    a.VitalSigns != null && a.VitalSigns.SystolicBp.HasValue && a.VitalSigns.DiastolicBp.HasValue
                        ? $"{a.VitalSigns.SystolicBp}/{a.VitalSigns.DiastolicBp}"
                        : null,
                    a.VitalSigns != null ? a.VitalSigns.Temperature : null,
                    a.VitalSigns != null ? a.VitalSigns.Spo2 : null,
                    a.VitalSigns != null ? a.VitalSigns.Weight : null,
                    a.Doctor != null ? a.Doctor.FullName : "Chuyên khoa",
                    a.Slot != null && a.Slot.Room != null ? a.Slot.Room.RoomNumber : (a.Doctor != null ? (a.Doctor.LicenseNumber ?? "") : "")
                ))
                .ToListAsync(ct);

            // 2. Danh sách Chỉ định & kết quả từ Database
            var labsQuery = _context.LabOrders
                .AsNoTracking()
                .Include(l => l.Service)
                .Include(l => l.MedicalRecord).ThenInclude(r => r.Appointment).ThenInclude(a => a.Patient)
                .Include(l => l.MedicalRecord).ThenInclude(r => r.Doctor)
                .Include(l => l.Result)
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
                    DateTime.Today.Year - l.MedicalRecord.Appointment.Patient.DateOfBirth.Year,
                    l.Service.Name,
                    l.Note ?? "Chỉ định chuyên khoa",
                    l.CreatedAt,
                    l.Status == LabOrderStatus.Completed ? "Đã có kết quả" : "Chờ kết quả",
                    l.Result != null ? l.Result.Result : null
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
                    DateTime.Today.Year - r.Appointment.Patient.DateOfBirth.Year,
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
                .Select(s => new DoctorScheduleItemDto(
                    s.DayOfWeek,
                    s.Shift,
                    s.StartTime,
                    s.EndTime,
                    s.Doctor.FullName,
                    s.Room.RoomNumber,
                    s.Doctor.Specialty ?? "Chuyên khoa",
                    s.DoctorId
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
                QueuePatients = queue,
                ActivePatient = queue.FirstOrDefault(),
                LabOrders = labs,
                MedicalRecords = records,
                DoctorSchedules = schedules,
                DutyShifts = dutyShifts,
                Exceptions = exceptions,
                AvailableDoctors = availableDoctors
            };

            return View(vm);
        }
        catch (OperationCanceledException)
        {
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

            var recordRequest = request with { DoctorId = doctorId };
            await _doctorService.CreateMedicalRecordAsync(recordRequest, ct);
            TempData["SuccessMessage"] = "Đã lưu hồ sơ bệnh án và hoàn thành lượt khám.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
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

            DateOnly targetDate;
            if (string.IsNullOrWhiteSpace(date) ||
                (!DateOnly.TryParse(date, out targetDate) &&
                 !DateOnly.TryParseExact(date, ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out targetDate)))
            {
                TempData["ErrorMessage"] = "Ngày xin nghỉ / đổi ca không đúng định dạng.";
                return RedirectToAction(nameof(Index), new { tab = "exceptions" });
            }

            if (targetDate < DateOnly.FromDateTime(DateTime.Today))
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

                var currentDoctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == doctorId, ct);
                var repDoctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == replacementDoctorId.Value, ct);

                if (currentDoctor == null || repDoctor == null || currentDoctor.DepartmentId != repDoctor.DepartmentId)
                {
                    TempData["ErrorMessage"] = "Chỉ được phép đổi ca với bác sĩ thuộc cùng khoa chuyên môn.";
                    return RedirectToAction(nameof(Index), new { tab = "exceptions" });
                }
            }

            var now = DateTime.SpecifyKind(_timeProvider.GetLocalNow().DateTime, DateTimeKind.Unspecified);

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
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Không thể gửi đề nghị: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), new { tab = "exceptions" });
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

