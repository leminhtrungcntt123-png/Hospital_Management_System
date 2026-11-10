using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.BLL.Options;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace HIS.BLL.Services;

public class ReceptionService : IReceptionService
{
    private static readonly string[] WaitingCodes = { AppointmentStatus.Codes.Pending, AppointmentStatus.Codes.Confirmed };
    private static readonly string[] ReceivedCodes = { AppointmentStatus.Codes.CheckedIn, AppointmentStatus.Codes.InProgress, AppointmentStatus.Codes.Completed };
    private static readonly string[] CancelledCodes =
    {
        AppointmentStatus.Codes.Cancelled, AppointmentStatus.Codes.NoShow, AppointmentStatus.Codes.Expired
    };

    private readonly AppDbContext _context;
    private readonly QueuePositionAllocator _allocator;
    private readonly BookingOptions _options;
    private readonly TimeProvider _timeProvider;

    public ReceptionService(
        AppDbContext context,
        QueuePositionAllocator allocator,
        IOptions<BookingOptions> options,
        TimeProvider timeProvider)
    {
        _context = context;
        _allocator = allocator;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    // Đồng hồ ứng dụng: mốc cắt giải phóng suất A và ngưỡng NO_SHOW phải dùng cùng một nguồn giờ
    // với updated_at do AppDbContext ghi, và phải giả lập được khi kiểm thử.
    private DateTime Now => _timeProvider.GetLocalNow().DateTime;

    private DateTime Today => Now.Date;

    public async Task<ReceptionCountsDto> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        await ExpireOverdueAppointmentsAsync(cancellationToken);

        var statusMap = await LoadStatusMapAsync(cancellationToken);
        var waitingIds = ResolveIds(statusMap, WaitingCodes);
        var receivedIds = ResolveIds(statusMap, ReceivedCodes);
        var cancelledIds = ResolveIds(statusMap, CancelledCodes);

        DateTime today = Today;
        DateTime tomorrow = today.AddDays(1);
        IQueryable<Appointment> todayAppointments = InDay(_context.Appointments.AsNoTracking(), today);

        int waiting = waitingIds.Count == 0
            ? 0
            : await todayAppointments.CountAsync(a => waitingIds.Contains(a.StatusId), cancellationToken);

        int received = receivedIds.Count == 0
            ? 0
            : await todayAppointments.CountAsync(a => receivedIds.Contains(a.StatusId), cancellationToken);

        int scheduled = waitingIds.Count == 0
            ? 0
            : await _context.Appointments.AsNoTracking()
                .CountAsync(a => a.Slot != null
                                 && a.Slot.StartTime >= tomorrow
                                 && waitingIds.Contains(a.StatusId), cancellationToken);

        int cancelled = cancelledIds.Count == 0
            ? 0
            : await todayAppointments.CountAsync(a => cancelledIds.Contains(a.StatusId), cancellationToken);

        return new ReceptionCountsDto(waiting, received, scheduled, cancelled);
    }

