using System.Diagnostics;
using System.Globalization;
using HIS.Domain;
using HIS.Web.Helpers;
using HIS.Web.Realtime;
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
// Ba đường vào cùng hội tụ về một slotId:
//   theo chuyên khoa: ChonKhoa -> ChonBacsi -> ChonNgayGio -> ChonNguoiKham -> XacNhan
//   theo ngày:        ChonNgay -> ChonKhoa  -> ChonBacsi (kèm khung giờ) -> ChonNguoiKham -> XacNhan
//   theo bác sĩ:      ChonBacsi -> ChonNgayGio -> ChonNguoiKham -> XacNhan
public class BookingController : PatientPageController
{
    // Session keys để lưu trạng thái đặt lịch (BookingState) và kết quả đặt lịch thành công (ThanhCongViewModel).
    private const string StateKey = "BookingState";
    private const string LastBookingKey = "LastBooking";

    /// <summary>
    /// Thứ tự bước của từng luồng, kèm nhãn hiển thị trên stepper. Một chỗ duy nhất quyết định
    /// bước nào nối tiếp bước nào nên thêm/bớt bước không phải sửa từng view.
    /// </summary>
    private static readonly Dictionary<string, (string Action, string Label)[]> Wizard = new()
    {
        [BookingModes.ByDepartment] = new[]
        {
            (nameof(ChonKhoa), "Chuyên khoa"),
            (nameof(ChonBacsi), "Bác sĩ"),
            (nameof(ChonNgayGio), "Ngày & giờ"),
            (nameof(ChonNguoiKham), "Người khám"),
            (nameof(XacNhan), "Xác nhận"),
        },
        [BookingModes.ByDate] = new[]
        {
            (nameof(ChonNgay), "Ngày khám"),
            (nameof(ChonKhoa), "Chuyên khoa"),
            (nameof(ChonBacsi), "Bác sĩ & giờ"),
            (nameof(ChonNguoiKham), "Người khám"),
            (nameof(XacNhan), "Xác nhận"),
        },
        [BookingModes.ByDoctor] = new[]
        {
            (nameof(ChonBacsi), "Bác sĩ"),
            (nameof(ChonNgayGio), "Ngày & giờ"),
            (nameof(ChonNguoiKham), "Người khám"),
            (nameof(XacNhan), "Xác nhận"),
        },
    };

    private readonly IBookingService _bookingService;
    private readonly IPatientProfileService _profileService;
    private readonly IPatientAccessService _accessService;
    private readonly QueueChangeNotifier _realtime;

    public BookingController(
        IBookingService bookingService,
        IPatientProfileService profileService,
        IPatientAccessService accessService,
        QueueChangeNotifier realtime)
    {
        _bookingService = bookingService;
        _profileService = profileService;
        _accessService = accessService;
        _realtime = realtime;
    }

    // ------------------------------------------------------------ A1: Trang bắt đầu, ba lối vào

    public IActionResult Index() => View();

    /// <summary>Bắt đầu một luồng. Đổi luồng thì mọi lựa chọn lịch cũ bị bỏ (hồ sơ vẫn giữ).</summary>
    public IActionResult Start(string mode)
    {
        if (!BookingModes.IsValid(mode)) return RedirectToAction(nameof(Index));

        var state = GetState();
        if (state.Mode != mode)
        {
            SaveState(new BookingState { Mode = mode, PatientId = state.PatientId });
            return RedirectToAction(StepsOf(mode)[0].Action);
        }

        return RedirectToAction(FirstUnfinishedStep(state)?.Action ?? nameof(ChonNguoiKham));
    }

    // ------------------------------------------------------------ Bước "chọn ngày" (luồng theo ngày)

