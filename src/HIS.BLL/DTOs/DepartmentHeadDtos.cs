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
    DateTime? ApprovedAt
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
    int DoctorId,
    string DoctorName,
    int RoomId,
    string RoomNumber,
    DayOfWeek DayOfWeek,
    ExamShift Shift,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int SlotDurationMinutes,
    int MaxPatients,
    int OnlineSlots,
    bool IsActive
);

public record UpsertDoctorScheduleRequest(
    int? ScheduleId,
    int DoctorId,
    int RoomId,
    DayOfWeek DayOfWeek,
    ExamShift Shift,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int SlotDurationMinutes,
    int MaxPatients,
    int OnlineSlots,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo
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
    string Conclusion
);

