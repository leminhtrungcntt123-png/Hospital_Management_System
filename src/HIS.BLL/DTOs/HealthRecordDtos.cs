using HIS.Domain;

namespace HIS.BLL.DTOs;

public record HealthRecordServiceItemDto(
    string Name,
    decimal Price,
    string Category);

public record HealthRecordExamDto(
    string Symptoms,
    string ClinicalExam,
    string DiagnosisCode,
    string DiagnosisDesc,
    string Conclusion);

public record HealthRecordLabItemDto(
    string TestName,
    string ResultValue,
    string Unit,
    string ReferenceRange,
    string Status);

public record HealthRecordImagingItemDto(
    string ServiceName,
    string Description,
    string Conclusion,
    string? ImageUrl);

public record HealthRecordPrescriptionItemDto(
    string MedicineName,
    string ActiveIngredient,
    string Dosage,
    int Quantity,
    string Unit,
    string UsageInstructions);

public record HealthRecordHistoryItemDto(
    int AppointmentId,
    string AppointmentCode,
    DateTime ExaminationTime,
    string DoctorName,
    string DepartmentName,
    string PrimaryDiagnosisDesc,
    decimal PatientAmount,
    InvoiceStatus BillingStatus);

public record HealthRecordDetailDto(
    int AppointmentId,
    string AppointmentCode,
    int PatientId,
    string PatientCode,
    string PatientName,
    string Gender,
    int Age,
    DateOnly DateOfBirth,
    string? PhoneNumber,
    string? Address,
    string FacilityName,
    DateTime ExaminationTime,
    string DoctorName,
    string DepartmentName,
    InvoiceStatus BillingStatus,
    decimal TotalAmount,
    decimal InsuranceCoveredAmount,
    decimal PatientAmount,
    decimal CoveragePct,
    IReadOnlyList<HealthRecordServiceItemDto> Services,
    HealthRecordExamDto? Examination,
    IReadOnlyList<HealthRecordLabItemDto> LabResults,
    IReadOnlyList<HealthRecordImagingItemDto> ImagingResults,
    IReadOnlyList<HealthRecordPrescriptionItemDto> Prescriptions);

