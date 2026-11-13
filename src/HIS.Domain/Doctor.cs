namespace HIS.Domain;

public class Doctor
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public int DepartmentId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Specialty { get; set; }
    public string? LicenseNumber { get; set; }
    public int AvgConsultationMinutes { get; set; } = 30;

    // Homepage Showcase
    public string? AcademicTitle { get; set; }
    public string? AvatarUrl { get; set; }
    public string? ShortBio { get; set; }
    public string? Biography { get; set; }
    public short YearsOfExperience { get; set; } = 0;
    public bool IsPublic { get; set; } = true;
    public bool IsFeatured { get; set; } = false;
    public int DisplayOrder { get; set; } = 100;

    // Shift Bidding Quota
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;
    public short MinShiftsPerWeek { get; set; } = 2;
    public short MaxShiftsPerWeek { get; set; } = 10;

    public Account Account { get; set; } = null!;
    public Department Department { get; set; } = null!;

    public ICollection<DoctorSchedule> Schedules { get; set; } = [];
    public ICollection<ScheduleException> ScheduleExceptions { get; set; } = [];
    public ICollection<ScheduleException> ReplacementScheduleExceptions { get; set; } = [];
    public ICollection<AppointmentSlot> AppointmentSlots { get; set; } = [];
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<MedicalRecord> MedicalRecords { get; set; } = [];
}
