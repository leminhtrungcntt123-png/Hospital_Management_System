namespace HIS.Domain;

public class MedicalRecord
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public int DoctorId { get; set; }
    public string? Symptoms { get; set; }
    public string? ClinicalExamination { get; set; }
    public string? PrimaryDiagnosisCode { get; set; }
    public string? PrimaryDiagnosisDesc { get; set; }
    public string? SecondaryDiagnosis { get; set; }
    public string? Conclusion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Appointment Appointment { get; set; } = null!;
    public Doctor Doctor { get; set; } = null!;
    public ICollection<LabOrder> LabOrders { get; set; } = [];
    public ICollection<Prescription> Prescriptions { get; set; } = [];
    public ICollection<AccessLog> AccessLogs { get; set; } = [];
}
