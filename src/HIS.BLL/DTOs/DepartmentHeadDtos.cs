using HIS.Domain;

namespace HIS.BLL.DTOs;

public record DepartmentExceptionItemDto(
    int Id,
    int DoctorId,
    string DoctorName,
    DateOnly Date,
    ExamShift? Shift,
    ScheduleExceptionType Type,
    ScheduleExceptionStatus Status,
    int? ReplacementDoctorId,
    string? ReplacementDoctorName,
    string Reason,
    DateTime CreatedAt,
    string? ApprovedByName,
    DateTime? ApprovedAt,
    int AffectedAppointmentsCount = 0
);

public record ApproveExceptionRequest(
    int ExceptionId,
    int DepartmentHeadAccountId,
    int? DepartmentId = null
);

public record RejectExceptionRequest(
    int ExceptionId,
    int DepartmentHeadAccountId,
    string? RejectReason,
    int? DepartmentId = null
);

public record DepartmentScheduleItemDto(
    int Id,
    int? PeriodId,
    int DoctorId,
    string DoctorName,
    int? RoomId,
    string? RoomNumber,
    DateOnly WorkDate,
    ExamShift Shift,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int SlotDurationMinutes,
    int MaxPatients,
    int OnlineSlots,
    bool IsHotShift,
    DoctorScheduleStatus Status,
    string? DoctorNote = null,
    int? ReallocatedFromId = null,
    string? ReviewedByName = null,
    DateTime? ReviewedAt = null,
    int BookedAppointmentsCount = 0,
    int ReceivedPatientsCount = 0
);

public record ScheduleCancelImpactDto(
    bool CanCancel,
    int AffectedAppointmentsCount,
    int OngoingAppointmentsCount,
    string? Message = null
);

public record UpsertDoctorScheduleRequest(
    int? ScheduleId,
    int? PeriodId,
    int DoctorId,
    int DepartmentId,
    int? RoomId,
    DateOnly WorkDate,
    ExamShift Shift,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int SlotDurationMinutes,
    int MaxPatients,
    int OnlineSlots,
    DoctorScheduleStatus Status = DoctorScheduleStatus.Approved,
    string? DoctorNote = null
);

public record DepartmentDutyShiftDto(
    int Id,
    int AccountId,
    string StaffName,
    string RoleName,
    DateTime StartTime,
    DateTime EndTime,
    string? Note,
    string? AssignedByName
);

public record AssignDutyShiftRequest(
    int AccountId,
    int DepartmentId,
    DateTime StartTime,
    DateTime EndTime,
    string? Note,
    int AssignedByAccountId
);

public record DepartmentQueueItemDto(
    int Id,
    string AppointmentCode,
    string PatientName,
    int Age,
    string Gender,
    string QueueNumber,
    int StatusId,
    string StatusText,
    string RoomNumber,
    string DoctorName,
    DateTime AppointmentTime,
    string Reason
);

public record DepartmentOverviewDto(
    int DepartmentId,
    string DepartmentName,
    int TotalDoctors,
    int TotalRooms,
    int PendingExceptionsCount,
    int TodayAppointmentsCount,
    int CompletedAppointmentsCount,
    int InProgressAppointmentsCount,
    int WaitingReceptionCount = 0,
    int NeedVitalSignsCount = 0,
    int UnpaidInvoicesCount = 0,
    decimal UnpaidAmount = 0,
    IReadOnlyList<DepartmentQueueItemDto>? QueueItems = null
);

public record DepartmentMedicalRecordDto(
    int RecordId,
    int AppointmentId,
    string AppointmentCode,
    string PatientName,
    int Age,
    string Gender,
    string DoctorName,
    DateTime CreatedAt,
    string DiagnosisCode,
    string DiagnosisDesc,
    string Conclusion,
    string? ClinicalExamination = null,
    string? Symptoms = null
);

public record DepartmentPatientListItemDto(
    int Id,
    string PatientCode,
    string FullName,
    string Gender,
    DateOnly DateOfBirth,
    int Age,
    string PhoneNumber,
    string? Address
);

