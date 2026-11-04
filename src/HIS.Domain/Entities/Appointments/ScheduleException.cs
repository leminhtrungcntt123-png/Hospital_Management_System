namespace HIS.Domain;

public class ScheduleException
{
    public int Id { get; set; }
    public int DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public DateOnly Date { get; set; }
    public ExamShift? Shift { get; set; }
    public ScheduleExceptionType Type { get; set; }
    public ScheduleExceptionStatus Status { get; set; } = ScheduleExceptionStatus.Pending;
    public int? ReplacementDoctorId { get; set; }
    public Doctor? ReplacementDoctor { get; set; }
    public string? Reason { get; set; }
    public int? ApprovedBy { get; set; }
    public Account? Approver { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
