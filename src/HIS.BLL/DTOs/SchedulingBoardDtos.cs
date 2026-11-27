namespace HIS.BLL.DTOs;

public record SchedulingBoardDto(
    DateOnly Today,
    DateOnly WeekStart,
    DateOnly WeekEnd,
    SchedulingBoardTotalsDto Totals,
    IReadOnlyList<SchedulingBoardDepartmentDto> Departments);

public record SchedulingBoardTotalsDto(
    int DepartmentsReady,
    int DepartmentsTotal,
    int DoctorsRegistered,
    int DoctorsTotal,
    int PendingShifts,
    int UnderstaffedShifts);

public record SchedulingBoardDepartmentDto(
    int DepartmentId,
    string Name,
    string? HeadName,
    int DoctorCount,
    int RegisteredDoctorCount,
    int ApprovedShifts,
    int PendingShifts,
    bool IsPublished,
    bool HasOpenPeriod,
    int UnderstaffedShifts);
