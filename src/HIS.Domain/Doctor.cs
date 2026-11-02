namespace HIS.Domain;

public class Doctor
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public int DepartmentId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Specialty { get; set; }
    public string? LicenseNumber { get; set; }
    public int AvgConsultationMinutes { get; set; } = 15;

    public Account Account { get; set; } = null!;
    public Department Department { get; set; } = null!;

    public ICollection<DoctorSchedule> Schedules { get; set; } = [];
    public ICollection<ScheduleException> ScheduleExceptions { get; set; } = [];
    public ICollection<ScheduleException> ReplacementScheduleExceptions { get; set; } = [];
    public ICollection<AppointmentSlot> AppointmentSlots { get; set; } = [];
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<MedicalRecord> MedicalRecords { get; set; } = [];
}
