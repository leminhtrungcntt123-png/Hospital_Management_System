namespace HIS.BLL.DTOs;

public sealed record NurseDashboardSummaryDto(
    string DepartmentName,
    DateOnly Date,
    int TotalToday,
    int WaitingProcess,
    int WaitingVitalsCount,
    int ReadyForDoctorCount,
    int InProgressCount,
    int CompletedCount,
    IReadOnlyList<VitalSignsQueueItemDto> TodayTasks);

public sealed record NursePatientListItemDto(
    int PatientId,
    string FullName,
    DateOnly DateOfBirth,
    string Gender,
    string PhoneNumber,
    string? NationalId,
    int TotalVisits,
    DateTime? LastVisitDate,
    int? LastVisitStatusId);
