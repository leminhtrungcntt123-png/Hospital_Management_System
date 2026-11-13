namespace HIS.Domain;

public class Account
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Role Role { get; set; } = null!;
    public Doctor? Doctor { get; set; }
    public Nurse? Nurse { get; set; }
    public DepartmentHead? DepartmentHead { get; set; }

    public ICollection<AccountPatientProfile> PatientLinks { get; set; } = [];
    public ICollection<AccountPatientProfile> CreatedPatientLinks { get; set; } = [];
    public ICollection<AccountPatientProfile> VerifiedPatientLinks { get; set; } = [];
    public ICollection<DutyShift> DutyShifts { get; set; } = [];
    public ICollection<DutyShift> AssignedDutyShifts { get; set; } = [];
    public ICollection<ScheduleException> ApprovedScheduleExceptions { get; set; } = [];
    public ICollection<Appointment> BookedAppointments { get; set; } = [];
    public ICollection<AppointmentStatusHistory> PerformedStatusHistories { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
    public ICollection<LabResult> PerformedLabResults { get; set; } = [];
    public ICollection<Payment> CashierPayments { get; set; } = [];
    public ICollection<AccessLog> AccessLogs { get; set; } = [];
    public ICollection<SchedulingPeriod> CreatedSchedulingPeriods { get; set; } = [];
    public ICollection<DoctorSchedule> ReviewedSchedules { get; set; } = [];
}
