namespace HIS.BLL.DTOs;

public record DoctorListItemDto(int Id, string FullName, string Specialty, string DepartmentName, string LicenseNumber, int AccountId = 0);

public record QueuePatientDto(int AppointmentId, string AppointmentCode, string PatientName, string? QueueNumber, int PriorityLevel, DateTime? CheckedInAt);

public record CreateLabOrderItemDto
{
    public int ServiceId { get; set; }
    public string? Note { get; set; }
}

public record CreatePrescriptionItemDto
{
    public int MedicationId { get; set; }
    public int Quantity { get; set; }
    public string? Dosage { get; set; }
    public int? DurationDays { get; set; }
    public string? UsageInstructions { get; set; }
}

public record CreateMedicalRecordRequest
{
    public int AppointmentId { get; set; }
    public int DoctorId { get; set; }
    public string? Symptoms { get; set; }
    public string? ClinicalExamination { get; set; }
    public string? PrimaryDiagnosisCode { get; set; }
    public string? PrimaryDiagnosisDesc { get; set; }
    public string? Conclusion { get; set; }
    public string? Allergies { get; set; }
    public string? PrescriptionNote { get; set; }
    public string? IpAddress { get; set; }
    public List<CreateLabOrderItemDto> LabOrders { get; set; } = [];
    public List<CreatePrescriptionItemDto> PrescriptionItems { get; set; } = [];

    public CreateMedicalRecordRequest() { }

    public CreateMedicalRecordRequest(
        int appointmentId,
        int doctorId,
        string? symptoms,
        string? clinicalExamination,
        string? primaryDiagnosisCode,
        string? primaryDiagnosisDesc,
        string? conclusion,
        string? allergies = null,
        string? prescriptionNote = null,
        List<CreateLabOrderItemDto>? labOrders = null,
        List<CreatePrescriptionItemDto>? prescriptionItems = null,
        string? ipAddress = null)
    {
        AppointmentId = appointmentId;
        DoctorId = doctorId;
        Symptoms = symptoms;
        ClinicalExamination = clinicalExamination;
        PrimaryDiagnosisCode = primaryDiagnosisCode;
        PrimaryDiagnosisDesc = primaryDiagnosisDesc;
        Conclusion = conclusion;
        Allergies = allergies;
        PrescriptionNote = prescriptionNote;
        LabOrders = labOrders ?? [];
        PrescriptionItems = prescriptionItems ?? [];
        IpAddress = ipAddress;
    }
}
