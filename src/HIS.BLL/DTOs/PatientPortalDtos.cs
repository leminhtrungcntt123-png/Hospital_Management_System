namespace HIS.BLL.DTOs;

public sealed record PatientAppointmentItemDto(
    int Id,
    string AppointmentCode,
    string PatientName,
    string DoctorName,
    string DepartmentName,
    string RoomNumber,
    DateTime? StartTime,
    DateTime? EndTime,
    int StatusId,
    string StatusName,
    string? Reason,
    bool CanCancel,
    DateTime CreatedAt,
    string? PatientPhone = null,
    DateOnly? PatientDateOfBirth = null,
    string? ContactEmail = null,
    string? QueueNumber = null);

public sealed record PatientVitalSignsDto(
    int? Pulse,
    int? SystolicBp,
    int? DiastolicBp,
    decimal? Temperature,
    int? RespiratoryRate,
    int? Spo2,
    decimal? Weight,
    decimal? Height);

public sealed record PatientPrescriptionItemDto(
    string MedicationName,
    string? ActiveIngredient,
    string? Strength,
    string Unit,
    string? Dosage,
    int Quantity,
    int? DurationDays,
    string? UsageInstructions);

public sealed record PatientPrescriptionDto(
    int Id,
    DateOnly PrescribedDate,
    string? Note,
    IReadOnlyList<PatientPrescriptionItemDto> Items);

public sealed record PatientLabOrderDto(
    string ServiceName,
    string? Note,
    string Status,
    string? Result,
    DateTime? RecordedAt);

public sealed record PatientRecordProfileOptionDto(
    int PatientId,
    string FullName,
    string RelationshipLabel);

public sealed record PatientMedicalRecordItemDto(
    int Id,
    int AppointmentId,
    string AppointmentCode,
    DateTime ExamDate,
    int PatientId,
    string PatientName,
    string DepartmentName,
    string DoctorName,
    string RoomNumber,
    string? Symptoms,
    string? ClinicalExamination,
    string? PrimaryDiagnosisCode,
    string? PrimaryDiagnosisDesc,
    string? SecondaryDiagnosis,
    string? Conclusion,
    DateOnly? FollowUpDate,
    PatientVitalSignsDto? VitalSigns,
    IReadOnlyList<PatientPrescriptionDto> Prescriptions,
    IReadOnlyList<PatientLabOrderDto> LabOrders);

public sealed record PatientMedicalRecordsResultDto(
    IReadOnlyList<PatientRecordProfileOptionDto> Profiles,
    IReadOnlyList<PatientMedicalRecordItemDto> Items);

public sealed record PatientNotificationItemDto(
    int Id,
    int? AppointmentId,
    string Title,
    string? Content,
    bool IsRead,
    DateTime CreatedAt);

public sealed record PatientNotificationsDto(
    int UnreadCount,
    IReadOnlyList<PatientNotificationItemDto> Items);

public sealed record CancelAppointmentRequest(int AppointmentId);

public sealed record MarkNotificationReadRequest(int? NotificationId);
