using System.ComponentModel.DataAnnotations;
using HIS.Domain;

namespace HIS.BLL.DTOs;

public sealed class WalkInReceptionRequest
{
    public int? PatientId { get; set; }
    [StringLength(100)] public string? FullName { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    [StringLength(20)] public string? NationalId { get; set; }
    [StringLength(15)] public string? PhoneNumber { get; set; }
    [StringLength(255)] public string? Address { get; set; }
    [Range(1, int.MaxValue)] public int ScheduleId { get; set; }
    [StringLength(255)] public string? Reason { get; set; }
    [Range(0, 3)] public int PriorityLevel { get; set; }
    public bool IdentityVerified { get; set; }
}

public record ReceptionVisit(int Id, string Code, string PatientName, DateOnly BirthDate,
    string Phone, string Doctor, string Room, DateTime ArrivalTime, BookingChannel Channel,
    int StatusId, string? QueueNumber, int PriorityLevel);
public record ReceptionSchedule(int Id, string Label);
public record ReceptionPatient(int Id, string Name, DateOnly BirthDate, string Phone);
public record ReceptionDesk(string Department, DateOnly Date, IReadOnlyList<ReceptionVisit> Visits,
    IReadOnlyList<ReceptionSchedule> Schedules, IReadOnlyList<ReceptionPatient> Patients);
