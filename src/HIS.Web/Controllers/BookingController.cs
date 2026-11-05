using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using HIS.Domain;
using HIS.Web.Helpers;
using HIS.Web.ViewModels;
using HIS.Web.ViewModels.Booking;
using Microsoft.AspNetCore.Mvc;
using HIS.BLL.Interfaces;
using HIS.BLL.DTOs;
namespace HIS.Web.Controllers;


// Quy trình đặt lịch khám cho bệnh nhân (web).
// Controller chỉ điều hướng và lưu lựa chọn vào Session; dữ liệu và quy tắc nghiệp vụ nằm ở HIS.BLL
// nên WinForms (quầy tiếp nhận) dùng chung cùng một logic.
//
// Các bước: Index -> ChonKhoa -> ChonNgayCa -> ChonGio -> ChonNguoiKham -> XacNhan -> ThanhCong
public class BookingController : PatientPageController
{
    private const int DaysToShow = 7;

    // Session keys để lưu trạng thái đặt lịch (BookingState) và kết quả đặt lịch thành công (ThanhCongViewModel).
    private const string StateKey = "BookingState";
    private const string LastBookingKey = "LastBooking";

    private static readonly Regex PhoneRegex = new(@"^0\d{9,10}$", RegexOptions.Compiled);

    // Mã quan hệ chấp nhận từ form "Thêm hồ sơ"; BLL tự chuyển sang nhãn tiếng Việt.
    private static readonly HashSet<string> RelationshipCodes =
        new(StringComparer.OrdinalIgnoreCase) { "SELF", "PARENT", "CHILD", "SPOUSE", "SIBLING", "OTHER" };

    private readonly IBookingService _bookingService;
    private readonly IPatientProfileService _profileService;
    private readonly IPatientAccessService _accessService;

    public BookingController(
        IBookingService bookingService,
        IPatientProfileService profileService,
        IPatientAccessService accessService)
    {
        _bookingService = bookingService;
        _profileService = profileService;
        _accessService = accessService;
    }

    // ------------------------------------------------------------ A1: Trang bắt đầu

    public IActionResult Index() => View();

    // ------------------------------------------------------------ Bước 1: Chọn chuyên khoa

