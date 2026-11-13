using HIS.Domain;

namespace HIS.BLL.DTOs;

public record SlotGenerationRequest(DateOnly StartDate, DateOnly EndDate, int? DepartmentId = null);

public record SlotGenerationResultDto(int CreatedCount, int ExistingCount, int ExceptionSkippedCount);

public record SlotSearchRequest(
    DateOnly FilterStartDate,
    DateOnly FilterEndDate,
    List<string>? Shifts = null,
    int? DepartmentId = null,
    int? RoomId = null,
    int? DoctorId = null
);

public record AppointmentSlotItemDto(
    int Id,
    string DoctorName,
    string AcademicTitle,
    string DepartmentName,
    string RoomName,
    string RoomNumber,
    DateOnly WorkDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int Capacity,
    int BookedCount,
    int RemainingCount,
    string Status
);

