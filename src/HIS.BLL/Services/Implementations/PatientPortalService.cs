using System.Data;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HIS.BLL.Services;

public sealed class PatientPortalService(
    AppDbContext db,
    TimeProvider clock,
    IOptions<BookingOptions> options) : IPatientPortalService
{
    private DateTime Now => DateTime.SpecifyKind(clock.GetLocalNow().DateTime, DateTimeKind.Unspecified);
    private static DateTime AsWallTime(DateTime dt) => DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
    private static DateTime? AsWallTime(DateTime? dt) => dt.HasValue ? DateTime.SpecifyKind(dt.Value, DateTimeKind.Unspecified) : null;

    public async Task<Result<IReadOnlyList<PatientAppointmentItemDto>>> GetAppointmentsAsync(int accountId, CancellationToken ct = default)
    {
        if (accountId <= 0)
        {
            return Result.Failure<IReadOnlyList<PatientAppointmentItemDto>>("UNAUTHORIZED", "Vui lòng đăng nhập.");
        }

        var linkedPatientIds = await db.AccountPatientProfiles
            .AsNoTracking()
            .Where(l => l.AccountId == accountId && l.Status == LinkStatus.Active)
            .Select(l => l.PatientId)
            .ToListAsync(ct);

        var rows = await db.Appointments
            .AsNoTracking()
            .Where(a => a.BookedBy == accountId || linkedPatientIds.Contains(a.PatientId))
            .OrderByDescending(a => a.Slot != null ? a.Slot.StartTime : a.CreatedAt)
            .Take(100)
            .Select(a => new
            {
                a.Id,
                a.AppointmentCode,
                PatientName = a.Patient.FullName,
                PatientPhone = a.Patient.PhoneNumber,
                PatientDateOfBirth = a.Patient.DateOfBirth,
                DoctorName = a.Doctor != null ? a.Doctor.FullName : "Chưa phân công",
                DepartmentName = a.Department != null ? a.Department.Name : "—",
                RoomNumber = a.Room != null ? a.Room.RoomNumber : "—",
                StartTime = a.Slot != null ? (DateTime?)a.Slot.StartTime : null,
                EndTime = a.Slot != null ? (DateTime?)a.Slot.EndTime : null,
                a.StatusId,
                a.Reason,
                a.ContactEmail,
                a.QueueNumber,
                a.CreatedAt
            })
            .ToListAsync(ct);

        var cutoff = Now.AddMinutes(options.Value.CancelCutoffMinutes);
        var items = rows.Select(a =>
        {
            var startTime = AsWallTime(a.StartTime);
            var endTime = AsWallTime(a.EndTime);
            var canCancel = a.StatusId is (int)AppointmentStatusId.Pending or (int)AppointmentStatusId.Confirmed or (int)AppointmentStatusId.RescheduleRequired
                && (a.StatusId == (int)AppointmentStatusId.RescheduleRequired || !startTime.HasValue || startTime.Value > cutoff);

            return new PatientAppointmentItemDto(
                a.Id,
                a.AppointmentCode,
                a.PatientName,
                a.DoctorName,
                a.DepartmentName,
                a.RoomNumber,
                startTime,
                endTime,
                a.StatusId,
                StatusLabel(a.StatusId),
                a.Reason,
                canCancel,
                AsWallTime(a.CreatedAt),
                a.PatientPhone,
                a.PatientDateOfBirth,
                a.ContactEmail,
                a.QueueNumber);
        }).ToList();

        return Result.Success<IReadOnlyList<PatientAppointmentItemDto>>(items);
    }

    public async Task<Result> CancelAppointmentAsync(int accountId, int appointmentId, CancellationToken ct = default)
    {
        if (accountId <= 0 || appointmentId <= 0)
        {
            return Result.Failure("INVALID_INPUT", "Thông tin lịch hẹn không hợp lệ.");
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            var appointment = await db.Appointments
                .Include(a => a.Slot)
                .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);

            if (appointment == null)
            {
                return Result.Failure("NOT_FOUND", "Không tìm thấy lịch hẹn.");
            }

            var hasAccess = appointment.BookedBy == accountId || await db.AccountPatientProfiles
                .AnyAsync(l => l.AccountId == accountId && l.PatientId == appointment.PatientId && l.Status == LinkStatus.Active, ct);

            if (!hasAccess)
            {
                return Result.Failure("FORBIDDEN", "Bạn không có quyền thao tác trên lịch hẹn này.");
            }

            var currentStatus = (AppointmentStatusId)appointment.StatusId;
            if (!AppointmentStateMachine.CanTransition(currentStatus, AppointmentStatusId.Cancelled))
            {
                return Result.Failure("INVALID_STATUS", "Trạng thái hiện tại của lịch hẹn không thể hủy.");
            }

            if (currentStatus != AppointmentStatusId.RescheduleRequired
                && appointment.Slot != null
                && appointment.Slot.StartTime <= Now.AddMinutes(options.Value.CancelCutoffMinutes))
            {
                return Result.Failure("CANCEL_CUTOFF",
                    $"Chỉ có thể hủy lịch trước giờ khám tối thiểu {options.Value.CancelCutoffMinutes} phút.");
            }

            var oldStatusId = appointment.StatusId;
            appointment.StatusId = (int)AppointmentStatusId.Cancelled;
            appointment.HoldUntil = null;
            appointment.UpdatedAt = Now;

            db.AppointmentStatusHistories.Add(new AppointmentStatusHistory
            {
                AppointmentId = appointment.Id,
                OldStatusId = oldStatusId,
                NewStatusId = (int)AppointmentStatusId.Cancelled,
                PerformedBy = accountId,
                ChangedAt = Now
            });

            if (appointment.SlotId.HasValue)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE appointment_slots
                    SET booked_count = GREATEST(0, booked_count - 1),
                        status = CASE
                            WHEN status = 'FULL' AND GREATEST(0, booked_count - 1) < capacity THEN 'AVAILABLE'
                            ELSE status
                        END,
                        version = version + 1
                    WHERE id = {appointment.SlotId.Value}
                    """, ct);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result.Success();
        });
    }

    public async Task<Result<PatientMedicalRecordsResultDto>> GetMedicalRecordsAsync(
        int accountId,
        int? patientId = null,
        CancellationToken ct = default)
    {
        if (accountId <= 0)
        {
            return Result.Failure<PatientMedicalRecordsResultDto>("UNAUTHORIZED", "Vui lòng đăng nhập.");
        }

        var links = await db.AccountPatientProfiles
            .AsNoTracking()
            .Where(l => l.AccountId == accountId && l.Status == LinkStatus.Active && l.CanViewRecords)
            .OrderByDescending(l => l.Relationship == PatientRelationship.Self)
            .ThenBy(l => l.PatientId)
            .Select(l => new
            {
                l.PatientId,
                FullName = l.Patient.FullName,
                l.Relationship
            })
            .ToListAsync(ct);

        var profiles = links
            .Select(l => new PatientRecordProfileOptionDto(
                l.PatientId,
                l.FullName,
                RelationshipLabel(l.Relationship)))
            .ToList();

        var allowedPatientIds = links.Select(l => l.PatientId).Distinct().ToList();
        if (patientId.HasValue && patientId.Value > 0)
        {
            if (!allowedPatientIds.Contains(patientId.Value))
            {
                return Result.Failure<PatientMedicalRecordsResultDto>("FORBIDDEN", "Bạn không có quyền xem hồ sơ sức khỏe của người khám này.");
            }
            allowedPatientIds = [patientId.Value];
        }

        if (allowedPatientIds.Count == 0)
        {
            return Result.Success(new PatientMedicalRecordsResultDto(profiles, []));
        }

        var records = await db.MedicalRecords
            .AsNoTracking()
            .Where(r => allowedPatientIds.Contains(r.Appointment.PatientId))
            .OrderByDescending(r => r.CreatedAt)
            .Take(100)
            .Select(r => new
            {
                r.Id,
                r.AppointmentId,
                AppointmentCode = r.Appointment.AppointmentCode,
                ExamDate = r.Appointment.Slot != null ? r.Appointment.Slot.StartTime : r.CreatedAt,
                PatientId = r.Appointment.PatientId,
                PatientName = r.Appointment.Patient.FullName,
                DepartmentName = r.Appointment.Department != null ? r.Appointment.Department.Name : "—",
                DoctorName = r.Doctor.FullName,
                RoomNumber = r.Appointment.Room != null ? r.Appointment.Room.RoomNumber : "—",
                r.Symptoms,
                r.ClinicalExamination,
                r.PrimaryDiagnosisCode,
                r.PrimaryDiagnosisDesc,
                r.SecondaryDiagnosis,
                r.Conclusion,
                r.FollowUpDate,
                VitalSigns = r.Appointment.VitalSigns == null
                    ? null
                    : new
                    {
                        r.Appointment.VitalSigns.Pulse,
                        r.Appointment.VitalSigns.SystolicBp,
                        r.Appointment.VitalSigns.DiastolicBp,
                        r.Appointment.VitalSigns.Temperature,
                        r.Appointment.VitalSigns.RespiratoryRate,
                        r.Appointment.VitalSigns.Spo2,
                        r.Appointment.VitalSigns.Weight,
                        r.Appointment.VitalSigns.Height
                    },
                Prescriptions = r.Prescriptions
                    .OrderByDescending(p => p.PrescribedDate)
                    .Select(p => new
                    {
                        p.Id,
                        p.PrescribedDate,
                        p.Note,
                        Items = p.PrescriptionItems
                            .OrderBy(i => i.Id)
                            .Select(i => new
                            {
                                MedicationName = i.Medication.Name,
                                i.Medication.ActiveIngredient,
                                i.Medication.Strength,
                                i.Medication.Unit,
                                i.Dosage,
                                i.Quantity,
                                i.DurationDays,
                                i.UsageInstructions
                            })
                            .ToList()
                    })
                    .ToList(),
                LabOrders = r.LabOrders
                    .OrderBy(o => o.CreatedAt)
                    .Select(o => new
                    {
                        ServiceName = o.Service.Name,
                        o.Note,
                        o.Status,
                        Result = o.LabResult != null ? o.LabResult.Result : null,
                        RecordedAt = o.LabResult != null ? (DateTime?)o.LabResult.RecordedAt : null
                    })
                    .ToList()
            })
            .ToListAsync(ct);

        var items = records.Select(r => new PatientMedicalRecordItemDto(
            r.Id,
            r.AppointmentId,
            r.AppointmentCode,
            AsWallTime(r.ExamDate),
            r.PatientId,
            r.PatientName,
            r.DepartmentName,
            r.DoctorName,
            r.RoomNumber,
            r.Symptoms,
            r.ClinicalExamination,
            r.PrimaryDiagnosisCode,
            r.PrimaryDiagnosisDesc,
            r.SecondaryDiagnosis,
            r.Conclusion,
            r.FollowUpDate,
            r.VitalSigns == null
                ? null
                : new PatientVitalSignsDto(
                    r.VitalSigns.Pulse,
                    r.VitalSigns.SystolicBp,
                    r.VitalSigns.DiastolicBp,
                    r.VitalSigns.Temperature,
                    r.VitalSigns.RespiratoryRate,
                    r.VitalSigns.Spo2,
                    r.VitalSigns.Weight,
                    r.VitalSigns.Height),
            r.Prescriptions
                .Select(p => new PatientPrescriptionDto(
                    p.Id,
                    p.PrescribedDate,
                    p.Note,
                    p.Items
                        .Select(i => new PatientPrescriptionItemDto(
                            i.MedicationName,
                            i.ActiveIngredient,
                            i.Strength,
                            i.Unit,
                            i.Dosage,
                            i.Quantity,
                            i.DurationDays,
                            i.UsageInstructions))
                        .ToList()))
                .ToList(),
            r.LabOrders
                .Select(o => new PatientLabOrderDto(
                    o.ServiceName,
                    o.Note,
                    LabOrderStatusLabel(o.Status),
                    o.Result,
                    AsWallTime(o.RecordedAt)))
                .ToList()))
            .ToList();

        return Result.Success(new PatientMedicalRecordsResultDto(profiles, items));
    }

    public async Task<Result<PatientNotificationsDto>> GetNotificationsAsync(int accountId, CancellationToken ct = default)
    {
        if (accountId <= 0)
        {
            return Result.Failure<PatientNotificationsDto>("UNAUTHORIZED", "Vui lòng đăng nhập.");
        }

        var rawItems = await db.Notifications
            .AsNoTracking()
            .Where(n => n.AccountId == accountId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new { n.Id, n.AppointmentId, n.Title, n.Content, n.IsRead, n.CreatedAt })
            .ToListAsync(ct);

        var items = rawItems
            .Select(n => new PatientNotificationItemDto(n.Id, n.AppointmentId, n.Title, n.Content, n.IsRead, AsWallTime(n.CreatedAt)))
            .ToList();

        var unreadCount = await db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.AccountId == accountId && !n.IsRead, ct);

        return Result.Success(new PatientNotificationsDto(unreadCount, items));
    }

    public async Task<Result> MarkNotificationsReadAsync(int accountId, int? notificationId = null, CancellationToken ct = default)
    {
        if (accountId <= 0)
        {
            return Result.Failure("UNAUTHORIZED", "Vui lòng đăng nhập.");
        }

        var query = db.Notifications.Where(n => n.AccountId == accountId && !n.IsRead);
        if (notificationId.HasValue)
        {
            query = query.Where(n => n.Id == notificationId.Value);
        }

        await query.ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, true), ct);
        return Result.Success();
    }

    private static string StatusLabel(int statusId) => (AppointmentStatusId)statusId switch
    {
        AppointmentStatusId.Pending => "Chờ xác nhận",
        AppointmentStatusId.Confirmed => "Đã xác nhận",
        AppointmentStatusId.CheckedIn => "Đã tiếp nhận",
        AppointmentStatusId.ReadyForDoctor => "Chờ bác sĩ",
        AppointmentStatusId.InProgress => "Đang khám",
        AppointmentStatusId.Completed => "Đã hoàn thành",
        AppointmentStatusId.Cancelled => "Đã hủy",
        AppointmentStatusId.NoShow => "Không đến",
        AppointmentStatusId.Expired => "Hết hạn",
        AppointmentStatusId.RescheduleRequired => "Cần đặt lại lịch",
        _ => "Không xác định"
    };

    private static string RelationshipLabel(PatientRelationship rel) => rel switch
    {
        PatientRelationship.Self => "Bản thân",
        PatientRelationship.Spouse => "Vợ / Chồng",
        PatientRelationship.Child => "Con",
        PatientRelationship.Parent => "Bố / Mẹ",
        PatientRelationship.Sibling => "Anh / Chị / Em",
        _ => "Người thân"
    };

    private static string LabOrderStatusLabel(LabOrderStatus status) => status switch
    {
        LabOrderStatus.Pending => "Chờ thực hiện",
        LabOrderStatus.Completed => "Đã có kết quả",
        _ => status.ToString()
    };
}
