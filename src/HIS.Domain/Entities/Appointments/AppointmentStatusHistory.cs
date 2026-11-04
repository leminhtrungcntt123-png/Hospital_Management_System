namespace HIS.Domain;

public class AppointmentStatusHistory
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;
    public int? OldStatusId { get; set; }
    public AppointmentStatus? OldStatus { get; set; }
    public int NewStatusId { get; set; }
    public AppointmentStatus NewStatus { get; set; } = null!;
    public int? PerformedBy { get; set; }
    public Account? Performer { get; set; }
    public DateTime ChangedAt { get; set; }

    // Hủy sát giờ (trong FreeCancelHours trước giờ khám) do chính bệnh nhân thực hiện,
    // đếm theo performed_by trong tháng dương lịch để áp hạn mức hủy.
    public bool IsLateCancel { get; set; } = false;
}
