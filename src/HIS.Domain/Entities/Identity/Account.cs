namespace HIS.Domain;

public class Account
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; } = 0;
    public DateTime? LockedUntil { get; set; }

    // Không còn dùng: nghiệp vụ mới bỏ hẳn cơ chế chặn đặt lịch vì vắng mặt (đến trễ thì bỏ lượt, bắt số lại).
    // Hai cột vẫn tồn tại trong DB vì giữ entity tránh phải ALTER; không có code nào đọc/ghi chúng.
    public DateTime? BookingBlockedUntil { get; set; }
    public DateTime? NoShowResetAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public Doctor? Doctor { get; set; }
    public Nurse? Nurse { get; set; }
    public DepartmentHead? DepartmentHead { get; set; }
    public ICollection<AccountPatientProfile> PatientLinks { get; set; } = [];
    public ICollection<Appointment> BookedAppointments { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
    public ICollection<DutyShift> DutyShifts { get; set; } = [];
    public ICollection<DutyShift> AssignedDutyShifts { get; set; } = [];
    public ICollection<ScheduleException> ApprovedScheduleExceptions { get; set; } = [];
    public ICollection<AppointmentStatusHistory> PerformedAppointmentHistories { get; set; } = [];
    public ICollection<LabResult> PerformedLabResults { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
    public ICollection<AccessLog> AccessLogs { get; set; } = [];
}