    public async Task<IReadOnlyList<ReceptionItemDto>> GetReceptionListAsync(
        ReceptionTab tab,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        await ExpireOverdueAppointmentsAsync(cancellationToken);

        var statusMap = await LoadStatusMapAsync(cancellationToken);

        List<int> statusIds = tab switch
        {
            ReceptionTab.Waiting => ResolveIds(statusMap, WaitingCodes),
            ReceptionTab.Received => ResolveIds(statusMap, ReceivedCodes),
            ReceptionTab.Scheduled => ResolveIds(statusMap, WaitingCodes),
            ReceptionTab.Cancelled => ResolveIds(statusMap, CancelledCodes),
            _ => new List<int>()
        };

        if (statusIds.Count == 0)
        {
            return Array.Empty<ReceptionItemDto>();
        }

        DateTime today = Today;
        DateTime tomorrow = today.AddDays(1);

        IQueryable<Appointment> query = _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
                .ThenInclude(p => p.InsuranceCards)
            .Include(a => a.Status)
            .Include(a => a.BookedByAccount)
                .ThenInclude(b => b!.Role)
            .Include(a => a.Room)
                .ThenInclude(r => r!.Department)
            .Include(a => a.Doctor)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Doctor)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Room)
                    .ThenInclude(r => r!.Department);

        query = tab == ReceptionTab.Scheduled
            ? query.Where(a => a.Slot != null && a.Slot.StartTime >= tomorrow)
            : InDay(query, today);

        query = query.Where(a => statusIds.Contains(a.StatusId));

        if (!string.IsNullOrWhiteSpace(search))
        {
            string keyword = search.Trim();
            string pattern = $"%{keyword}%";
            int? patientIdFromCode = TryParsePatientCode(keyword);

            query = query.Where(a =>
                EF.Functions.ILike(a.AppointmentCode, pattern) ||
                EF.Functions.ILike(a.Patient.FullName, pattern) ||
                EF.Functions.ILike(a.Patient.PhoneNumber, pattern) ||
                (a.Patient.NationalId != null && EF.Functions.ILike(a.Patient.NationalId, pattern)) ||
                (a.Patient.InsuranceCards.Any(c => EF.Functions.ILike(c.CardNumber, pattern))) ||
                (patientIdFromCode != null && a.PatientId == patientIdFromCode));
        }

        List<Appointment> rows = await query.ToListAsync(cancellationToken);

        // Tab Chờ là màn gọi số nên thứ tự phải dựng theo hàng đợi thật, không theo giờ hẹn.
        if (tab == ReceptionTab.Waiting)
        {
            rows = ApplyCallOrder(rows);
        }

        // Bệnh nhân nào đang có liên kết chờ duyệt -> quầy thấy ngay trên dòng tiếp nhận.
        HashSet<int> pendingLinkPatientIds = await LoadPendingLinkPatientIdsAsync(
            rows.Select(a => a.PatientId).Distinct().ToList(), cancellationToken);

        List<ReceptionItemDto> items = rows
            .Select(a => MapItem(a, pendingLinkPatientIds.Contains(a.PatientId)))
            .ToList();

        IEnumerable<ReceptionItemDto> ordered = tab switch
        {
            // Đã sắp hàng ở ApplyCallOrder, giữ nguyên thứ tự đó khi ánh xạ sang DTO.
            ReceptionTab.Waiting => items,
            ReceptionTab.Received => items.OrderByDescending(i => i.AppointmentTime),
            ReceptionTab.Scheduled => items.OrderBy(i => i.AppointmentTime),
            ReceptionTab.Cancelled => items.OrderByDescending(i => i.AppointmentTime),
            _ => items
        };

        return ordered.ToList();
    }

    private List<Appointment> ApplyCallOrder(IReadOnlyList<Appointment> waiting) =>
        QueueCallOrder.Order(waiting, _options.MaxOvertakeAhead, _options.MaxTimesOvertaken);

    public async Task<ReceptionPatientDto?> FindPatientAsync(string keyword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return null;
        }

        string normalized = keyword.Trim();
        int? patientIdFromCode = TryParsePatientCode(normalized);

        Patient? patient = null;

        if (patientIdFromCode is int patientId)
        {
            patient = await _context.Patients.AsNoTracking()
                .Include(p => p.InsuranceCards)
                .FirstOrDefaultAsync(p => p.Id == patientId, cancellationToken);
        }

        patient ??= await _context.Patients.AsNoTracking()
            .Include(p => p.InsuranceCards)
            .FirstOrDefaultAsync(p =>
                p.PhoneNumber == normalized ||
                p.NationalId == normalized ||
                p.InsuranceCards.Any(c => c.CardNumber == normalized), cancellationToken);

        patient ??= await _context.Patients.AsNoTracking()
            .Include(p => p.InsuranceCards)
            .FirstOrDefaultAsync(p => EF.Functions.ILike(p.FullName, normalized), cancellationToken);

        return patient == null ? null : MapPatient(patient);
    }

    public async Task<IReadOnlyList<DepartmentOptionDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Departments
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentOptionDto(d.Id, d.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DoctorOptionDto>> GetDoctorsAsync(int? departmentId = null, CancellationToken cancellationToken = default)
    {
        IQueryable<Doctor> query = _context.Doctors.AsNoTracking();

        if (departmentId is int id && id > 0)
        {
            query = query.Where(d => d.DepartmentId == id);
        }

        return await query
            .OrderBy(d => d.FullName)
            .Select(d => new DoctorOptionDto(d.Id, d.DepartmentId, d.FullName, d.Specialty ?? string.Empty))
            .ToListAsync(cancellationToken);
    }

    public async Task<CreateReceptionResult> CreateReceptionAsync(
        CreateReceptionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        DateTime now = Now;
        string fullName = request.FullName.Trim();
        string phoneNumber = NormalizePhone(request.PhoneNumber);
        string? nationalId = NullIfWhiteSpace(request.NationalId);
        string? address = NullIfWhiteSpace(request.Address);
        string? insuranceCard = NullIfWhiteSpace(request.InsuranceCardNumber);

        Department department = await _context.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId, cancellationToken)
            ?? throw new ArgumentException("Khoa/phòng đã chọn không tồn tại.");

        Doctor? doctor = null;
        if (request.DoctorId is int doctorId)
        {
            doctor = await _context.Doctors
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == doctorId && d.DepartmentId == department.Id, cancellationToken)
                ?? throw new ArgumentException("Bác sĩ đã chọn không thuộc khoa/phòng này.");
        }

        int? roomId = request.RoomId;
        if (roomId == null && doctor != null)
        {
            roomId = await _context.DoctorSchedules
                .AsNoTracking()
                .Where(s => s.DoctorId == doctor.Id && s.IsActive)
                .OrderBy(s => s.Id)
                .Select(s => (int?)s.RoomId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        roomId ??= await _context.ClinicRooms
            .AsNoTracking()
            .Where(r => r.DepartmentId == department.Id)
            .OrderBy(r => r.Id)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (roomId == null)
        {
            throw new InvalidOperationException("Khoa đã chọn chưa được cấu hình phòng khám.");
        }

        var statusMap = await LoadStatusMapAsync(cancellationToken);
        if (!statusMap.TryGetValue(AppointmentStatus.Codes.CheckedIn, out int checkedInStatusId))
        {
            throw new InvalidOperationException("Cơ sở dữ liệu chưa có trạng thái CHECKED_IN. Vui lòng nạp dữ liệu danh mục trạng thái.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            Patient patient;
            bool isNewPatient = false;

            if (request.PatientId is int existingPatientId)
            {
                patient = await _context.Patients
                    .FirstOrDefaultAsync(p => p.Id == existingPatientId, cancellationToken)
                    ?? throw new ArgumentException("Không tìm thấy hồ sơ bệnh nhân đã chọn.");

                patient.PhoneNumber = phoneNumber;
                patient.Address = address;
                if (!string.IsNullOrWhiteSpace(request.Email))
                {
                    await ValidateEmailQuotaAsync(request.Email, existingPatientId, cancellationToken);
                    patient.Email = request.Email.Trim();
                }
            }
            else
            {
                Patient? duplicate = null;
                if (nationalId != null)
                {
                    duplicate = await _context.Patients
                        .FirstOrDefaultAsync(p => p.NationalId == nationalId, cancellationToken);
                }

                if (duplicate != null)
                {
                    patient = duplicate;
                    patient.PhoneNumber = phoneNumber;
                    patient.Address = address;
                    if (!string.IsNullOrWhiteSpace(request.Email))
                    {
                        await ValidateEmailQuotaAsync(request.Email, duplicate.Id, cancellationToken);
                        patient.Email = request.Email.Trim();
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(request.Email))
                    {
                        await ValidateEmailQuotaAsync(request.Email, null, cancellationToken);
                    }

                    // Quầy không còn tự sinh tài khoản đăng nhập: hồ sơ vãng lai chỉ là Patient,
                    // muốn liên kết tài khoản phải đi qua account_patient_profiles.
                    patient = new Patient
                    {
                        FullName = fullName,
                        DateOfBirth = request.DateOfBirth,
                        Gender = EnumCodes.ParseDatabaseCode<Gender>(request.Gender),
                        NationalId = nationalId,
                        PhoneNumber = phoneNumber,
                        Email = NullIfWhiteSpace(request.Email),
                        Address = address,
                        CreatedAt = now
                    };

                    _context.Patients.Add(patient);
                    isNewPatient = true;
                }
            }

            await UpsertInsuranceCardAsync(patient, insuranceCard, now, cancellationToken);

            var appointment = new Appointment
            {
                AppointmentCode = await GenerateAppointmentCodeAsync(cancellationToken),
                Patient = patient,
                StatusId = checkedInStatusId,
                BookingChannel = BookingChannel.OnSite,
                VisitType = VisitType.WalkIn,
                PriorityLevel = ResolvePriorityLevel(request.PriorityLevel, patient.DateOfBirth, now),
                Reason = Truncate(NullIfWhiteSpace(request.Reason), 255),
                RoomId = roomId,
                DepartmentId = department.Id,
                DoctorId = doctor?.Id,
                // ai mở ca: tài khoản quầy đăng nhập; hồ sơ vãng lai không có booked_by web.
                BookedBy = request.OperatorAccountId,
                CheckedInAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            appointment.StatusHistories.Add(new AppointmentStatusHistory
            {
                NewStatusId = checkedInStatusId,
                ChangedAt = now
            });

            _context.Appointments.Add(appointment);

            // Cấp cứu không bốc số: không giữ chỗ trong khung giờ để khỏi chiếm suất của người đã đặt,
            // lượt khám đi thẳng vào buồng bệnh và số thứ tự để trống.
            if (!request.IsEmergency)
            {
                // Số thứ tự là suất T còn trống của khung giờ đang mở; hết suất thì báo hết số chứ không cấp số ngoài luồng.
                QueuePlacement? placement = await _allocator.AssignOnSiteAsync(
                    appointment, now, roomId, doctor?.Id, cancellationToken);

                if (placement is null)
                {
                    await transaction.RollbackAsync(cancellationToken);

                    throw new InvalidOperationException(
                        $"Ca {(ExamShifts.For(now) == ExamShift.Morning ? "sáng" : "chiều")} ngày {now:dd/MM/yyyy} của phòng này đã hết số thứ tự. " +
                        "Vui lòng chọn bác sĩ khác hoặc quay lại vào ca sau.");
                }

                // Bác sĩ và phòng đọc từ khung giờ thật sự giữ chỗ để màn tiếp nhận không còn trống cột bác sĩ.
                appointment.RoomId = placement.Slot.RoomId;
                appointment.DoctorId = placement.Slot.DoctorId;
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new CreateReceptionResult(
                appointment.Id,
                appointment.AppointmentCode,
                patient.Id,
                patient.Id.ToPatientCode(),
                appointment.QueueNumber ?? (request.IsEmergency ? "CẤP CỨU" : string.Empty),
                isNewPatient);
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(
                "Không thể lưu hồ sơ tiếp nhận. Số CCCD hoặc thông tin bệnh nhân có thể đã tồn tại.",
                ex);
        }
    }

    public async Task<ReceptionActionResult> CheckInAsync(
        int appointmentId,
        int? operatorAccountId = null,
        CancellationToken cancellationToken = default)
    {
        var statusMap = await LoadStatusMapAsync(cancellationToken);
        if (!statusMap.TryGetValue(AppointmentStatus.Codes.CheckedIn, out int checkedInStatusId))
        {
            throw new InvalidOperationException("Cơ sở dữ liệu chưa có trạng thái CHECKED_IN.");
        }

        Appointment? appointment = await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Status)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Room)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Doctor)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

        if (appointment == null)
        {
            return new ReceptionActionResult(false, "Không tìm thấy lượt khám cần tiếp nhận.");
        }

        if (appointment.Status.Code is not (AppointmentStatus.Codes.Pending or AppointmentStatus.Codes.Confirmed))
        {
            return new ReceptionActionResult(
                false,
                $"Lượt khám {appointment.AppointmentCode} đang ở trạng thái \"{appointment.Status.Name}\", không thể tiếp nhận.");
        }

        DateTime now = Now;

        // Quá hạn 30 phút chỉ tính khi lượt khám có giờ hẹn (slot). Dùng created_at thay thế sẽ
        // hủy oan các lượt vừa đặt trên Web cho ngày khác.
        if (appointment.Slot is not null)
        {
            DateTime appointmentTime = appointment.Slot.StartTime;

            if (now > appointmentTime.AddMinutes(30))
            {
                bool marked = statusMap.TryGetValue(AppointmentStatus.Codes.NoShow, out int noShowStatusId);

                if (marked)
                {
                    int oldId = appointment.StatusId;
                    appointment.StatusId = noShowStatusId;
                    appointment.UpdatedAt = now;

                    _context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
                    {
                        AppointmentId = appointment.Id,
                        OldStatusId = oldId,
                        NewStatusId = noShowStatusId,
                        ChangedAt = now
                    });

                    await _context.SaveChangesAsync(cancellationToken);
                }

                string note = marked
                    ? "Hệ thống đã đánh dấu Vắng mặt (No-Show)."
                    : "Cơ sở dữ liệu chưa có trạng thái NO_SHOW nên lượt khám chưa bị đánh dấu.";

                return new ReceptionActionResult(
                    false,
                    $"Lượt khám {appointment.AppointmentCode} đã quá giờ hẹn hơn 30 phút (giờ hẹn: {appointmentTime:HH:mm}). {note}",
                    string.Empty,
                    IsOverdue: true,
                    AppointmentId: appointment.Id);
            }
        }

        string queueNumber = appointment.QueueNumber ?? string.Empty;

        await using (var transaction = await _context.Database.BeginTransactionAsync(cancellationToken))
        {
            int? queueRoomId = appointment.RoomId ?? appointment.Slot?.RoomId;

            if (queueRoomId == null)
            {
                return new ReceptionActionResult(
                    false,
                    $"Lượt {appointment.AppointmentCode} chưa xác định được phòng khám nên chưa cấp được số thứ tự.");
            }

            // Qua mốc cắt thì suất A chưa check-in đã bị giải phóng, lượt đến trễ phải xếp lại như suất T.
            await _allocator.ReleaseExpiredOnlinePositionsAsync(queueRoomId.Value, now, cancellationToken);

            if (appointment.SlotPosition == null)
            {
                int? wantedDoctorId = appointment.DoctorId ?? appointment.Slot?.DoctorId;

                QueuePlacement? placement = await _allocator.AssignOnSiteAsync(
                    appointment, now, queueRoomId, wantedDoctorId, cancellationToken);

                if (placement is null)
                {
                    return new ReceptionActionResult(
                        false,
                        $"Ca {(ExamShifts.For(now) == ExamShift.Morning ? "sáng" : "chiều")} ngày {now:dd/MM/yyyy} của phòng này đã hết số thứ tự, " +
                        $"không tiếp nhận được lượt {appointment.AppointmentCode}.");
                }

                appointment.RoomId = placement.Slot.RoomId;
                appointment.DoctorId = placement.Slot.DoctorId;
                queueNumber = placement.QueueNumber;
            }
            else if (string.IsNullOrEmpty(appointment.QueueNumber))
            {
                // Lượt web đã giữ suất nhưng chưa thanh toán nên chưa có số; quầy tiếp nhận cấp số theo đúng suất đã giữ.
                queueNumber = await _allocator.IssueQueueNumberAsync(appointment, cancellationToken) ?? string.Empty;
            }

            int oldStatusId = appointment.StatusId;
            appointment.StatusId = checkedInStatusId;
            appointment.UpdatedAt = now;
            appointment.CheckedInAt = now;

            if (appointment.RoomId == null && appointment.Slot?.RoomId is int slotRoomId)
            {
                appointment.RoomId = slotRoomId;
            }

            // Lịch hẹn web cũ chưa ghi bác sĩ/khoa trên lượt; suy ra từ slot để màn gọi số sau này có dữ liệu.
            appointment.DoctorId ??= appointment.Slot?.DoctorId;
            appointment.DepartmentId ??= appointment.Slot?.Room?.DepartmentId;

            // Lượt do bệnh nhân tự đặt đã có booked_by là tài khoản bệnh nhân; lượt quầy mở hộ thì ghi quầy.
            appointment.BookedBy ??= operatorAccountId;

            _context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
            {
                AppointmentId = appointment.Id,
                OldStatusId = oldStatusId,
                NewStatusId = checkedInStatusId,
                ChangedAt = now
            });

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return new ReceptionActionResult(
            true,
            $"Đã tiếp nhận bệnh nhân {appointment.Patient.FullName}.",
            queueNumber);
    }

    public async Task<int> ExpireOverdueAppointmentsAsync(CancellationToken cancellationToken = default)
    {
        var statusMap = await LoadStatusMapAsync(cancellationToken);
        if (!statusMap.TryGetValue(AppointmentStatus.Codes.NoShow, out int noShowStatusId))
        {
            return 0;
        }

        var waitingIds = ResolveIds(statusMap, WaitingCodes);
        if (waitingIds.Count == 0) return 0;

        DateTime now = Now;
        DateTime overdueThreshold = now.AddMinutes(-30);

        // Mốc 30 phút phải tính từ LẦN GỌI SỐ ĐẦU TIÊN (appointments.called_at), theo nghiệp vụ đã chốt.
        // Gọi màn hình bác sĩ chưa làm trong đợt này nên cột called_at đang trống: chưa gọi số thì
        // chưa được tự đánh NO_SHOW. Quét theo slot.start_time từng hủy oan lịch hẹn web nên bỏ hẳn.
        var overdueAppointments = await _context.Appointments
            .Where(a => a.CalledAt != null
                     && a.CalledAt < overdueThreshold
                     && waitingIds.Contains(a.StatusId))
            .ToListAsync(cancellationToken);

        if (overdueAppointments.Count == 0) return 0;

        foreach (var app in overdueAppointments)
        {
            int oldStatusId = app.StatusId;
            app.StatusId = noShowStatusId;
            app.UpdatedAt = now;

            _context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
            {
                AppointmentId = app.Id,
                OldStatusId = oldStatusId,
                NewStatusId = noShowStatusId,
                ChangedAt = now
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        return overdueAppointments.Count;
    }

    public async Task<ReceptionActionResult> ConvertToWalkInAsync(
        int appointmentId,
        int? operatorAccountId = null,
        CancellationToken cancellationToken = default)
    {
        var statusMap = await LoadStatusMapAsync(cancellationToken);
        if (!statusMap.TryGetValue(AppointmentStatus.Codes.CheckedIn, out int checkedInStatusId))
        {
            throw new InvalidOperationException("Cơ sở dữ liệu chưa có trạng thái CHECKED_IN.");
        }

        var sourceAppointment = await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Status)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Doctor)
            .Include(a => a.Room)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

        if (sourceAppointment == null)
        {
            return new ReceptionActionResult(false, "Không tìm thấy lượt khám gốc để chuyển.");
        }

        string[] reopenableCodes =
        {
            AppointmentStatus.Codes.Pending,
            AppointmentStatus.Codes.Confirmed,
            AppointmentStatus.Codes.Cancelled,
            AppointmentStatus.Codes.NoShow,
            AppointmentStatus.Codes.Expired
        };

        if (!reopenableCodes.Contains(sourceAppointment.Status.Code))
        {
            return new ReceptionActionResult(
                false,
                $"Lượt khám {sourceAppointment.AppointmentCode} đang ở trạng thái \"{sourceAppointment.Status.Name}\", không cần cấp lại số trực tiếp.");
        }

        var existingChild = await _context.Appointments
            .AsNoTracking()
            .Where(a => a.ParentAppointmentId == appointmentId)
            .OrderByDescending(a => a.Id)
            .Select(a => new { a.Id, a.QueueNumber })
            .FirstOrDefaultAsync(cancellationToken);

        if (existingChild != null)
        {
            return new ReceptionActionResult(
                false,
                $"Lượt {sourceAppointment.AppointmentCode} đã được cấp số trực tiếp trước đó.",
                existingChild.QueueNumber ?? string.Empty,
                AppointmentId: existingChild.Id);
        }

        List<int> completedIds = ResolveIds(statusMap, new[] { AppointmentStatus.Codes.Completed });
        bool hasCompletedToday = completedIds.Count > 0
            && await InDay(_context.Appointments.AsNoTracking(), Today)
                .AnyAsync(a => a.PatientId == sourceAppointment.PatientId && completedIds.Contains(a.StatusId), cancellationToken);

        if (hasCompletedToday)
        {
            return new ReceptionActionResult(
                false,
                $"Bệnh nhân {sourceAppointment.Patient.FullName} đã có ca khám hoàn thành trong hôm nay nên không mở lượt thường mới.");
        }

        int? roomId = sourceAppointment.RoomId ?? sourceAppointment.Slot?.RoomId;
        if (roomId == null)
        {
            return new ReceptionActionResult(
                false,
                $"Lượt {sourceAppointment.AppointmentCode} chưa xác định được phòng khám. Hãy dùng chức năng Tiếp nhận mới.");
        }

        DateTime now = Now;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        List<int> waitingIds = ResolveIds(statusMap, WaitingCodes);
        statusMap.TryGetValue(AppointmentStatus.Codes.NoShow, out int noShowStatusId);
        bool closeSource = noShowStatusId != 0 && waitingIds.Contains(sourceAppointment.StatusId);

        if (closeSource)
        {
            int oldSourceStatusId = sourceAppointment.StatusId;
            sourceAppointment.StatusId = noShowStatusId;
            sourceAppointment.UpdatedAt = now;

            // Lượt gốc bỏ chỗ đang giữ để suất đó mở lại cho người khác; số mới sẽ cấp ở dưới.
            QueuePositionAllocator.ReleaseSeat(sourceAppointment);

            _context.AppointmentStatusHistories.Add(new AppointmentStatusHistory
            {
                AppointmentId = sourceAppointment.Id,
                OldStatusId = oldSourceStatusId,
                NewStatusId = noShowStatusId,
                ChangedAt = now
            });
        }

        var newAppointment = new Appointment
        {
            AppointmentCode = await GenerateAppointmentCodeAsync(cancellationToken),
            PatientId = sourceAppointment.PatientId,
            StatusId = checkedInStatusId,
            BookingChannel = BookingChannel.OnSite,
            VisitType = VisitType.WalkIn,
            PriorityLevel = ResolvePriorityLevel(sourceAppointment.PriorityLevel, sourceAppointment.Patient.DateOfBirth, now),
            Reason = Truncate($"Chuyển sang bốc số trực tiếp từ lịch hẹn trễ {sourceAppointment.AppointmentCode}: {sourceAppointment.Reason}", 255),
            RoomId = roomId,
            DepartmentId = sourceAppointment.DepartmentId ?? sourceAppointment.Slot?.Room?.DepartmentId,
            DoctorId = sourceAppointment.DoctorId ?? sourceAppointment.Slot?.DoctorId,
            BookedBy = sourceAppointment.BookedBy ?? operatorAccountId,
            CheckedInAt = now,
            ParentAppointmentId = sourceAppointment.Id,
            CreatedAt = now,
            UpdatedAt = now
        };

        newAppointment.StatusHistories.Add(new AppointmentStatusHistory
        {
            NewStatusId = checkedInStatusId,
            ChangedAt = now
        });

        _context.Appointments.Add(newAppointment);

        // Phải lưu việc đóng lượt gốc trước khi cấp số: bộ cấp vị trí đọc chỗ đang bị giữ từ CSDL.
        await _context.SaveChangesAsync(cancellationToken);

        QueuePlacement? placement = await _allocator.AssignOnSiteAsync(
            newAppointment, now, roomId, newAppointment.DoctorId, cancellationToken);

        if (placement is null)
        {
            return new ReceptionActionResult(
                false,
                $"Ca {(ExamShifts.For(now) == ExamShift.Morning ? "sáng" : "chiều")} ngày {now:dd/MM/yyyy} của phòng này đã hết số thứ tự, " +
                $"không chuyển được lượt {sourceAppointment.AppointmentCode} sang bốc số trực tiếp.");
        }

        newAppointment.RoomId = placement.Slot.RoomId;
        newAppointment.DoctorId = placement.Slot.DoctorId;

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ReceptionActionResult(
            true,
            $"Đã chuyển bệnh nhân {sourceAppointment.Patient.FullName} sang bốc số trực tiếp thành công.",
            placement.QueueNumber,
            AppointmentId: newAppointment.Id);
    }

    public async Task<ReceptionActionResult> AssignStaffPriorityAsync(
        int appointmentId,
        int? operatorAccountId = null,
        CancellationToken cancellationToken = default)
    {
        var statusMap = await LoadStatusMapAsync(cancellationToken);
        var waitingIds = ResolveIds(statusMap, WaitingCodes);

        if (waitingIds.Count == 0)
        {
            return new ReceptionActionResult(false, "Cơ sở dữ liệu chưa có trạng thái chờ để xếp hàng.");
        }

        var appointment = await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Status)
            .Include(a => a.Slot)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

        if (appointment == null)
        {
            return new ReceptionActionResult(false, "Không tìm thấy lượt khám để gán ưu tiên.");
        }

        if (!waitingIds.Contains(appointment.StatusId))
        {
            return new ReceptionActionResult(
                false,
                $"Lượt {appointment.AppointmentCode} đang ở trạng thái \"{appointment.Status.Name}\", không còn trong hàng đợi nên không gán ưu tiên.");
        }

        if (appointment.PriorityLevel == PriorityLevels.StaffPriority)
        {
            return new ReceptionActionResult(
                false,
                $"Lượt {appointment.AppointmentCode} đã được gán ưu tiên trước đó.",
                appointment.QueueNumber ?? string.Empty);
        }

        int? roomId = appointment.RoomId ?? appointment.Slot?.RoomId;
        if (roomId == null)
        {
            return new ReceptionActionResult(false, $"Lượt {appointment.AppointmentCode} chưa xác định được phòng khám nên không xếp hàng được.");
        }

        DateTime now = Now;

        // Hàng đợi đang xét là các lượt còn chờ của cùng phòng trong hôm nay; một ngày của một phòng
        // chỉ vài chục lượt nên lọc phòng trong bộ nhớ để bắt cả lượt chỉ có room_id trên khung giờ.
        // Phải lọc theo ngày, không lọc từ giờ hiện tại: các khung đã chạy trong buổi sáng vẫn còn người chờ.
        var dayWaiting = await InDay(_context.Appointments.Include(a => a.Slot), Today)
            .Where(a => waitingIds.Contains(a.StatusId))
            .ToListAsync(cancellationToken);

        var waiting = dayWaiting.Where(a => (a.RoomId ?? a.Slot?.RoomId) == roomId.Value).ToList();

        // Hàng đợi dựng trước khi gán ưu tiên: chỗ đứng tự nhiên phải là chỗ thật của lượt này,
        // nếu không số người bị chen sẽ tính sai.
        var queue = ApplyCallOrder(waiting);
        int naturalIndex = queue.FindIndex(a => a.Id == appointment.Id);

        if (naturalIndex < 0)
        {
            return new ReceptionActionResult(false, $"Lượt {appointment.AppointmentCode} không nằm trong hàng đợi của phòng này.");
        }

        appointment.PriorityLevel = PriorityLevels.StaffPriority;
        appointment.UpdatedAt = now;

        var passed = new List<Appointment>();
        int index = naturalIndex;

        while (passed.Count < _options.MaxOvertakeAhead && index > 0)
        {
            var ahead = queue[index - 1];

            // Không chen qua một lượt ưu tiên khác, và không chen qua người đã bị chen đủ trần.
            if (ahead.PriorityLevel >= PriorityLevels.StaffPriority
                || ahead.OvertakenCount >= _options.MaxTimesOvertaken)
            {
                break;
            }

            index--;
            ahead.OvertakenCount++;
            ahead.UpdatedAt = now;
            passed.Add(ahead);
        }

        await _context.SaveChangesAsync(cancellationToken);

        string message = passed.Count == 0
            ? $"Đã gán ưu tiên cho lượt {appointment.AppointmentCode} nhưng không chen được ai: " +
              $"người đứng trước đều đã bị chen đủ {_options.MaxTimesOvertaken} lần."
            : $"Đã gán ưu tiên cho {appointment.Patient.FullName} (số {appointment.QueueNumber}), " +
              $"vượt lên trước {passed.Count} người: {string.Join(", ", passed.Select(p => p.QueueNumber ?? p.AppointmentCode))}.";

        return new ReceptionActionResult(true, message, appointment.QueueNumber ?? string.Empty, AppointmentId: appointment.Id);
    }

    // Mức ưu tiên: mức 2 chỉ do nhân viên gán nên giữ nguyên; còn lại hệ thống tự tính từ ngày sinh
    // (trẻ dưới PriorityChildUnderAge tuổi hoặc người từ PrioritySeniorFromAge tuổi -> mức 1).
    private int ResolvePriorityLevel(int requestedLevel, DateOnly dateOfBirth, DateTime now)
    {
        int requested = Math.Clamp(requestedLevel, PriorityLevels.Normal, PriorityLevels.Max);

        if (requested == PriorityLevels.StaffPriority)
        {
            return PriorityLevels.StaffPriority;
        }

        bool agePriority = PatientAge.NeedsAgePriority(
            dateOfBirth,
            DateOnly.FromDateTime(now),
            _options.PriorityChildUnderAge,
            _options.PrioritySeniorFromAge);

        return agePriority ? PriorityLevels.AgePriority : requested;
    }

    private void ValidateRequest(CreateReceptionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new ArgumentException("Vui lòng nhập họ tên bệnh nhân.");
        }

        if (request.FullName.Trim().Length > 100)
        {
            throw new ArgumentException("Họ tên bệnh nhân không được vượt quá 100 ký tự.");
        }

        if (request.DateOfBirth == default || request.DateOfBirth > DateOnly.FromDateTime(Today))
        {
            throw new ArgumentException("Ngày sinh không hợp lệ.");
        }

        if (EnumCodes.ParseDatabaseCode<Gender>(request.Gender) is null)
        {
            throw new ArgumentException("Vui lòng chọn giới tính.");
        }

        string phone = NormalizePhone(request.PhoneNumber);
        if (phone.Length is < 9 or > 15 || !phone.All(char.IsDigit))
        {
            throw new ArgumentException("Số điện thoại phải gồm 9 đến 15 chữ số.");
        }

        if (request.DepartmentId <= 0)
        {
            throw new ArgumentException("Vui lòng chọn khoa/phòng khám.");
        }

        if (!string.IsNullOrWhiteSpace(request.NationalId) && request.NationalId.Trim().Length > 20)
        {
            throw new ArgumentException("Số CCCD/hộ chiếu không được vượt quá 20 ký tự.");
        }

        if (!string.IsNullOrWhiteSpace(request.InsuranceCardNumber) && request.InsuranceCardNumber.Trim().Length > 20)
        {
            throw new ArgumentException("Số BHYT không được vượt quá 20 ký tự.");
        }
    }

    private async Task<Dictionary<string, int>> LoadStatusMapAsync(CancellationToken cancellationToken)
    {
        List<AppointmentStatus> statuses = await _context.AppointmentStatuses
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return statuses.ToDictionary(s => s.Code, s => s.Id, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<string> GenerateAppointmentCodeAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            string code = AppointmentCode.Generate(DateOnly.FromDateTime(Now));
            bool exists = await _context.Appointments
                .AsNoTracking()
                .AnyAsync(a => a.AppointmentCode == code, cancellationToken);

            if (!exists)
            {
                return code;
            }
        }

        throw new InvalidOperationException("Không thể sinh mã lượt khám mới. Vui lòng thử lại.");
    }

    // Số thẻ BHYT nằm ở insurance_cards (không còn cột trên patients).
    // Chưa có kế hoạch/thời hạn trong form quầy nên dùng kế hoạch nội tuyến đầu tiên và năm hiện hành.
    private async Task UpsertInsuranceCardAsync(Patient patient, string? cardNumber, DateTime now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return;
        }

        var normalizedCard = cardNumber.Trim();

        // 1. Kiểm tra xem thẻ này đã được cấp cho bệnh nhân khác chưa (tránh chuyển nhượng trái phép thẻ BHYT sang người khác)
        var cardByNumber = await _context.InsuranceCards
            .FirstOrDefaultAsync(c => c.CardNumber == normalizedCard, cancellationToken);

        if (cardByNumber != null && cardByNumber.PatientId != patient.Id)
        {
            throw new ArgumentException($"Thẻ BHYT '{normalizedCard}' đã được liên kết với một bệnh nhân khác trong hệ thống.");
        }

        // 2. Nếu chính bệnh nhân này đã có thẻ với số này, kích hoạt lại thẻ
        if (cardByNumber != null)
        {
            cardByNumber.IsActive = true;
            return;
        }

        // 3. Nếu số thẻ mới hoàn toàn, tắt các thẻ cũ của bệnh nhân (không ghi đè số lên thẻ cũ để bảo toàn lịch sử và quyền lợi bảo hiểm)
        var oldActiveCards = await _context.InsuranceCards
            .Where(c => c.PatientId == patient.Id && c.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var old in oldActiveCards)
        {
            old.IsActive = false;
        }

        int? defaultPlanId = await _context.InsurancePlans
            .AsNoTracking()
            .Where(p => p.ReferralType == ReferralType.InNetwork)
            .OrderBy(p => p.Id)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        defaultPlanId ??= await _context.InsurancePlans
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (defaultPlanId is null)
        {
            return;
        }

        int year = now.Year;
        _context.InsuranceCards.Add(new InsuranceCard
        {
            Patient = patient,
            CardNumber = cardNumber,
            InsurancePlanId = defaultPlanId.Value,
            ValidFrom = new DateOnly(year, 1, 1),
            ValidTo = new DateOnly(year, 12, 31),
            IsActive = true,
            CreatedAt = now
        });
    }

    private static IQueryable<Appointment> InDay(IQueryable<Appointment> query, DateTime day)
    {
        DateTime end = day.AddDays(1);

        return query.Where(a =>
            (a.Slot != null && a.Slot.StartTime >= day && a.Slot.StartTime < end) ||
            (a.SlotId == null && a.CreatedAt >= day && a.CreatedAt < end));
    }

    private static List<int> ResolveIds(IReadOnlyDictionary<string, int> statusMap, IEnumerable<string> codes)
    {
        return codes
            .Where(code => statusMap.ContainsKey(code))
            .Select(code => statusMap[code])
            .ToList();
    }

    private static ReceptionItemDto MapItem(Appointment appointment, bool hasPendingLink)
    {
        string departmentName = appointment.Room?.Department.Name
            ?? appointment.Slot?.Room?.Department?.Name
            ?? "Chưa phân khoa";

        // Walk-in không có slot nên bác sĩ phải đọc từ appointments.doctor_id, không phải slot.Doctor.
        string doctorName = appointment.Doctor?.FullName
            ?? appointment.Slot?.Doctor.FullName
            ?? string.Empty;
        DateTime appointmentTime = appointment.Slot?.StartTime ?? appointment.CreatedAt;

        return new ReceptionItemDto(
            appointment.Id,
            appointment.AppointmentCode,
            appointment.Patient.Id,
            appointment.Patient.Id.ToPatientCode(),
            appointment.Patient.FullName,
            appointment.Patient.Gender.ToLabel(),
            appointment.Patient.DateOfBirth,
            ActiveInsuranceCardNumber(appointment.Patient),
            departmentName,
            doctorName,
            appointment.Status.Code,
            appointment.Status.Name,
            appointmentTime,
            appointment.BookingChannel.ToLabel(),
            appointment.VisitType.ToLabel(),
            appointment.PriorityLevel,
            appointment.PriorityLevel.ToPriorityVietnamese(),
            appointment.QueueNumber ?? string.Empty,
            appointment.Reason ?? string.Empty,
            BookedByName(appointment.BookedByAccount),
            hasPendingLink,
            InsuranceDeclarationLabel(appointment));
    }

    // Quầy nhắc người bệnh mang thẻ và làm thủ tục viện phí theo đúng khai báo, không phải theo số thẻ đã lưu.
    private static string InsuranceDeclarationLabel(Appointment a) =>
        (a.HasHealthInsurance, a.HasPrivateInsurance) switch
        {
            (true, true) => "BHYT + tư nhân",
            (true, false) => "BHYT",
            (false, true) => "Tư nhân",
            _ => string.Empty
        };

    // Quầy cần phân biệt ai mở ca: bệnh nhân tự đặt trên web hay nhân viên nhập hộ.
    private static string BookedByName(Account? account) =>
        account == null
            ? "Chưa ghi nhận"
            : string.Equals(account.Role?.Code, "PATIENT", StringComparison.OrdinalIgnoreCase)
                ? $"{account.Username} (bệnh nhân)"
                : $"{account.Username} · {account.Role?.Name ?? "Nhân viên"}";

    private async Task<HashSet<int>> LoadPendingLinkPatientIdsAsync(
        List<int> patientIds,
        CancellationToken cancellationToken)
    {
        if (patientIds.Count == 0)
        {
            return new HashSet<int>();
        }

        List<int> pending = await _context.AccountPatientProfiles
            .AsNoTracking()
            .Where(l => patientIds.Contains(l.PatientId) && l.Status == LinkStatus.Pending)
            .Select(l => l.PatientId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return pending.ToHashSet();
    }

    private static ReceptionPatientDto MapPatient(Patient patient)
    {
        return new ReceptionPatientDto(
            patient.Id,
            patient.Id.ToPatientCode(),
            patient.FullName,
            patient.DateOfBirth,
            // DTO này nuôi form nhập hồ sơ: giữ mã (MALE/FEMALE/OTHER) để combo khớp giá trị gửi về,
            // còn danh sách quầy dùng ReceptionItemDto.Gender đã dịch sang nhãn tiếng Việt.
            patient.Gender is Gender gender ? gender.ToDatabaseCode() : "OTHER",
            patient.PhoneNumber,
            patient.NationalId ?? string.Empty,
            patient.Address ?? string.Empty,
            ActiveInsuranceCardNumber(patient));
    }

    // Cột insurance_card_number không còn trên patients; thẻ đang hoạt động lấy từ insurance_cards.
    private static string ActiveInsuranceCardNumber(Patient patient)
    {
        return patient.InsuranceCards
            .Where(c => c.IsActive)
            .OrderByDescending(c => c.ValidTo)
            .Select(c => c.CardNumber)
            .FirstOrDefault() ?? string.Empty;
    }

    private static int? TryParsePatientCode(string keyword)
    {
        if (keyword.StartsWith("BN", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(keyword.AsSpan(2), out int patientId))
        {
            return patientId;
        }

        return null;
    }

    private static string NormalizePhone(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return string.Empty;
        }

        return new string(phoneNumber.Where(c => !char.IsWhiteSpace(c) && c != '.' && c != '-').ToArray());
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Truncate(string? value, int maxLength)
    {
        return value == null || value.Length <= maxLength ? value ?? string.Empty : value[..maxLength];
    }

    // ==========================================
    // Web Quầy Tiếp Nhận (Reception Desk API)
    // ==========================================

    /// <summary>
    /// Lấy dữ liệu bàn tiếp nhận cho nhân viên điều dưỡng/tiếp đón bao gồm danh sách ca khám trong ngày và tìm kiếm bệnh nhân.
    /// </summary>
    /// <param name="staffAccountId">Mã tài khoản nhân viên tiếp đón.</param>
    /// <param name="search">Từ khóa tìm kiếm (họ tên, CCCD, SĐT, số thứ tự).</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    /// <returns>Dữ liệu bàn tiếp đón (ReceptionDesk).</returns>
    public async Task<Result<ReceptionDesk>> GetDeskAsync(int staffAccountId, string? search, CancellationToken ct = default)
    {
        var nurse = await _context.Nurses
            .Include(n => n.Department)
            .FirstOrDefaultAsync(n => n.AccountId == staffAccountId, ct);

        int departmentId = nurse?.DepartmentId ?? 0;
        string departmentName = nurse?.Department?.Name ?? "";

        if (departmentId <= 0)
        {
            var firstDept = await _context.Departments.OrderBy(d => d.Id).FirstOrDefaultAsync(ct);
            if (firstDept != null)
            {
                departmentId = firstDept.Id;
                departmentName = firstDept.Name;
            }
        }

        var todayDate = DateOnly.FromDateTime(Now);
        var start = todayDate.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);

        var visitsQuery = _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Room)
            .Include(a => a.Slot)
            .Where(a => (departmentId <= 0 || a.DepartmentId == departmentId)
                && ((a.Slot != null && a.Slot.StartTime >= start && a.Slot.StartTime < end)
                    || (a.Slot == null && a.CreatedAt >= start && a.CreatedAt < end)));

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            visitsQuery = visitsQuery.Where(a =>
                a.AppointmentCode.Contains(search)
                || a.Patient.PhoneNumber.Contains(search)
                || (a.Patient.NationalId != null && a.Patient.NationalId.Contains(search))
                || (a.QueueNumber != null && a.QueueNumber.Contains(search))
                || a.Patient.FullName.Contains(search));
        }

        var rows = await visitsQuery
            .OrderByDescending(a => a.PriorityLevel)
            .ThenBy(a => a.Slot != null ? a.Slot.StartTime : a.CreatedAt)
            .Select(a => new ReceptionVisit(
                a.Id,
                a.AppointmentCode,
                a.Patient.FullName,
                a.Patient.DateOfBirth,
                a.Patient.PhoneNumber,
                a.Doctor != null ? a.Doctor.FullName : "Chưa phân công",
                a.Room != null ? a.Room.RoomNumber : "",
                a.Slot != null ? a.Slot.StartTime : a.CreatedAt,
                a.BookingChannel,
                a.StatusId,
                a.QueueNumber,
                a.PriorityLevel))
            .Take(100)
            .ToListAsync(ct);

        var schedules = await _context.DoctorSchedules
            .AsNoTracking()
            .Include(s => s.Doctor)
            .Include(s => s.Room)
            .Where(s => (departmentId <= 0 || s.Doctor.DepartmentId == departmentId)
                && s.Doctor.Account.IsActive
                && s.IsActive
                && s.DayOfWeek == todayDate.DayOfWeek
                && s.EffectiveFrom <= todayDate
                && (s.EffectiveTo == null || s.EffectiveTo >= todayDate))
            .OrderBy(s => s.StartTime)
            .ThenBy(s => s.Doctor.FullName)
            .ToListAsync(ct);

        var choices = schedules
            .Select(s => new ReceptionSchedule(
                s.Id,
                $"{s.Doctor.FullName} · Phòng {s.Room.RoomNumber} · {s.StartTime:HH:mm}–{s.EndTime:HH:mm}"))
            .ToList();

        var patients = !string.IsNullOrWhiteSpace(search) && search.Trim().Length >= 3
            ? await _context.Patients
                .AsNoTracking()
                .Where(p => p.PhoneNumber.Contains(search.Trim())
                    || (p.NationalId != null && p.NationalId.Contains(search.Trim()))
                    || p.FullName.Contains(search.Trim()))
                .OrderBy(p => p.FullName)
                .Select(p => new ReceptionPatient(
                    p.Id,
                    p.FullName,
                    p.DateOfBirth,
                    p.PhoneNumber))
                .Take(20)
                .ToListAsync(ct)
            : (IReadOnlyList<ReceptionPatient>)[];

        var allDepts = await _context.Departments
            .AsNoTracking()
            .OrderBy(d => d.Id)
            .Select(d => new DepartmentOptionDto(d.Id, d.Name))
            .ToListAsync(ct);

        return Result.Success(new ReceptionDesk(
            departmentName,
            todayDate,
            rows,
            choices,
            patients,
            allDepts));
    }

    /// <summary>
    /// Tiếp nhận (check-in) cho lượt khám đặt trước sau khi nhân viên đã xác minh danh tính giấy tờ.
    /// </summary>
    /// <param name="staffAccountId">Mã tài khoản nhân viên tiếp nhận.</param>
    /// <param name="appointmentId">Mã lượt khám.</param>
    /// <param name="identityVerified">Cờ xác nhận đã đối chiếu giấy tờ tùy thân.</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    /// <returns>Số thứ tự khám (STT) vừa được cấp phát.</returns>
    public async Task<Result<string>> CheckInAsync(int staffAccountId, int appointmentId, bool identityVerified, CancellationToken ct = default)
    {
        if (!identityVerified)
        {
            return Result.Failure<string>("NOT_VERIFIED", "Cần đối chiếu danh tính người khám trước khi tiếp nhận.");
        }

        var res = await CheckInAsync(appointmentId, staffAccountId, ct);
        if (!res.Success)
        {
            return Result.Failure<string>("CHECK_IN_FAILED", res.Message);
        }

        return Result.Success(res.QueueNumber);
    }

    /// <summary>
    /// Đăng ký tiếp nhận lượt khám vãng lai (walk-in) trực tiếp tại quầy và cấp số thứ tự khám ngay.
    /// </summary>
    /// <param name="staffAccountId">Mã tài khoản nhân viên tiếp nhận.</param>
    /// <param name="request">Thông tin đăng ký vãng lai (bệnh nhân, lịch trực, BHYT, mức ưu tiên).</param>
    /// <param name="ct">Token hủy tác vụ bất đồng bộ.</param>
    /// <returns>Thông tin phiếu khám vừa được cấp phát.</returns>
    public async Task<Result<WalkInTicketDto>> RegisterWalkInAsync(int staffAccountId, WalkInReceptionRequest request, CancellationToken ct = default)
    {
        if (!request.IdentityVerified)
        {
            return Result.Failure<WalkInTicketDto>("NOT_VERIFIED", "Cần đối chiếu danh tính người khám trước khi tiếp nhận.");
        }

        var schedule = await _context.DoctorSchedules
            .Include(s => s.Doctor)
                .ThenInclude(d => d.Department)
            .Include(s => s.Doctor)
                .ThenInclude(d => d.Account)
            .Include(s => s.Room)
            .FirstOrDefaultAsync(s => s.Id == request.ScheduleId && s.IsActive, ct);

        if (schedule == null)
        {
            return Result.Failure<WalkInTicketDto>("NOT_FOUND", "Không tìm thấy lịch khám hoặc phòng khám được chọn.");
        }

        // Bác sĩ được chọn phải thuộc đúng chuyên khoa đăng ký
        if (request.DepartmentId > 0 && schedule.Doctor.DepartmentId != request.DepartmentId)
        {
            return Result.Failure<WalkInTicketDto>("INVALID_DEPARTMENT", "Bác sĩ hoặc phòng khám được chọn không thuộc chuyên khoa đã chọn.");
        }

        // Kiểm tra lịch khám có hiệu lực cho ngày hôm nay hay không (tránh cache cũ qua nửa đêm)
        var today = DateOnly.FromDateTime(Now);
        if (schedule.DayOfWeek != Now.DayOfWeek ||
            schedule.EffectiveFrom > today ||
            (schedule.EffectiveTo.HasValue && schedule.EffectiveTo.Value < today) ||
            (schedule.Doctor.Account != null && !schedule.Doctor.Account.IsActive))
        {
            return Result.Failure<WalkInTicketDto>("INVALID_SCHEDULE", "Lịch trực này không áp dụng cho ngày hôm nay hoặc bác sĩ tạm ngưng tiếp nhận.");
        }

        // Kiểm tra ca khám đã kết thúc hôm nay chưa
        var currentTime = TimeOnly.FromDateTime(Now);
        if (currentTime > schedule.EndTime)
        {
            return Result.Failure<WalkInTicketDto>("SHIFT_ENDED", "Ca khám của bác sĩ đã kết thúc hôm nay. Vui lòng chọn ca trực tiếp theo.");
        }

        Patient? patient = null;
        int? resolvedPatientId = request.PatientId;
        if (resolvedPatientId.HasValue && resolvedPatientId.Value > 0)
        {
            patient = await _context.Patients.FirstOrDefaultAsync(p => p.Id == resolvedPatientId.Value, ct);
        }

        var fullName = request.FullName?.Trim() ?? patient?.FullName ?? "";
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return Result.Failure<WalkInTicketDto>("INVALID_NAME", "Vui lòng nhập họ và tên người khám.");
        }

        var birthDate = request.DateOfBirth ?? patient?.DateOfBirth ?? DateOnly.FromDateTime(DateTime.Today);
        var gender = (request.Gender ?? patient?.Gender ?? Gender.Male).ToString();
        var phone = request.PhoneNumber?.Trim() ?? patient?.PhoneNumber ?? "";

        var isChild = request.IsChild || birthDate > DateOnly.FromDateTime(DateTime.Today.AddYears(-15));

        // Đối chiếu danh tính hồ sơ cũ: Nếu chuyển sang chế độ trẻ em hoặc tên người khám không khớp hồ sơ cũ -> coi như bệnh nhân mới, không ghi đè hồ sơ người lớn
        if (patient != null)
        {
            bool nameMatches = string.Equals(patient.FullName.Trim(), fullName, StringComparison.OrdinalIgnoreCase);
            bool adultToChildMismatch = isChild && patient.DateOfBirth <= DateOnly.FromDateTime(DateTime.Today.AddYears(-15));
            if (!nameMatches || adultToChildMismatch)
            {
                patient = null;
                resolvedPatientId = null;
            }
        }

        string? combinedReason = request.Reason?.Trim();
        if (isChild && !string.IsNullOrWhiteSpace(request.GuardianName))
        {
            var guardianInfo = $"[Người giám hộ: {request.GuardianName.Trim()}" +
                (!string.IsNullOrWhiteSpace(request.GuardianRelationship) ? $" ({request.GuardianRelationship.Trim()})" : "") +
                (!string.IsNullOrWhiteSpace(request.GuardianPhone) ? $" - SĐT: {request.GuardianPhone.Trim()}" : "") +
                (!string.IsNullOrWhiteSpace(request.GuardianNationalId) ? $" - CCCD: {request.GuardianNationalId.Trim()}" : "") +
                "]";
            combinedReason = string.IsNullOrWhiteSpace(combinedReason) ? guardianInfo : $"{guardianInfo} {combinedReason}";
        }

        if (string.IsNullOrWhiteSpace(phone) && !string.IsNullOrWhiteSpace(request.GuardianPhone))
        {
            phone = request.GuardianPhone.Trim();
        }

        var resolvedNationalId = isChild && string.IsNullOrWhiteSpace(request.NationalId)
            ? null
            : (request.NationalId?.Trim() ?? patient?.NationalId);

        if (!isChild && string.IsNullOrWhiteSpace(resolvedNationalId))
        {
            return Result.Failure<WalkInTicketDto>("INVALID_NATIONAL_ID", "Vui lòng nhập số CCCD/Định danh cho bệnh nhân người lớn.");
        }

        var createReq = new CreateReceptionRequest
        {
            PatientId = resolvedPatientId,
            FullName = fullName,
            DateOfBirth = birthDate,
            Gender = gender,
            PhoneNumber = phone,
            NationalId = resolvedNationalId,
            Email = request.Email?.Trim() ?? patient?.Email,
            Address = request.Address?.Trim() ?? patient?.Address,
            InsuranceCardNumber = request.InsuranceCardNumber?.Trim(),
            DepartmentId = schedule.Doctor.DepartmentId,
            DoctorId = schedule.DoctorId,
            RoomId = schedule.RoomId,
            PriorityLevel = request.PriorityLevel,
            Reason = combinedReason,
            IsEmergency = false,
            OperatorAccountId = staffAccountId
        };

        CreateReceptionResult createRes;
        try
        {
            createRes = await CreateReceptionAsync(createReq, ct);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<WalkInTicketDto>("VALIDATION_ERROR", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<WalkInTicketDto>("INVALID_OPERATION", ex.Message);
        }

        // Tác vụ lấy phiếu khám diễn ra sau khi lượt khám đã lưu thành công vào CSDL.
        // Nếu có lỗi truy vấn vé thì dùng vé dự phòng fallback, tuyệt đối không báo false failure làm nhân viên đăng ký trùng lặp.
        WalkInTicketDto? ticket = null;
        try
        {
            var ticketRes = await GetWalkInTicketAsync(null, createRes.AppointmentId, ct);
            if (ticketRes.IsSuccess)
            {
                ticket = ticketRes.Value;
            }
        }
        catch
        {
            // Bỏ qua lỗi query vé, dùng fallback bên dưới
        }

        ticket ??= new WalkInTicketDto(
            createRes.AppointmentId,
            createRes.AppointmentCode,
            createRes.QueueNumber,
            fullName,
            schedule.Doctor.Department?.Name ?? "Khoa khám",
            schedule.Room?.RoomNumber ?? "Phòng khám",
            schedule.Doctor.FullName,
            0,
            Now
        );

        return Result.Success(ticket);
    }

    /// <summary>
    /// Kiểm tra giới hạn số lượng hồ sơ dùng chung 1 địa chỉ email.
    /// </summary>
    private async Task ValidateEmailQuotaAsync(string? email, int? excludePatientId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        var normalized = email.Trim().ToLowerInvariant();
        var query = _context.Patients.AsNoTracking().Where(p => p.Email != null && p.Email.ToLower() == normalized);
        if (excludePatientId.HasValue && excludePatientId.Value > 0)
        {
            query = query.Where(p => p.Id != excludePatientId.Value);
        }
        int count = await query.CountAsync(ct);
        if (count >= _options.MaxProfilesPerEmail)
        {
            throw new ArgumentException($"Email '{email.Trim()}' đã được dùng cho tối đa {_options.MaxProfilesPerEmail} hồ sơ bệnh nhân.");
        }
    }

    /// <summary>
    /// Tra cứu hồ sơ người khám theo số CCCD (định danh 12 số).
    /// </summary>
    public async Task<Result<PatientSearchResultDto?>> FindPatientByNationalIdAsync(string nationalId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nationalId))
        {
            return Result.Success<PatientSearchResultDto?>(null);
        }

        nationalId = nationalId.Trim();
        var patient = await _context.Patients
            .AsNoTracking()
            .Include(p => p.InsuranceCards)
            .FirstOrDefaultAsync(p => p.NationalId == nationalId, ct);

        if (patient == null)
        {
            return Result.Success<PatientSearchResultDto?>(null);
        }

        var activeCard = patient.InsuranceCards
            .OrderByDescending(c => c.ValidTo)
            .FirstOrDefault();

        var isChild = patient.DateOfBirth > DateOnly.FromDateTime(DateTime.Today.AddYears(-15));
        var dto = new PatientSearchResultDto(
            patient.Id,
            patient.FullName,
            patient.DateOfBirth.ToString("yyyy-MM-dd"),
            patient.Gender?.ToString() ?? "Male",
            patient.PhoneNumber,
            patient.NationalId,
            patient.Email,
            patient.Address,
            activeCard?.CardNumber,
            isChild
        );

        return Result.Success<PatientSearchResultDto?>(dto);
    }

    /// <summary>
    /// Tìm kiếm bệnh nhân theo từ khóa (CCCD, SĐT, hoặc Họ tên).
    /// </summary>
    public async Task<Result<IReadOnlyList<PatientSearchResultDto>>> SearchPatientsAsync(string keyword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword) || keyword.Trim().Length < 2)
        {
            return Result.Success<IReadOnlyList<PatientSearchResultDto>>([]);
        }

        var term = keyword.Trim();
        var patients = await _context.Patients
            .AsNoTracking()
            .Include(p => p.InsuranceCards)
            .Where(p => (p.NationalId != null && p.NationalId.Contains(term))
                || p.PhoneNumber.Contains(term)
                || p.FullName.Contains(term))
            .OrderBy(p => p.FullName)
            .Take(10)
            .ToListAsync(ct);

        var list = patients.Select(p => new PatientSearchResultDto(
            p.Id,
            p.FullName,
            p.DateOfBirth.ToString("yyyy-MM-dd"),
            p.Gender?.ToString() ?? "Male",
            p.PhoneNumber,
            p.NationalId,
            p.Email,
            p.Address,
            p.InsuranceCards.OrderByDescending(c => c.ValidTo).FirstOrDefault()?.CardNumber,
            p.DateOfBirth > DateOnly.FromDateTime(DateTime.Today.AddYears(-15))
        )).ToList();

        return Result.Success<IReadOnlyList<PatientSearchResultDto>>(list);
    }

    /// <summary>
    /// Lấy danh sách bác sĩ trực ca hôm nay thuộc chuyên khoa kèm số lượng ca đang chờ và trạng thái đầy slot.
    /// </summary>
    public async Task<Result<IReadOnlyList<DepartmentDoctorScheduleDto>>> GetDepartmentSchedulesWithLoadAsync(
        int departmentId,
        DateOnly? date = null,
        CancellationToken ct = default)
    {
        var queryDate = date ?? DateOnly.FromDateTime(Now);
        var start = queryDate.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);

        var schedules = await _context.DoctorSchedules
            .AsNoTracking()
            .Include(s => s.Doctor)
            .Include(s => s.Room)
            .Where(s => s.Doctor.DepartmentId == departmentId
                && s.Doctor.Account.IsActive
                && s.IsActive
                && s.DayOfWeek == queryDate.DayOfWeek
                && s.EffectiveFrom <= queryDate
                && (s.EffectiveTo == null || s.EffectiveTo >= queryDate))
            .OrderBy(s => s.StartTime)
            .ThenBy(s => s.Doctor.FullName)
            .ToListAsync(ct);

        if (schedules.Count == 0)
        {
            return Result.Success<IReadOnlyList<DepartmentDoctorScheduleDto>>([]);
        }

        var doctorIds = schedules.Select(s => s.DoctorId).Distinct().ToList();

        // Đếm số lượng bệnh nhân đang chờ khám (StatusId = 3 CheckedIn hoặc 4 ReadyForDoctor)
        var waitingCounts = await _context.Appointments
            .AsNoTracking()
            .Where(a => a.DoctorId.HasValue && doctorIds.Contains(a.DoctorId.Value)
                && (a.StatusId == 3 || a.StatusId == 4)
                && ((a.Slot != null && a.Slot.StartTime >= start && a.Slot.StartTime < end)
                    || (a.Slot == null && a.CreatedAt >= start && a.CreatedAt < end)))
            .GroupBy(a => a.DoctorId!.Value)
            .Select(g => new { DoctorId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DoctorId, x => x.Count, ct);

        // Kiểm tra slot và lượt đặt theo từng ca khám cụ thể (Ca sáng vs Ca chiều)
        var roomIds = schedules.Select(s => s.RoomId).Distinct().ToList();
        var allSlots = await _context.AppointmentSlots
            .AsNoTracking()
            .Where(sl => roomIds.Contains(sl.RoomId) && sl.StartTime >= start && sl.StartTime < end)
            .ToListAsync(ct);

        var slotsByRoomShift = allSlots
            .GroupBy(sl => (sl.RoomId, IsAfternoon: sl.StartTime.Hour >= 12))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Capacity));

        var allAppointments = await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Slot)
            .Where(a => a.RoomId.HasValue && roomIds.Contains(a.RoomId.Value)
                && (a.StatusId == 1 || a.StatusId == 2 || a.StatusId == 3 || a.StatusId == 4 || a.StatusId == 5)
                && ((a.Slot != null && a.Slot.StartTime >= start && a.Slot.StartTime < end)
                    || (a.Slot == null && a.CreatedAt >= start && a.CreatedAt < end)))
            .ToListAsync(ct);

        var bookedCountsByRoomShift = allAppointments
            .GroupBy(a => (
                RoomId: a.RoomId!.Value,
                IsAfternoon: (a.Slot != null ? a.Slot.StartTime.Hour : a.CreatedAt.Hour) >= 12
            ))
            .ToDictionary(g => g.Key, g => g.Count());

        var currentTime = TimeOnly.FromDateTime(Now);
        var todayDate = DateOnly.FromDateTime(Now);
        bool isToday = queryDate == todayDate;

        var resultList = new List<DepartmentDoctorScheduleDto>();
        foreach (var s in schedules)
        {
            int waiting = waitingCounts.GetValueOrDefault(s.DoctorId, 0);
            bool isAfternoon = s.Shift == ExamShift.Afternoon;
            int roomCap = slotsByRoomShift.GetValueOrDefault((s.RoomId, isAfternoon), 15);
            int roomBooked = bookedCountsByRoomShift.GetValueOrDefault((s.RoomId, isAfternoon), 0);
            bool isFull = roomCap > 0 && roomBooked >= roomCap;

            string shiftName = s.Shift == ExamShift.Morning ? "Ca sáng" : "Ca chiều";
            string shiftTime = $"{s.StartTime:HH:mm} – {s.EndTime:HH:mm}";
            bool isShiftEnded = isToday && (currentTime > s.EndTime);
            bool isCurrentShift = isToday && (currentTime >= s.StartTime && currentTime <= s.EndTime);

            resultList.Add(new DepartmentDoctorScheduleDto(
                s.Id,
                s.DoctorId,
                s.Doctor.FullName,
                s.Doctor.Specialty ?? "Chuyên khoa",
                s.Room.RoomNumber,
                waiting,
                isFull,
                $"{s.Doctor.FullName} · Phòng {s.Room.RoomNumber} · {shiftName} ({shiftTime})",
                shiftName,
                shiftTime,
                isCurrentShift,
                isShiftEnded
            ));
        }

        return Result.Success<IReadOnlyList<DepartmentDoctorScheduleDto>>(resultList);
    }

    /// <summary>
    /// Lấy thông tin phiếu khám vừa cấp phát cho lượt vãng lai để in hoặc hiển thị modal.
    /// </summary>
    public async Task<Result<WalkInTicketDto?>> GetWalkInTicketAsync(string? queueNumber = null, int? appointmentId = null, CancellationToken ct = default)
    {
        var todayDate = DateOnly.FromDateTime(Now);
        var start = todayDate.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);

        var query = _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Department)
            .Include(a => a.Room)
            .Include(a => a.Slot)
            .Where(a => (a.Slot != null && a.Slot.StartTime >= start && a.Slot.StartTime < end)
                || (a.Slot == null && a.CreatedAt >= start && a.CreatedAt < end));

        if (appointmentId.HasValue && appointmentId.Value > 0)
        {
            query = query.Where(a => a.Id == appointmentId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(queueNumber))
        {
            query = query.Where(a => a.QueueNumber == queueNumber.Trim());
        }
        else
        {
            return Result.Success<WalkInTicketDto?>(null);
        }

        var app = await query.OrderByDescending(a => a.Id).FirstOrDefaultAsync(ct);
        if (app == null)
        {
            return Result.Success<WalkInTicketDto?>(null);
        }

        int waitingAhead = 0;
        if (app.RoomId.HasValue)
        {
            waitingAhead = await _context.Appointments
                .AsNoTracking()
                .CountAsync(a => a.RoomId == app.RoomId
                    && (a.StatusId == 3 || a.StatusId == 4)
                    && a.Id < app.Id
                    && ((a.Slot != null && a.Slot.StartTime >= start && a.Slot.StartTime < end)
                        || (a.Slot == null && a.CreatedAt >= start && a.CreatedAt < end)), ct);
        }

        var ticket = new WalkInTicketDto(
            app.Id,
            app.AppointmentCode,
            app.QueueNumber ?? "Chưa cấp",
            app.Patient.FullName,
            app.Department?.Name ?? "Khoa khám",
            app.Room?.RoomNumber ?? "Đang xếp phòng",
            app.Doctor?.FullName ?? "Bác sĩ phụ trách",
            waitingAhead,
            app.CreatedAt
        );

        return Result.Success<WalkInTicketDto?>(ticket);
    }
}