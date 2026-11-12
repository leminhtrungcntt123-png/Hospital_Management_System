namespace HIS.Domain;

public class SchedulingPeriod
{
    public int Id { get; set; }
    public int? DepartmentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTime BiddingOpensAt { get; set; }
    public DateTime BiddingClosesAt { get; set; }
    public SchedulingPeriodStatus Status { get; set; } = SchedulingPeriodStatus.Open;
    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Department? Department { get; set; }
    public Account? CreatedByAccount { get; set; }

    public ICollection<DoctorSchedule> Schedules { get; set; } = [];
    public ICollection<DutyShift> DutyShifts { get; set; } = [];
}

