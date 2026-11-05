using System.Globalization;
using System.Text.RegularExpressions;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.Domain;
using HIS.Web.Helpers;
using HIS.Web.ViewModels.Booking;
using HIS.Web.ViewModels.HoSo;
using HIS.Web.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HIS.Web.Controllers;

/// <summary>
/// Màn "Hồ sơ của tôi": một màn Tạo hồ sơ duy nhất với hai nhánh — "đã từng khám" (chỉ nhập mã bệnh nhân,
/// hồ sơ tìm được gắn link PENDING rồi xác thực email của chính hồ sơ đó) và "chưa từng khám" (khai đầy đủ
/// nhân thân + email; phiếu nằm trong Session và chỉ được ghi xuống DB sau khi nhập đúng mã OTP)
/// — cùng màn chi tiết/xem lịch sử theo hồ sơ. Không còn màn "Xin liên kết" riêng.
/// Quy tắc liên kết nằm trong PatientProfileService/PatientAccessService nên WinForms dùng chung.
/// </summary>
public class HoSoController : PatientPageController
{
    private static readonly Regex PhoneRegex = new(@"^0\d{9,10}$", RegexOptions.Compiled);

    // Mã OTP đang chờ nhập chỉ sống trong Session (không lập bảng mới).
    private const string OtpKey = "PendingProfileOtp";

    private readonly IPatientProfileService _profileService;
    private readonly IBookingService _bookingService;
    private readonly IOtpService _otpService;
    private readonly OtpOptions _otpOptions;
    private readonly QueueChangeNotifier _realtime;

    public HoSoController(
        IPatientProfileService profileService,
        IBookingService bookingService,
        IOtpService otpService,
        IOptions<OtpOptions> otpOptions,
        QueueChangeNotifier realtime)
    {
        _profileService = profileService;
        _bookingService = bookingService;
        _otpService = otpService;
        _otpOptions = otpOptions.Value;
        _realtime = realtime;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (RequireAccount() is { } signIn) return signIn;

        return View(new HoSoIndexViewModel
        {
            Profiles = await LoadProfilesAsync()
        });
    }

    // ------------------------------------------------------------------ Tạo hồ sơ (hai nhánh một màn)

