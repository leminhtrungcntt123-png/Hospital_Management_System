namespace HIS.Domain;

public class QueueCounter
{
    public DateOnly QueueDate { get; set; }
    public int RoomId { get; set; }
    public ExamShift Shift { get; set; }
    public BookingChannel Channel { get; set; }
    public int LastNumber { get; set; }

    public ClinicRoom Room { get; set; } = null!;
}