    [HttpGet]
    public async Task<IActionResult> ChonNgay()
    {
        var state = GetState();
        if (Guard(state, nameof(ChonNgay)) is { } redirect) return redirect;

        var from = _bookingService.FirstBookableDate;
        var days = (await _bookingService.GetDaysAsync(from, _bookingService.BookableDayCount))
            .Select(d => new DayVm { Date = d.Date, State = d.State })
            .ToList();

        var selected = PickDate(days, state.Date);
        if (selected is null)
        {
            TempData["Error"] = $"Bệnh viện chưa mở ngày nào còn chỗ đặt online trong {WindowText}.";
            return RedirectToAction(nameof(Index));
        }

        return View(new ChonNgayViewModel
        {
            Days = days,
            SelectedDate = selected.Value,
            MonthLabel = FormatMonth(selected.Value)
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChonNgay(string? date)
    {
        var state = GetState();
        if (Guard(state, nameof(ChonNgay)) is { } redirect) return redirect;

        if (!TryParseDate(date, out var d) || !_bookingService.IsBookableDate(d))
        {
            TempData["Error"] = "Ngày khám không còn đặt được. Vui lòng chọn ngày khác.";
            return RedirectToAction(nameof(ChonNgay));
        }

        // Đổi ngày thì khoa/bác sĩ/giờ đã chọn trước đó không còn đúng.
        state.Date = d;
        state.DepartmentId = null;
        state.DoctorId = null;
        state.DoctorChosen = false;
        state.SlotId = null;
        SaveState(state);

        return RedirectToAction(nameof(ChonKhoa));
    }

    // ------------------------------------------------------------ Bước "chọn chuyên khoa"

    [HttpGet]
    public async Task<IActionResult> ChonKhoa()
    {
        var state = GetState();
        if (Guard(state, nameof(ChonKhoa)) is { } redirect) return redirect;

        // Luồng theo ngày: số chỗ chỉ tính trong ngày đã chọn, khoa nghỉ hôm đó hiện "Chưa có lịch".
        var departments = state.Mode == BookingModes.ByDate && state.Date is { } day
            ? await _bookingService.GetDepartmentsOnDateAsync(day)
            : await _bookingService.GetActiveDepartmentsAsync();

        var departmentVms = departments.Select(ToDepartmentVm).ToList();
        var selected = departmentVms.FirstOrDefault(d => d.Id == state.DepartmentId && d.SlotsAvailable > 0);

        return View(new ChonKhoaViewModel
        {
            Departments = departmentVms,
            SelectedDepartmentId = selected?.Id,
            OnDate = state.Mode == BookingModes.ByDate ? state.Date : null
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChonKhoa(int departmentId)
    {
        var state = GetState();
        if (Guard(state, nameof(ChonKhoa)) is { } redirect) return redirect;

        var dept = await LoadDepartmentAsync(state, departmentId);
        if (dept is null) return RedirectToAction(nameof(ChonKhoa));

        if (state.DepartmentId != departmentId)
        {
            state.DoctorId = null;
            state.DoctorChosen = false;
            state.SlotId = null;
        }
        state.DepartmentId = departmentId;
        SaveState(state);

        return RedirectToAction(nameof(ChonBacsi));
    }

    // ------------------------------------------------------------ Bước "chọn bác sĩ" (+ khung giờ nếu đã chốt ngày)

    [HttpGet]
    public async Task<IActionResult> ChonBacsi(string? q)
    {
        var state = GetState();
        if (Guard(state, nameof(ChonBacsi)) is { } redirect) return redirect;

        var vm = new ChonBacsiViewModel
        {
            Date = state.Mode == BookingModes.ByDate ? state.Date : null,
            ShowAnyDoctor = state.Mode == BookingModes.ByDepartment,
            // "Bác sĩ bất kỳ" không lưu doctorId, nên thẻ đó cần một giá trị để tô trạng thái đã chọn.
            SelectedDoctorId = state.DoctorChosen
                ? state.DoctorId ?? (state.Mode == BookingModes.ByDepartment ? BookingModes.AnyDoctorId : null)
                : null,
            SelectedSlotId = state.SlotId
        };

        if (state.Mode == BookingModes.ByDate && state.Date is { } day)
        {
            vm.Department = ToDepartmentVm(await RequireDepartmentAsync(state));
            vm.DoctorSlots = (await _bookingService.GetDoctorSlotsAsync(state.DepartmentId!.Value, day))
                .Select(ToDoctorSlotsVm)
                .ToList();

            if (vm.DoctorSlots.Count == 0)
            {
                TempData["Error"] = $"Chuyên khoa {vm.Department.Name} không còn ca trực nào ngày {VietnameseFormat.ShortDate(day)}. Vui lòng chọn ngày khác.";
                return RedirectToAction(nameof(ChonNgay));
            }
        }
        else
        {
            // Luồng theo chuyên khoa giới hạn trong khoa; luồng theo bác sĩ tìm trong toàn viện.
            int? departmentFilter = state.Mode == BookingModes.ByDepartment ? state.DepartmentId : null;
            if (departmentFilter is int deptFilter) vm.Department = ToDepartmentVm(await RequireOrEmptyAsync(deptFilter));

            vm.Doctors = (await _bookingService.GetDoctorsAsync(departmentFilter))
                .Select(DoctorVm.From)
                .ToList();
        }

        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChonBacsi(int doctorId, string? slotId)
    {
        var state = GetState();
        if (Guard(state, nameof(ChonBacsi)) is { } redirect) return redirect;

        // "Bác sĩ bất kỳ": bệnh nhân để hệ thống phân công theo lịch trực (chỉ luồng theo chuyên khoa).
        if (doctorId == BookingModes.AnyDoctorId && state.Mode == BookingModes.ByDepartment)
        {
            state.DoctorId = null;
            state.DoctorChosen = true;
            state.SlotId = null;
            SaveState(state);
            return RedirectToAction(nameof(ChonNgayGio));
        }

        var departmentId = state.DepartmentId;
        if (state.Mode == BookingModes.ByDoctor)
        {
            // Chưa biết khoa của bác sĩ cho tới khi đọc danh sách toàn viện.
            var known = await _bookingService.GetDoctorsAsync(null);
            var picked = known.FirstOrDefault(d => d.Id == doctorId);
            if (picked is null)
            {
                TempData["Error"] = "Không tìm thấy bác sĩ vừa chọn. Vui lòng chọn lại.";
                return RedirectToAction(nameof(ChonBacsi));
            }
            departmentId = picked.DepartmentId;
            state.DepartmentId = departmentId;
        }

        if (departmentId is not int deptId) return RedirectToAction(nameof(ChonKhoa));

        var doctors = await _bookingService.GetDoctorsAsync(deptId, state.Date);
        if (doctors.All(d => d.Id != doctorId))
        {
            TempData["Error"] = "Bác sĩ này không trực trong phạm vi đã chọn. Vui lòng chọn bác sĩ khác.";
            return RedirectToAction(nameof(ChonBacsi));
        }

        state.DoctorId = doctorId;
        state.DoctorChosen = true;

        // Luồng theo ngày chốt luôn khung giờ ở màn này vì ngày đã cố định từ bước 1.
        if (state.Mode == BookingModes.ByDate && state.Date is { } day)
        {
            if (!int.TryParse(slotId, out var chosenSlotId))
            {
                TempData["Error"] = "Vui lòng chọn một khung giờ còn trống của bác sĩ.";
                return RedirectToAction(nameof(ChonBacsi));
            }

            var slots = await _bookingService.GetSlotsAsync(day, deptId, doctorId);
            var chosen = slots.FirstOrDefault(s => s.Id == chosenSlotId);
            if (chosen is null || !chosen.IsSelectable)
            {
                TempData["Error"] = "Khung giờ này vừa hết suất đặt online. Vui lòng chọn khung khác.";
                return RedirectToAction(nameof(ChonBacsi));
            }

            state.SlotId = chosen.Id;
            SaveState(state);
            return RedirectToAction(nameof(ChonNguoiKham));
        }

        SaveState(state);
        return RedirectToAction(nameof(ChonNgayGio));
    }

    // ------------------------------------------------------------ Bước "ngày & khung giờ" (gộp, không còn chọn ca)

    [HttpGet]
    public async Task<IActionResult> ChonNgayGio()
    {
        var state = GetState();
        if (Guard(state, nameof(ChonNgayGio)) is { } redirect) return redirect;

        var from = _bookingService.FirstBookableDate;
        var days = (await _bookingService.GetDaysAsync(from, _bookingService.BookableDayCount, state.DepartmentId, state.DoctorId))
            .Select(d => new DayVm { Date = d.Date, State = d.State })
            .ToList();

        var selected = PickDate(days, state.Date);
        if (selected is null)
        {
            TempData["Error"] = $"{StateScopeLabel(state)} chưa mở ngày nào còn chỗ đặt online trong {WindowText}. Vui lòng chọn bác sĩ hoặc chuyên khoa khác.";
            return RedirectToAction(nameof(ChonBacsi));
        }

        var slots = await _bookingService.GetSlotsAsync(selected.Value, state.DepartmentId, state.DoctorId);

        // Ngày cũ không còn lịch (vừa đổi bác sĩ) -> xóa lựa chọn khung giờ đang giữ.
        int? selectedSlot = slots.Any(s => s.Id == state.SlotId && s.IsSelectable) ? state.SlotId : null;
        if (selectedSlot != state.SlotId)
        {
            state.SlotId = null;
            state.Date = selected.Value;
            SaveState(state);
        }

        var vm = new ChonNgayGioViewModel
        {
            Department = state.DepartmentId is { } deptId ? ToDepartmentVm(await RequireOrEmptyAsync(deptId)) : null,
            Doctor = await LoadDoctorAsync(state),
            Days = days,
            SelectedDate = selected.Value,
            MonthLabel = FormatMonth(selected.Value),
            Slots = slots.Select(ToSlotVm).ToList(),
            SelectedSlotId = selectedSlot,
            CanChangeDepartment = ModeHasStep(state, nameof(ChonKhoa)),
            CanChangeDoctor = ModeHasStep(state, nameof(ChonBacsi))
        };

        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChonNgayGio(string? date, string? slotId)
    {
        var state = GetState();
        if (Guard(state, nameof(ChonNgayGio)) is { } redirect) return redirect;

        if (!TryParseDate(date, out var d) || !_bookingService.IsBookableDate(d))
        {
            TempData["Error"] = "Ngày khám không còn đặt được. Vui lòng chọn ngày khác.";
            return RedirectToAction(nameof(ChonNgayGio));
        }

        if (!int.TryParse(slotId, out var chosenSlotId))
        {
            TempData["Error"] = "Vui lòng chọn một khung giờ khám.";
            return RedirectToAction(nameof(ChonNgayGio));
        }

        // Đối chiếu lại từ DB: khung giờ phải thuộc đúng ngày và đúng phạm vi đã chọn.
        var slots = await _bookingService.GetSlotsAsync(d, state.DepartmentId, state.DoctorId);
        var chosen = slots.FirstOrDefault(s => s.Id == chosenSlotId);
        if (chosen is null || !chosen.IsSelectable)
        {
            TempData["Error"] = "Khung giờ này vừa hết suất đặt online. Vui lòng chọn khung khác.";
            return RedirectToAction(nameof(ChonNgayGio));
        }

        state.Date = d;
        state.SlotId = chosen.Id;
        SaveState(state);

        return RedirectToAction(nameof(ChonNguoiKham));
    }

    // Trả về HTML khối khung giờ của một ngày (booking.js gọi bằng fetch khi bấm ngày khác).
    [HttpGet]
    public async Task<IActionResult> KhungGio(string? date)
    {
        var state = GetState();
        if (state.Mode is null) return BadRequest();

        if (!TryParseDate(date, out var d) || !_bookingService.IsBookableDate(d))
            return BadRequest();

        var slots = await _bookingService.GetSlotsAsync(d, state.DepartmentId, state.DoctorId);
        int? selected = slots.Any(s => s.Id == state.SlotId && s.IsSelectable) ? state.SlotId : null;

        return PartialView("_SlotList", new SlotListViewModel
        {
            Date = d,
            Slots = slots.Select(ToSlotVm).ToList(),
            SelectedSlotId = selected,
            ShowDoctor = state.DoctorId is null
        });
    }

    // ------------------------------------------------------------ Bước "chọn người khám"

    [HttpGet]
    public async Task<IActionResult> ChonNguoiKham()
    {
        var state = GetState();
        if (Guard(state, nameof(ChonNguoiKham)) is { } redirect) return redirect;

        var profiles = await GetProfilesAsync();

        int? selectedId = profiles.Any(p => p.Id == state.PatientId && p.IsBookable)
            ? state.PatientId
            : profiles.FirstOrDefault(p => p.RelationshipCode == "SELF" && p.IsBookable)?.Id;

        return View(new ChonNguoiKhamViewModel
        {
            Patients = profiles,
            SelectedPatientId = selectedId
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChonNguoiKham(int patientId)
    {
        var state = GetState();
        if (Guard(state, nameof(ChonNguoiKham)) is { } redirect) return redirect;

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

    // ------------------------------------------------------------ Bước cuối: xác nhận

    [HttpGet]
    public async Task<IActionResult> XacNhan()
    {
        var state = GetState();
        if (Guard(state, nameof(XacNhan)) is { } redirect) return redirect;

        var slot = await FindSelectedSlotAsync(state);
        if (slot is null) return RedirectToAction(SlotStepOf(state));

        var dept = await LoadDepartmentForSlotAsync(state, slot);
        if (dept is null) return RedirectToAction(nameof(ChonKhoa));

        var patient = (await GetProfilesAsync()).FirstOrDefault(p => p.Id == state.PatientId);
        if (patient is null) return RedirectToAction(nameof(ChonNguoiKham));

        return View(new XacNhanViewModel
        {
            Patient = patient,
            Department = ToDepartmentVm(dept),
            Date = DateOnly.FromDateTime(slot.StartTime),
            Slot = ToSlotVm(slot),
            Fee = await _bookingService.GetExaminationFeeAsync(dept.Id),
            HasHealthInsurance = state.HasHealthInsurance,
            HasPrivateInsurance = state.HasPrivateInsurance
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> XacNhan(
        bool agreed,
        bool hasHealthInsurance = false,
        bool hasPrivateInsurance = false)
    {
        var state = GetState();
        if (Guard(state, nameof(XacNhan)) is { } redirect) return redirect;

        var patient = (await GetProfilesAsync()).FirstOrDefault(p => p.Id == state.PatientId);
        if (patient is null) return RedirectToAction(nameof(ChonNguoiKham));

        var slot = await FindSelectedSlotAsync(state);
        if (slot is null)
        {
            TempData["Error"] = "Khung giờ vừa hết suất trước khi bạn xác nhận. Vui lòng chọn lại giờ.";
            return RedirectToAction(SlotStepOf(state));
        }

        var dept = await LoadDepartmentForSlotAsync(state, slot);
        if (dept is null) return RedirectToAction(nameof(ChonKhoa));

        var visitDate = DateOnly.FromDateTime(slot.StartTime);

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

        state.HasHealthInsurance = hasHealthInsurance;
        state.HasPrivateInsurance = hasPrivateInsurance;

        // BLL giữ một suất A của khung giờ, sinh số thứ tự và kiểm tra hạn mức trong cùng một giao dịch.
        Appointment created;
        try
        {
            created = await _bookingService.CreateAppointmentAsync(
                AccountId, patient.Id, slot.Id,
                hasHealthInsurance: hasHealthInsurance,
                hasPrivateInsurance: hasPrivateInsurance);
        }
        catch (InvalidOperationException blocked)
        {
            // Vượt hạn mức trong ngày, trùng lịch, hoặc ca vừa có người đặt trước.
            TempData["Error"] = blocked.Message;
            SaveState(state);
            return RedirectToAction(nameof(XacNhan));
        }

        var slotVm = ToSlotVm(slot);

        HttpContext.Session.SetObject(LastBookingKey, new ThanhCongViewModel
        {
            Code = created.AppointmentCode,
            Status = "CONFIRMED",
            Patient = patient,
            DepartmentName = dept.Name,
            Date = visitDate,
            ShiftLabel = slotVm.ShiftLabel,
            TimeRange = slotVm.Label,
            QueueNumber = created.QueueNumber ?? string.Empty,
            Doctor = slotVm.Doctor,
            Room = slotVm.Room
        });

        // Đặt thành công -> reset trạng thái chọn lịch (vẫn giữ PatientId cho lần sau)
        SaveState(new BookingState { Mode = state.Mode, PatientId = state.PatientId });

        // Báo quầy tiếp nhận (WinForms) nạp lại danh sách ngay khi có lượt hẹn mới.
        _realtime.Publish(QueueChangeKinds.AppointmentCreated);

        return RedirectToAction(nameof(ThanhCong));
    }

    // ------------------------------------------------------------ Bước cuối: thành công

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

    // ------------------------------------------------------------ Điều hướng theo luồng

    private static (string Action, string Label)[] StepsOf(string mode) => Wizard[mode];

    /// <summary>Bước có nằm trong luồng hiện hành không (quyết định màn hình được phép "Đổi" lựa chọn đó).</summary>
    private static bool ModeHasStep(BookingState state, string action) =>
        state.Mode is string mode && StepsOf(mode).Any(s => s.Action == action);

    /// <summary>
    /// Bước sớm nhất bệnh nhân còn thiếu trong luồng đang đi; null khi đã chọn đủ.
    /// </summary>
    private static (string Action, string Label)? FirstUnfinishedStep(BookingState state)
    {
        if (state.Mode is not string mode) return null;

        foreach (var step in StepsOf(mode))
        {
            if (!IsDone(state, step.Action)) return step;
        }
        return null;
    }

    private static bool IsDone(BookingState state, string step) => step switch
    {
        nameof(ChonNgay) => state.Date is not null,
        nameof(ChonKhoa) => state.DepartmentId is not null,
        // Màn "Bác sĩ & giờ" của luồng theo ngày chốt luôn khung giờ nên bước chỉ hoàn thành khi có slotId.
        nameof(ChonBacsi) => state.DoctorChosen && (state.Mode != BookingModes.ByDate || state.SlotId is not null),
        nameof(ChonNgayGio) => state.SlotId is not null,
        nameof(ChonNguoiKham) => state.PatientId is not null,
        _ => true
    };

    /// <summary>
    /// Chặn nhảy bước: đưa về bước sớm nhất còn thiếu, đồng thời nạp nhãn stepper và nút Quay lại
    /// của bước hiện tại theo đúng luồng đang đi.
    /// </summary>
    private IActionResult? Guard(BookingState state, string action)
    {
        if (RequireAccount() is { } signIn) return signIn;
        if (state.Mode is not string mode) return RedirectToAction(nameof(Index));

        if (FirstUnfinishedStep(state) is { } missing && missing.Action != action)
            return RedirectToAction(missing.Action);

        var steps = StepsOf(mode);
        int index = Array.FindIndex(steps, s => s.Action == action);
        ViewData["Steps"] = steps.Select(s => s.Label).ToArray();
        ViewData["Step"] = index + 1;
        ViewData["StepLabel"] = $"BƯỚC {index + 1} / {steps.Length}";
        ViewData["BackUrl"] = index <= 0
            ? Url.Action(nameof(Index), "Booking")!
            : Url.Action(steps[index - 1].Action, "Booking")!;

        return null;
    }

    /// <summary>Bước quyết định khung giờ của luồng hiện hành (màn "Bác sĩ & giờ" hoặc "Ngày & giờ").</summary>
    private static string SlotStepOf(BookingState state) =>
        state.Mode == BookingModes.ByDate ? nameof(ChonBacsi) : nameof(ChonNgayGio);

    /// <summary>
    /// Ngày đang chọn vẫn còn lịch thì giữ, không thì lấy ngày đầu tiên còn chỗ.
    /// null khi phạm vi đang xét (toàn viện / khoa / bác sĩ) không còn ngày nào mở đặt online.
    /// </summary>
    private static DateOnly? PickDate(List<DayVm> days, DateOnly? saved)
    {
        var pickable = days.Where(d => d.State is not ("off" or "closed")).ToList();
        if (pickable.Count == 0) return null;

        return saved is { } savedDate && pickable.Any(d => d.Date == savedDate)
            ? savedDate
            : (pickable.FirstOrDefault(d => d.State != "full") ?? pickable[0]).Date;
    }

    private string WindowText =>
        $"{_bookingService.FirstBookableDate:dd/MM/yyyy} – {_bookingService.LastBookableDate:dd/MM/yyyy}";

    private static string StateScopeLabel(BookingState state) => state switch
    {
        { DoctorId: not null } => "Bác sĩ đã chọn",
        { DepartmentId: not null } => "Chuyên khoa này",
        _ => "Bệnh viện"
    };

    private static string FormatMonth(DateOnly date)
        => $"Tháng {date.ToString("MM/yyyy", CultureInfo.InvariantCulture)}";

    // ------------------------------------------------------------ Helpers dữ liệu

    private BookingState GetState()
        => HttpContext.Session.GetObject<BookingState>(StateKey) ?? new BookingState();

    private void SaveState(BookingState state)
        => HttpContext.Session.SetObject(StateKey, state);

    /// <summary>
    /// Khoa vừa chọn còn mở đặt online hay không. Luồng theo ngày chỉ tính chỗ của đúng ngày đã chọn,
    /// nên một khoa đầy lịch hôm đó bị từ chối ngay cả khi tuần sau còn trống.
    /// </summary>
    private async Task<BookingDepartmentDto?> LoadDepartmentAsync(BookingState state, int departmentId)
    {
        BookingDepartmentDto? dept;

        if (state.Mode == BookingModes.ByDate && state.Date is { } day)
        {
            dept = (await _bookingService.GetDepartmentsOnDateAsync(day)).FirstOrDefault(d => d.Id == departmentId);
            if (dept is null || dept.SlotsAvailable == 0)
            {
                TempData["Error"] = $"Chuyên khoa này không còn chỗ ngày {VietnameseFormat.ShortDate(day)}. Vui lòng chọn chuyên khoa khác.";
                return null;
            }
            return dept;
        }

        dept = await _bookingService.GetDepartmentByIdAsync(departmentId);
        if (dept is null || dept.SlotsAvailable == 0)
        {
            TempData["Error"] = "Chuyên khoa này chưa có lịch khả dụng. Vui lòng chọn chuyên khoa khác.";
            return null;
        }
        return dept;
    }

    private async Task<BookingDepartmentDto> RequireDepartmentAsync(BookingState state) =>
        await RequireOrEmptyAsync(state.DepartmentId!.Value);

    private async Task<BookingDepartmentDto> RequireOrEmptyAsync(int departmentId) =>
        await _bookingService.GetDepartmentByIdAsync(departmentId) ?? EmptyDepartment(departmentId);

    private static BookingDepartmentDto EmptyDepartment(int id) => new() { Id = id, Name = "chuyên khoa đã chọn" };

    /// <summary>Bác sĩ đã chọn trong Session, để màn "Ngày & giờ" ghi tên và chuyên môn bác sĩ.</summary>
    private async Task<DoctorVm?> LoadDoctorAsync(BookingState state)
    {
        if (state.DoctorId is not int doctorId) return null;

        var doctor = (await _bookingService.GetDoctorsAsync(state.DepartmentId))
            .FirstOrDefault(d => d.Id == doctorId);

        return doctor is null ? null : DoctorVm.From(doctor);
    }

    /// <summary>
    /// Khoa của lượt khám: ưu tiên khoa bệnh nhân đã chọn, nếu đi thẳng từ bác sĩ thì lấy theo khung giờ.
    /// null khi cả hai đều không đọc được -> controller đưa về bước chọn chuyên khoa.
    /// </summary>
    private async Task<BookingDepartmentDto?> LoadDepartmentForSlotAsync(BookingState state, BookingSlotDto slot)
    {
        var dept = await _bookingService.GetDepartmentByIdAsync(state.DepartmentId ?? slot.DepartmentId);
        return dept ?? await _bookingService.GetDepartmentByIdAsync(slot.DepartmentId);
    }

    // Khung giờ đang chọn, đối chiếu lại với DB (còn tồn tại, còn trong cửa sổ đặt được, còn suất online).
    private async Task<BookingSlotDto?> FindSelectedSlotAsync(BookingState state)
    {
        if (state.SlotId is not int slotId) return null;

        var slot = await _bookingService.GetSlotByIdAsync(slotId);
        if (slot is null || !slot.IsSelectable) return null;

        if (state.DoctorId is { } doctorId && slot.DoctorId != doctorId) return null;
        if (state.DepartmentId is { } departmentId && slot.DepartmentId != departmentId) return null;

        return slot;
    }

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

    private static DepartmentVm ToDepartmentVm(BookingDepartmentDto d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Description = d.Description ?? string.Empty,
        Icon = DepartmentIcons.For(d.Name),
        SlotsAvailable = d.SlotsAvailable
    };

    private static DoctorSlotsVm ToDoctorSlotsVm(BookingDoctorSlotsDto d) => new()
    {
        Doctor = new DoctorVm
        {
            Id = d.DoctorId,
            FullName = d.DoctorName,
            Specialty = d.Specialty,
            DutyDays = string.Empty
        },
        Room = d.RoomName,
        Slots = d.Slots.Select(ToSlotVm).ToList()
    };

    private static SlotVm ToSlotVm(BookingSlotDto s) => new()
    {
        Id = s.Id,
        Label = s.Label,
        ShiftLabel = s.ShiftLabel,
        Status = s.Status,
        SlotsLeft = s.SlotsLeft,
        OnlineQuota = s.OnlineQuota,
        Room = s.RoomName ?? string.Empty,
        Doctor = DoctorVm.From(s)
    };

    private static bool TryParseDate(string? text, out DateOnly date)
        => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
