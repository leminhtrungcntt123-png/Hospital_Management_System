namespace HIS.BLL.DTOs;

public record DoctorListItemDto(int Id, string FullName, string Specialty, string DepartmentName, string LicenseNumber);

public record QueuePatientDto(int AppointmentId, string AppointmentCode, string PatientName, string? QueueNumber, int PriorityLevel, DateTime? CheckedInAt);

public record CreateMedicalRecordRequest(
    int AppointmentId,
    int DoctorId,
    string? Symptoms,
    string? ClinicalExamination,
    string? PrimaryDiagnosisCode,
    string? PrimaryDiagnosisDesc,
    string? Conclusion
);