    [HttpGet]
    public async Task<IActionResult> Them(string? branch = null)
    {
        if (RequireAccount() is { } signIn) return signIn;

        ViewData["RelationshipOptions"] = RelationshipOptions(excludeSelf: true);
        ViewData["NewRelationshipOptions"] = await SelfOptionAllowedAsync();
        return View(new ThemHoSoRequest { Branch = branch == ThemHoSoRequest.SeenBranch ? ThemHoSoRequest.SeenBranch : ThemHoSoRequest.NewBranch });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Them(ThemHoSoRequest request)
    {
        if (RequireAccount() is { } signIn) return signIn;

        return request.IsSeen
            ? await LinkExistingAsync(request)
            : await CreateNewAsync(request);
    }

    /// <summary>
    /// Nhánh "đã từng khám": một thông tin định danh là đủ, hồ sơ tìm được sẽ ở PENDING
    /// cho tới khi quầy đối chiếu giấy tờ (quy tắc nằm trong PatientProfileService.RequestLinkAsync).
    /// </summary>
    private async Task<IActionResult> LinkExistingAsync(ThemHoSoRequest request)
    {
        ViewData["RelationshipOptions"] = RelationshipOptions(excludeSelf: true);
        ViewData["NewRelationshipOptions"] = await SelfOptionAllowedAsync();

        string keyword = (request.Keyword ?? string.Empty).Trim();
        if (keyword.Length == 0)
        {
            TempData["Error"] = "Vui lòng nhập mã bệnh nhân in trên phiếu khám (ví dụ BN000123). Quên mã thì gọi 0808 0808.";
            return View(request);
        }

        var result = await _profileService.RequestLinkAsync(
            AccountId, keyword, NormalizeRelationship(request.Relationship));

        TempData[result.Success ? "Message" : "Error"] = result.Message;

        if (result.Success)
        {
            // Quầy có badge "chờ duyệt giấy tờ" -> báo để hiện ngay.
            _realtime.Publish(QueueChangeKinds.ProfileLinkRequested);

            // Liên kết mới còn PENDING thì phải xác thực email của hồ sơ trước khi mở quyền đặt lịch.
            return result.LinkStatus == "PENDING"
                ? await StartExistingOtpAsync(result.PatientId)
                : RedirectToAction(nameof(Chitiet), new { patientId = result.PatientId });
        }

        return View(request);
    }

    /// <summary>
    /// Nhánh "chưa từng khám": phiếu được giữ trong Session và chỉ được ghi vào DB sau khi nhập đúng mã OTP,
    /// nên lỗi gửi mã hay bỏ giữa chừng không để lại hồ sơ "ma" mà người dùng không biết.
    /// </summary>
    private async Task<IActionResult> CreateNewAsync(ThemHoSoRequest request)
    {
        var options = await SelfOptionAllowedAsync();
        ViewData["RelationshipOptions"] = RelationshipOptions(excludeSelf: true);
        ViewData["NewRelationshipOptions"] = options;

        var name = request.FullName?.Trim() ?? string.Empty;
        var phone = (request.Phone ?? string.Empty).Replace(" ", "");
        var email = ProfileEmail.Normalize(request.Email);

        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstModelError() ?? "Dữ liệu nhập vào không hợp lệ.";
            return View(request);
        }

        if (name.Length == 0)
        {
            TempData["Error"] = "Vui lòng nhập họ và tên người khám.";
            return View(request);
        }

        if (!PhoneRegex.IsMatch(phone))
        {
            TempData["Error"] = "Số điện thoại phải từ 10-11 số và bắt đầu bằng 0.";
            return View(request);
        }

        if (email is null)
        {
            TempData["Error"] = "Vui lòng nhập email để hệ thống gửi mã xác thực. Hồ sơ chưa được lưu.";
            return View(request);
        }

        if (!ProfileEmail.IsUsable(email))
        {
            TempData["Error"] = $"Email \"{email}\" không hợp lệ. Vui lòng kiểm tra lại. Hồ sơ chưa được lưu.";
            return View(request);
        }

        if (!DateOnly.TryParseExact(request.DateOfBirth?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
        {
            TempData["Error"] = "Ngày sinh không hợp lệ. Vui lòng nhập lại theo định dạng DD/MM/YYYY.";
            return View(request);
        }

        if (string.IsNullOrWhiteSpace(request.Gender))
        {
            TempData["Error"] = "Vui lòng chọn giới tính.";
            return View(request);
        }

        return await StartNewProfileOtpAsync(request, email);
    }

    // ------------------------------------------------------------------ Bước sau khi thêm hồ sơ: nhập mã OTP

    /// <summary>Màn nhập mã xác thực. Vào được khi Session còn mã đang chờ.</summary>
    [HttpGet]
    public IActionResult XacOtp()
    {
        if (RequireAccount() is { } signIn) return signIn;

        PendingOtp? pending = GetPendingOtp();
        if (pending is null)
        {
            TempData["Error"] = "Không còn mã xác thực nào đang chờ. Hãy thêm hồ sơ lại.";
            return RedirectToAction(nameof(Index));
        }

        return View(BuildOtpVm(pending));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> XacOtp(string code)
    {
        if (RequireAccount() is { } signIn) return signIn;

        PendingOtp? pending = GetPendingOtp();
        if (pending is null)
        {
            TempData["Error"] = "Không còn mã xác thực nào đang chờ. Hãy thêm hồ sơ lại.";
            return RedirectToAction(nameof(Index));
        }

        if (DateTimeOffset.Now > pending.ExpiresAt)
        {
            TempData["Error"] = "Mã xác thực đã hết hạn. Bấm \"Gửi lại mã\" để lấy mã mới.";
            return View(BuildOtpVm(pending));
        }

        if (!OtpCodes.Matches(pending.Code, code))
        {
            pending.Attempts++;

            if (pending.Attempts >= _otpOptions.MaxAttempts)
            {
                ClearPendingOtp();

                if (pending.Draft is null)
                {
                    TempData["Error"] = $"Nhập sai {_otpOptions.MaxAttempts} lần nên mã đã bị hủy. Hồ sơ vẫn còn ở danh sách; " +
                                        "quầy sẽ xác nhận giấy tờ khi bạn tới khám.";
                    return RedirectToAction(nameof(Index));
                }

                TempData["Error"] = $"Nhập sai {_otpOptions.MaxAttempts} lần nên mã đã bị hủy. " +
                                    "Hệ thống chưa lưu hồ sơ nào — hãy khai lại và xin mã mới.";
                return RedirectToAction(nameof(Them), new { branch = ThemHoSoRequest.NewBranch });
            }

            SavePendingOtp(pending);
            TempData["Error"] = $"Mã không đúng. Còn {_otpOptions.MaxAttempts - pending.Attempts} lần nhập.";
            return View(BuildOtpVm(pending));
        }

        ClearPendingOtp();

        // Nhánh "chưa từng khám": phiếu khai chỉ được ghi xuống DB sau khi mã đúng, nên không bao giờ
        // còn cảnh báo lỗi mà hồ sơ vẫn hiện trong danh sách.
        if (pending.Draft is { } draft)
        {
            var created = await _profileService.CreateProfileAsync(AccountId, BuildCreateRequest(draft, pending.Email));

            if (!created.Success)
            {
                TempData["Error"] = created.Message;
                return RedirectToAction(nameof(Them), new { branch = ThemHoSoRequest.NewBranch });
            }

            TempData["Message"] = $"Đã tạo hồ sơ {created.PatientCode} và xác thực email {pending.Email}.";
            return RedirectToAction(nameof(Chitiet), new { patientId = created.PatientId });
        }

        // Mã đúng thì liên kết PENDING tự chuyển ACTIVE, khỏi chờ quầy; người dùng bỏ giữa chừng
        // thì hồ sơ vẫn còn PENDING cho quầy đối chiếu giấy tờ.
        var verified = await _profileService.VerifyLinkAsync(AccountId, pending.PatientId, AccountId);
        TempData["Message"] = verified.Success
            ? verified.Message
            : $"Đã xác thực email {pending.Email} cho hồ sơ {pending.PatientName}.";
        return RedirectToAction(nameof(Chitiet), new { patientId = pending.PatientId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GuiLaiOtp()
    {
        if (RequireAccount() is { } signIn) return signIn;

        PendingOtp? pending = GetPendingOtp();
        if (pending is null)
        {
            TempData["Error"] = "Không còn mã xác thực nào đang chờ. Hãy thêm hồ sơ lại.";
            return RedirectToAction(nameof(Index));
        }

        int waited = (int)(DateTimeOffset.Now - pending.IssuedAt).TotalSeconds;
        if (waited < _otpOptions.ResendDelaySeconds)
        {
            TempData["Error"] = $"Vui lòng chờ thêm {_otpOptions.ResendDelaySeconds - waited} giây trước khi xin mã mới.";
            return RedirectToAction(nameof(XacOtp));
        }

        return pending.Draft is { } draft
            ? await StartNewProfileOtpAsync(draft, pending.Email)
            : await StartExistingOtpAsync(pending.PatientId);
    }

    // ------------------------------------------------------------------ Chi tiết một hồ sơ + lịch sử khám

    [HttpGet]
    public async Task<IActionResult> Chitiet(int patientId)
    {
        if (RequireAccount() is { } signIn) return signIn;

        var profile = (await LoadProfilesAsync()).FirstOrDefault(p => p.PatientId == patientId);
        if (profile is null)
        {
            TempData["Error"] = "Hồ sơ này không thuộc tài khoản của bạn.";
            return RedirectToAction(nameof(Index));
        }

        var history = (await _bookingService.GetAppointmentsByAccountAsync(AccountId))
            .Where(a => a.PatientId == patientId)
            .Select(a => a.ToVm())
            .ToList();

        return View(new HoSoChiTietViewModel { Profile = profile, History = history });
    }

    // ------------------------------------------------------------------ Helpers

    /// <summary>
    /// Nhánh "đã từng khám": xin mã gửi về email đã lưu trong hồ sơ rồi đưa sang màn nhập mã.
    /// </summary>
    private async Task<IActionResult> StartExistingOtpAsync(int patientId)
    {
        OtpIssueResult issue = await _otpService.IssueProfileOtpAsync(AccountId, patientId);

        if (issue.Code is null || issue.ExpiresAt is null)
        {
            // Không gửi được mã thì trả về đúng nhánh vừa khai, không bắt chọn lại từ đầu.
            TempData["Error"] = issue.Message;
            return RedirectToAction(nameof(Them), new { branch = ThemHoSoRequest.SeenBranch });
        }

        return await SavePendingAndAskCodeAsync(
            new PendingOtp { PatientId = patientId, PatientName = issue.PatientName }, issue);
    }

    /// <summary>
    /// Nhánh "chưa từng khám": xin mã gửi về email vừa gõ trên form, giữ nguyên phiếu khai trong Session.
    /// Hồ sơ chỉ được tạo ở màn nhập mã, nên lỗi gửi mã không để lại dữ liệu dở dang — phiếu được trả về
    /// đúng màn hình cũ với các ô đã nhập.
    /// </summary>
    private async Task<IActionResult> StartNewProfileOtpAsync(ThemHoSoRequest draft, string email)
    {
        OtpIssueResult issue = await _otpService.IssueNewProfileOtpAsync(
            AccountId, email, draft.FullName?.Trim() ?? string.Empty);

        if (issue.Code is null || issue.ExpiresAt is null)
        {
            ViewData["RelationshipOptions"] = RelationshipOptions(excludeSelf: true);
            ViewData["NewRelationshipOptions"] = await SelfOptionAllowedAsync();
            TempData["Error"] = issue.Message;
            return View(nameof(Them), draft);
        }

        draft.Email = email;

        return await SavePendingAndAskCodeAsync(
            new PendingOtp { PatientName = draft.FullName?.Trim() ?? string.Empty, Draft = draft }, issue);
    }

    private async Task<IActionResult> SavePendingAndAskCodeAsync(PendingOtp pending, OtpIssueResult issue)
    {
        pending.Email = issue.RecipientEmail ?? pending.Email;
        pending.Code = issue.Code!;
        pending.ExpiresAt = issue.ExpiresAt!.Value;
        pending.IssuedAt = DateTimeOffset.Now;
        pending.RevealCode = issue.RevealCode;

        SavePendingOtp(pending);

        TempData[issue.Success ? "Message" : "Error"] = issue.Message;
        return RedirectToAction(nameof(XacOtp));
    }

    /// <summary>Chuyển phiếu khai ở Session thành yêu cầu tạo hồ sơ; email lấy từ phiếu đã xác thực chứ không tin lại form.</summary>
    private static CreateProfileRequest BuildCreateRequest(ThemHoSoRequest draft, string email)
    {
        DateOnly.TryParseExact(draft.DateOfBirth?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var dob);

        return new CreateProfileRequest
        {
            FullName = draft.FullName?.Trim() ?? string.Empty,
            DateOfBirth = dob,
            Gender = (draft.Gender ?? string.Empty).ToUpperInvariant(),
            PhoneNumber = (draft.Phone ?? string.Empty).Replace(" ", ""),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            NationalId = string.IsNullOrWhiteSpace(draft.NationalId) ? null : draft.NationalId.Trim(),
            GuardianNationalId = string.IsNullOrWhiteSpace(draft.GuardianNationalId) ? null : draft.GuardianNationalId.Trim(),
            Address = string.IsNullOrWhiteSpace(draft.Address) ? null : draft.Address.Trim(),
            InsuranceCardNumber = string.IsNullOrWhiteSpace(draft.InsuranceCardNumber) ? null : draft.InsuranceCardNumber.Trim(),
            Relationship = NormalizeRelationship(draft.Relationship)
        };
    }

    private XacOtpViewModel BuildOtpVm(PendingOtp pending)
    {
        int resendWait = _otpOptions.ResendDelaySeconds - (int)(DateTimeOffset.Now - pending.IssuedAt).TotalSeconds;

        return new XacOtpViewModel
        {
            PatientName = pending.PatientName,
            Email = pending.Email,
            ExpiresAt = pending.ExpiresAt,
            AttemptsLeft = Math.Max(0, _otpOptions.MaxAttempts - pending.Attempts),
            ResendWaitSeconds = Math.Max(0, resendWait),
            RevealedCode = pending.RevealCode ? pending.Code : null
        };
    }

    private PendingOtp? GetPendingOtp() => HttpContext.Session.GetObject<PendingOtp>(OtpKey);

    private void SavePendingOtp(PendingOtp pending) => HttpContext.Session.SetObject(OtpKey, pending);

    private void ClearPendingOtp() => HttpContext.Session.Remove(OtpKey);

    private async Task<List<HoSoItemVm>> LoadProfilesAsync()
    {
        var links = (await _profileService.GetLinksForAccountAsync(AccountId))
            .Where(l => l.StatusCode != "REVOKED")
            .ToList();

        var counts = (await _bookingService.GetAppointmentsByAccountAsync(AccountId))
            .GroupBy(a => a.PatientId)
            .ToDictionary(g => g.Key, g => g.Count());

        return links
            .Select(l => new HoSoItemVm
            {
                PatientId = l.PatientId,
                Code = l.PatientCode,
                FullName = l.PatientFullName,
                Relationship = l.RelationshipLabel,
                RelationshipCode = l.RelationshipCode,
                DateOfBirth = l.DateOfBirth,
                GenderLabel = l.GenderLabel,
                Phone = l.PatientPhone,
                StatusCode = l.StatusCode,
                StatusLabel = l.StatusLabel,
                NeedsApproval = l.StatusCode == "PENDING",
                CanViewRecords = l.CanViewRecords,
                AppointmentCount = counts.GetValueOrDefault(l.PatientId)
            })
            .OrderByDescending(p => p.StatusCode == "ACTIVE")
            .ThenBy(p => p.FullName)
            .ToList();
    }

    private static List<RelationshipOptionVm> RelationshipOptions(bool excludeSelf = false)
        => Enum.GetValues<PatientRelationship>()
            .Where(r => !excludeSelf || r != PatientRelationship.Self)
            .Select(r => new RelationshipOptionVm { Code = r.ToString().ToUpperInvariant(), Label = r.ToLabel() })
            .ToList();

    /// <summary>
    /// Chỉ cho chọn "Bản thân" khi tài khoản chưa có hồ sơ SELF — BLL cũng chặn trường hợp này,
    /// giấu lựa chọn giúp người dùng không phải đọc thông báo lỗi.
    /// </summary>
    private async Task<List<RelationshipOptionVm>> SelfOptionAllowedAsync()
    {
        string self = PatientRelationship.Self.ToDatabaseCode();
        bool hasSelf = (await _profileService.GetLinksForAccountAsync(AccountId))
            .Any(l => l.RelationshipCode == self && l.StatusCode != "REVOKED");

        return RelationshipOptions(excludeSelf: hasSelf);
    }

    private static string NormalizeRelationship(string? code)
        => Enum.TryParse<PatientRelationship>(code, ignoreCase: true, out var rel)
            ? rel.ToString().ToUpperInvariant()
            : nameof(PatientRelationship.Other).ToUpperInvariant();

    private string? FirstModelError()
        => ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage;
}
