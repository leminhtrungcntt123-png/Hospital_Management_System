namespace HIS.Domain;

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<ClinicRoom> Rooms { get; set; } = [];
    public ICollection<DutyShift> DutyShifts { get; set; } = [];
    public ICollection<Doctor> Doctors { get; set; } = [];
    public ICollection<Nurse> Nurses { get; set; } = [];
    public DepartmentHead? DepartmentHead { get; set; }
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<DoctorSchedule> Schedules { get; set; } = [];
    public ICollection<SchedulingPeriod> SchedulingPeriods { get; set; } = [];
}