    [HttpGet]
    public async Task<IActionResult> ChonKhoa()
    {
        if (RequireAccount() is { } signIn) return signIn;

        var state = GetState();
        // Nhận DTOs từ BookingService (BLL) rồi map sang ViewModels để trả về View.
        var departments = await _bookingService.GetActiveDepartmentsAsync();

        // Map DTO (BLL) sang ViewModel để trả về View.
        var departmentVms = departments.Select(ToDepartmentVm).ToList();

        var selected = departmentVms.FirstOrDefault(d => d.Id == state.DepartmentId && d.SlotsThisWeek > 0);

        return View(new ChonKhoaViewModel
        {
            Departments = departmentVms,
            SelectedDepartmentId = selected?.Id
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChonKhoa(int departmentId)
    {
        if (RequireAccount() is { } signIn) return signIn;

        // Nhận DTOs từ BookingService (BLL) rồi map sang ViewModels để kiểm tra số chỗ còn.
        var dept = await _bookingService.GetDepartmentByIdAsync(departmentId);
        if (dept is null || dept.SlotsThisWeek == 0)
        {
            TempData["Error"] = "Chuyên khoa này chưa có lịch khả dụng. Vui lòng chọn chuyên khoa khác.";
            return RedirectToAction(nameof(ChonKhoa));
        }

        var state = GetState();
        if (state.DepartmentId != departmentId)
        {
            // Đổi chuyên khoa thì lịch cũ không còn đúng; hồ sơ người khám vẫn giữ lại.
            state.Date = null;
            state.Shift = null;
            state.SlotId = null;
        }
        state.DepartmentId = departmentId;
        SaveState(state);

        return RedirectToAction(nameof(ChonNgayCa));
    }

    // ------------------------------------------------------------ Bước 2: Chọn ngày và ca

    [HttpGet]
    public async Task<IActionResult> ChonNgayCa()
    {
        if (RequireAccount() is { } signIn) return signIn;

        var state = GetState();
        // Kiểm tra DepartmentId trong Session trước để lấy ra biến deptId
        if (state.DepartmentId is not int deptId)
            return RedirectToAction(nameof(ChonKhoa));

        // Sau khi đã có deptId mới truyền vào BLL và lấy DTOs
        var dept = await _bookingService.GetDepartmentByIdAsync(deptId);
        if (dept is null)
            return RedirectToAction(nameof(ChonKhoa));

        var from = FirstBookableDate();
        // Lấy trạng thái khả dụng của các ngày từ BookingService (BLL) rồi map sang ViewModels.
        var days = await _bookingService.GetDaysAsync(dept.Id, from, DaysToShow);
        var dayVms = days.Select(d => new DayVm { Date = d.Date, State = d.State }).ToList();

        var pickable = dayVms.Where(d => d.State != "off").ToList();
        if (pickable.Count == 0)
        {
            TempData["Error"] = "Chuyên khoa này chưa mở lịch trong tuần tới. Vui lòng chọn chuyên khoa khác.";
            return RedirectToAction(nameof(ChonKhoa));
        }

        DateOnly selected = state.Date is { } saved && pickable.Any(d => d.Date == saved)
            ? saved
            : (pickable.FirstOrDefault(d => d.State != "full") ?? pickable[0]).Date;

        return View(new ChonNgayCaViewModel
        {
            Department = ToDepartmentVm(dept),
            Days = dayVms,
            MonthLabel = $"Tháng {selected.ToString("MM/yyyy", CultureInfo.InvariantCulture)}",
            ShiftList = await BuildShiftListAsync(dept.Id, dept.Name, selected, state.Date == selected ? state.Shift : null)
        });
    }

    // Trả về HTML danh sách ca khám của một ngày (booking.js gọi bằng fetch).
    [HttpGet]
    public async Task<IActionResult> CaKham(string? date)
    {
        var state = GetState();
        if (state.DepartmentId is not int deptId)
            return BadRequest();

        // gọi BLL lấy thông tin khoa
        var dept = await _bookingService.GetDepartmentByIdAsync(deptId);
        if (dept is null)
            return BadRequest();

        // Kiểm tra ngày hợp lệ
        if (!TryParseDate(date, out var d) || !IsBookableDate(d))
            return BadRequest();

        // Gọi BuildShiftListAsync (bất đồng bộ) để lấy danh sách ca từ BLL
        var shiftListVm = await BuildShiftListAsync(dept.Id, dept.Name, d, state.Date == d ? state.Shift : null);

        return PartialView("_ShiftList", shiftListVm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChonNgayCa(string? date, string? shift)
    {
        if (RequireAccount() is { } signIn) return signIn;

        var state = GetState();
        if (state.DepartmentId is not int deptId)
            return RedirectToAction(nameof(ChonKhoa));

        if (!TryParseDate(date, out var d) || !IsBookableDate(d))
        {
            TempData["Error"] = "Ngày khám không hợp lệ. Vui lòng chọn lại.";
            return RedirectToAction(nameof(ChonNgayCa));
        }

        // Gọi BLL lấy danh sách ca khám thực tế trong DB
        var shifts = await _bookingService.GetShiftsAsync(deptId, d);
        var chosen = shifts.FirstOrDefault(s => s.Key == shift);

        // Kiểm tra ca khám có hợp lệ và còn suất đặt online (Status == "AVAILABLE") không
        if (chosen is null || !chosen.IsSelectable)
        {
            TempData["Error"] = "Ca khám này không còn chỗ trống. Vui lòng chọn ca khác.";
            return RedirectToAction(nameof(ChonNgayCa));
        }

        // Đổi ngày hoặc đổi ca thì khung giờ đã chọn ở bước trước không còn đúng.
        if (state.Date != d || state.Shift != chosen.Key) state.SlotId = null;

        state.Date = d;
        state.Shift = chosen.Key;
        SaveState(state);

        return RedirectToAction(nameof(ChonGio));
    }

    // ------------------------------------------------------------ Bước 3: Chọn khung giờ

    [HttpGet]
    public async Task<IActionResult> ChonGio()
    {
        var state = GetState();
        if (Guard(state, throughStep: 2) is { } redirect) return redirect;

        var slotList = await BuildSlotListAsync(state);
        if (slotList is null)
        {
            TempData["Error"] = "Ca khám này vừa hết chỗ. Vui lòng chọn ngày hoặc ca khác.";
            return RedirectToAction(nameof(ChonNgayCa));
        }

        int? selectedId = state.SlotId is { } saved && slotList.Slots.Any(s => s.Id == saved && s.IsSelectable)
            ? saved
            : slotList.Slots.FirstOrDefault(s => s.IsSelectable)?.Id;

        return View(new ChonGioViewModel
        {
            Department = slotList.Department,
            Date = state.Date!.Value,
            Shift = slotList.Shift,
            Slots = slotList.Slots,
            SelectedSlotId = selectedId
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChonGio(string? slotId)
    {
        var state = GetState();
        if (Guard(state, throughStep: 2) is { } redirect) return redirect;

        if (!int.TryParse(slotId, out var chosenSlotId))
        {
            TempData["Error"] = "Vui lòng chọn một khung giờ khám.";
            return RedirectToAction(nameof(ChonGio));
        }

        // Kiểm tra lại từ DB: khung giờ này thuộc đúng ca/ngày đã chọn và còn suất online.
        var slots = await _bookingService.GetSlotsAsync(state.DepartmentId!.Value, state.Date!.Value, state.Shift!);
        var chosen = slots.FirstOrDefault(s => s.Id == chosenSlotId);
        if (chosen is null || !chosen.IsSelectable)
        {
            TempData["Error"] = "Khung giờ này vừa hết suất đặt online. Vui lòng chọn khung khác.";
            return RedirectToAction(nameof(ChonGio));
        }

        state.SlotId = chosen.Id;
        SaveState(state);

        return RedirectToAction(nameof(ChonNguoiKham));
    }

    // ------------------------------------------------------------ Bước 4: Chọn người khám

    [HttpGet]
    public async Task<IActionResult> ChonNguoiKham()
    {
        var state = GetState();
        if (Guard(state, throughStep: 3) is { } redirect) return redirect;

        var profiles = await GetProfilesAsync();

        int? selectedId = profiles.Any(p => p.Id == state.PatientId && p.IsBookable)
            ? state.PatientId
            : profiles.FirstOrDefault(p => p.RelationshipCode == "SELF" && p.IsBookable)?.Id;

        return View(new ChonNguoiKhamViewModel { Patients = profiles, SelectedPatientId = selectedId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChonNguoiKham(int patientId)
    {
        var state = GetState();
        if (Guard(state, throughStep: 3) is { } redirect) return redirect;

        var patient = (await GetProfilesAsync()).FirstOrDefault(p => p.Id == patientId);

        if (patient is null)
        {
            TempData["Error"] = "Không tìm thấy hồ sơ người khám. Vui lòng chọn lại.";
            return RedirectToAction(nameof(ChonNguoiKham));
        }
        if (!patient.IsBookable)
        {
            TempData["Error"] = "Hồ sơ này đã bị thu hồi quyền đặt lịch. Vui lòng chọn hồ sơ khác.";
            return RedirectToAction(nameof(ChonNguoiKham));
        }

        state.PatientId = patient.Id;
        SaveState(state);
        return RedirectToAction(nameof(XacNhan));
    }

    // Thêm hồ sơ mới ngay trong quy trình đặt lịch; giữ nguyên khoa, ngày, ca, giờ đã chọn.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ThemHoSo(ThemHoSoRequest request)
    {
        var state = GetState();
        if (Guard(state, throughStep: 3) is { } redirect) return redirect;

        // 1. Kiểm tra Form Validation (ViewModel đảm nhận qua Data Annotations)
        if (!ModelState.IsValid)
        {
            TempData["Error"] = ModelState.Values
                .SelectMany(v => v.Errors)
                .FirstOrDefault()?.ErrorMessage ?? "Dữ liệu nhập vào không hợp lệ.";
            return RedirectToAction(nameof(ChonNguoiKham));
        }

        var name = request.FullName?.Trim() ?? string.Empty;
        var phone = (request.Phone ?? string.Empty).Replace(" ", "");
        if (!PhoneRegex.IsMatch(phone))
        {
            TempData["Error"] = "Số điện thoại phải từ 10-11 số và bắt đầu bằng 0.";
            return RedirectToAction(nameof(ChonNguoiKham));
        }

        // Ngày sinh phải parse được (ModelState đã ràng buộc định dạng dd/MM/yyyy)
        if (!DateOnly.TryParseExact(request.DateOfBirth?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
        {
            TempData["Error"] = "Ngày sinh không hợp lệ. Vui lòng nhập lại.";
            return RedirectToAction(nameof(ChonNguoiKham));
        }

        var relationship = request.Relationship is { } rel && RelationshipCodes.Contains(rel)
            ? rel.ToUpperInvariant()
            : "OTHER";

        // 2. Lưu hồ sơ + liên kết xuống CSDL qua BLL. BLL chặn CCCD đã tồn tại và trả lý do bằng tiếng Việt.
        var created = await _profileService.CreateProfileAsync(AccountId, new CreateProfileRequest
        {
            FullName = name,
            DateOfBirth = dob,
            Gender = string.IsNullOrWhiteSpace(request.Gender) ? "OTHER" : request.Gender.ToUpperInvariant(),
            PhoneNumber = phone,
            NationalId = string.IsNullOrWhiteSpace(request.NationalId) ? null : request.NationalId.Trim(),
            Relationship = relationship
        });

        if (!created.Success)
        {
            TempData["Error"] = created.Message;
            return RedirectToAction(nameof(ChonNguoiKham));
        }

        state.PatientId = created.PatientId;
        SaveState(state);
        return RedirectToAction(nameof(ChonNguoiKham));
    }

    // ------------------------------------------------------------ Bước 5: Xác nhận

    [HttpGet]
    public async Task<IActionResult> XacNhan()
    {
        var state = GetState();
        if (Guard(state, throughStep: 4) is { } redirect) return redirect;

        var dept = await _bookingService.GetDepartmentByIdAsync(state.DepartmentId!.Value);
        if (dept is null) return RedirectToAction(nameof(ChonKhoa));

        var patient = (await GetProfilesAsync()).FirstOrDefault(p => p.Id == state.PatientId);
        if (patient is null) return RedirectToAction(nameof(ChonNguoiKham));

        // Kiểm tra lần cuối khung giờ từ BLL; hết suất thì đưa về bước chọn giờ, dữ liệu khác vẫn giữ.
        var slot = await FindSelectedSlotAsync(state);
        if (slot is null)
        {
            TempData["Error"] = "Khung giờ vừa hết suất. Vui lòng chọn lại giờ, các thông tin còn lại vẫn được giữ.";
            return RedirectToAction(nameof(ChonGio));
        }

        var shift = await GetSelectedShiftAsync(state);
        if (shift is null) return RedirectToAction(nameof(ChonNgayCa));

        var fee = await _bookingService.GetExaminationFeeAsync(dept.Id);

        return View(new XacNhanViewModel
        {
            Patient = patient,
            Department = ToDepartmentVm(dept),
            Date = state.Date!.Value,
            Shift = ToShiftVm(shift),
            Slot = ToSlotVm(slot),
            Fee = fee
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> XacNhan(bool agreed)
    {
        var state = GetState();
        if (Guard(state, throughStep: 4) is { } redirect) return redirect;

        var patient = (await GetProfilesAsync()).FirstOrDefault(p => p.Id == state.PatientId);
        if (patient is null) return RedirectToAction(nameof(ChonNguoiKham));

        var dept = await _bookingService.GetDepartmentByIdAsync(state.DepartmentId!.Value);
        if (dept is null) return RedirectToAction(nameof(ChonKhoa));

        if (!agreed)
        {
            TempData["Error"] = "Vui lòng xác nhận thông tin và đồng ý với quy định đặt lịch khám để tiếp tục.";
            return RedirectToAction(nameof(XacNhan));
        }

        // Quyền đặt lịch chỉ do PatientAccessService quyết định (link ACTIVE hoặc PENDING).
        if (!await _accessService.CanBookAsync(AccountId, patient.Id))
        {
            TempData["Error"] = "Hồ sơ này không còn quyền đặt lịch. Vui lòng chọn hồ sơ khác.";
            return RedirectToAction(nameof(ChonNguoiKham));
        }

        // Kiểm tra lần cuối từ Database: khung giờ còn suất online không?
        var slot = await FindSelectedSlotAsync(state);
        if (slot is null)
        {
            TempData["Error"] = "Khung giờ vừa hết suất trước khi bạn xác nhận. Vui lòng chọn lại giờ.";
            return RedirectToAction(nameof(ChonGio));
        }

        // BLL giữ một suất A của khung giờ, sinh số thứ tự và kiểm tra hạn mức trong cùng một giao dịch.
        Appointment created;
        try
        {
            created = await _bookingService.CreateAppointmentAsync(AccountId, patient.Id, slot.Id);
        }
        catch (InvalidOperationException blocked)
        {
            // Tài khoản bị chặn vì quá số lần vắng mặt, hoặc ca vừa có người đặt trước.
            TempData["Error"] = blocked.Message;
            return RedirectToAction(nameof(XacNhan));
        }

        HttpContext.Session.SetObject(LastBookingKey, new ThanhCongViewModel
        {
            Code = created.AppointmentCode,
            Status = "CONFIRMED",
            Patient = patient,
            DepartmentName = dept.Name,
            Date = state.Date!.Value,
            ShiftLabel = slot.ShiftLabel,
            TimeRange = slot.Label,
            QueueNumber = created.QueueNumber ?? string.Empty,
            Doctor = ToSlotVm(slot).Doctor,
            Room = slot.RoomName ?? string.Empty
        });

        // Đặt thành công -> reset trạng thái chọn lịch (vẫn giữ PatientId cho lần sau)
        SaveState(new BookingState { PatientId = state.PatientId });

        return RedirectToAction(nameof(ThanhCong));
    }

    // ------------------------------------------------------------ Bước 6: Thành công

    [HttpGet]
    public IActionResult ThanhCong()
    {
        var result = HttpContext.Session.GetObject<ThanhCongViewModel>(LastBookingKey);
        return result is null ? RedirectToAction(nameof(Index)) : View(result);
    }

    // ------------------------------------------------------------ Lỗi

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    // ------------------------------------------------------------ Helpers

    private BookingState GetState()
        => HttpContext.Session.GetObject<BookingState>(StateKey) ?? new BookingState();

    private void SaveState(BookingState state)
        => HttpContext.Session.SetObject(StateKey, state);

    // Hồ sơ của tài khoản đang đăng nhập, kèm nhãn quan hệ và trạng thái liên kết từ DB.
    private async Task<List<PatientVm>> GetProfilesAsync()
        => (await _profileService.GetLinksForAccountAsync(AccountId))
            .Where(l => l.StatusCode != "REVOKED")
            .Select(l => new PatientVm
            {
                Id = l.PatientId,
                FullName = l.PatientFullName,
                Relationship = l.RelationshipLabel,
                RelationshipCode = l.RelationshipCode,
                Code = l.PatientCode,
                DateOfBirth = l.DateOfBirth,
                Phone = l.PatientPhone,
                IsVerified = l.StatusCode == "ACTIVE",
                IsBookable = l.StatusCode is "ACTIVE" or "PENDING",
                NeedsApproval = l.StatusCode == "PENDING"
            })
            .OrderByDescending(p => p.IsVerified)
            .ThenBy(p => p.FullName)
            .ToList();

    // Nếu người dùng nhảy bước khi chưa chọn đủ thì đưa về bước còn thiếu.
    private IActionResult? Guard(BookingState state, int throughStep)
    {
        if (RequireAccount() is { } signIn) return signIn;
        if (throughStep >= 1 && state.DepartmentId is null) return RedirectToAction(nameof(ChonKhoa));
        if (throughStep >= 2 && (state.Date is null || string.IsNullOrEmpty(state.Shift))) return RedirectToAction(nameof(ChonNgayCa));
        if (throughStep >= 3 && state.SlotId is null) return RedirectToAction(nameof(ChonGio));
        if (throughStep >= 4 && state.PatientId is null) return RedirectToAction(nameof(ChonNguoiKham));
        return null;
    }

    // Khung giờ đang chọn, đối chiếu lại với DB (đúng khoa + ngày + ca và còn suất online).
    private async Task<BookingSlotDto?> FindSelectedSlotAsync(BookingState state)
    {
        if (state.SlotId is not int slotId) return null;
        var slots = await _bookingService.GetSlotsAsync(state.DepartmentId!.Value, state.Date!.Value, state.Shift!);
        return slots.FirstOrDefault(s => s.Id == slotId && s.IsSelectable);
    }

    private async Task<BookingShiftDto?> GetSelectedShiftAsync(BookingState state)
    {
        var shifts = await _bookingService.GetShiftsAsync(state.DepartmentId!.Value, state.Date!.Value);
        return shifts.FirstOrDefault(s => s.Key == state.Shift);
    }

    private static DepartmentVm ToDepartmentVm(BookingDepartmentDto d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Description = d.Description ?? string.Empty,
        SlotsThisWeek = d.SlotsThisWeek
    };

    private static ShiftVm ToShiftVm(BookingShiftDto s) => new()
    {
        Key = s.Key,
        Label = s.Label,
        TimeRange = s.TimeRange,
        SlotsLeft = s.SlotsLeft,
        Status = s.Status,
        Doctor = s.DoctorId is { } docId
            ? new DoctorVm
            {
                Id = docId,
                FullName = s.DoctorName ?? string.Empty,
                Title = s.DoctorTitle ?? string.Empty,
                Specialty = s.DoctorTitle ?? string.Empty
            }
            : null,
        Room = s.RoomName ?? string.Empty
    };

    private static SlotVm ToSlotVm(BookingSlotDto s) => new()
    {
        Id = s.Id,
        Label = s.Label,
        Status = s.Status,
        SlotsLeft = s.SlotsLeft,
        OnlineQuota = s.OnlineQuota,
        Room = s.RoomName ?? string.Empty,
        Doctor = new DoctorVm
        {
            Id = s.DoctorId,
            FullName = s.DoctorName ?? string.Empty,
            Title = s.DoctorTitle ?? string.Empty,
            Specialty = s.DoctorTitle ?? string.Empty
        }
    };

    private async Task<SlotListViewModel?> BuildSlotListAsync(BookingState state)
    {
        var dept = await _bookingService.GetDepartmentByIdAsync(state.DepartmentId!.Value);
        var shift = await GetSelectedShiftAsync(state);
        if (dept is null || shift is null) return null;

        var slots = await _bookingService.GetSlotsAsync(dept.Id, state.Date!.Value, shift.Key);
        if (slots.Count == 0) return null;

        return new SlotListViewModel
        {
            Department = ToDepartmentVm(dept),
            Shift = ToShiftVm(shift),
            Slots = slots.Select(ToSlotVm).ToList()
        };
    }

    private async Task<ShiftListViewModel> BuildShiftListAsync(int departmentId, string departmentName, DateOnly date, string? selectedShift)
    {
        var shifts = await _bookingService.GetShiftsAsync(departmentId, date);
        var vms = shifts.Select(ToShiftVm).ToList();
        var valid = vms.Any(s => s.Key == selectedShift && s.IsSelectable) ? selectedShift : null;
        return new ShiftListViewModel
        {
            DepartmentName = departmentName,
            Date = date,
            Shifts = vms,
            SelectedShift = valid
        };
    }

    private static DateOnly FirstBookableDate() => DateOnly.FromDateTime(DateTime.Today).AddDays(1);

    private static bool IsBookableDate(DateOnly d)
    {
        var from = FirstBookableDate();
        return d >= from && d < from.AddDays(DaysToShow);
    }

    private static bool TryParseDate(string? text, out DateOnly date)
        => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
