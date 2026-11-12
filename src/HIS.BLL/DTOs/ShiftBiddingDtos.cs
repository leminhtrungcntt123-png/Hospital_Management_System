using HIS.Domain;

namespace HIS.BLL.DTOs;

public enum DemandLevel
{
    Empty,          // ⚪ 0 / total_rooms
    Available,      // 🟢 1..total_rooms / total_rooms
    HighCompetition // 🔴 > total_rooms
}

public record SchedulingPeriodDto(
    int Id,
    int? DepartmentId,
    string? DepartmentName,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    DateTime BiddingOpensAt,
    DateTime BiddingClosesAt,
    SchedulingPeriodStatus Status,
    bool IsOpenForBidding
);

public record ShiftDemandCellDto(
    DateOnly WorkDate,
    ExamShift Shift,
    int CurrentBidders,
    int ApprovedCount,
    int TotalRooms,
    DemandLevel Level,
    bool IsHotShift,
    IReadOnlyList<DoctorBidSummaryDto> Bidders
);

public record DoctorBidSummaryDto(
    int ScheduleId,
    int DoctorId,
    string DoctorName,
    int? RoomId,
    string? RoomNumber,
    DoctorScheduleStatus Status,
    string? Note
);

public record DepartmentDemandGridDto(
    int DepartmentId,
    string DepartmentName,
    int TotalActiveRooms,
    SchedulingPeriodDto? Period,
    IReadOnlyList<ShiftDemandCellDto> Cells
);

public record RegisterShiftBidRequest(
    int DoctorId,
    int PeriodId,
    DateOnly WorkDate,
    ExamShift Shift,
    string? DoctorNote = null
);

public record CreateSchedulingPeriodRequest(
    int? DepartmentId,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    DateTime BiddingOpensAt,
    DateTime BiddingClosesAt,
    int CreatedByAccountId
);

public record DecisionMatrixCandidateDto(
    int ScheduleId,
    int DoctorId,
    string DoctorName,
    string? Specialty,
    string? AcademicTitle,
    EmploymentType EmploymentType,
    int? RoomId,
    string? RoomNumber,
    DoctorScheduleStatus Status,
    string? DoctorNote,
    int? ReallocatedFromId,

    // 4 chỉ số hỗ trợ quyết định (Decision Matrix)
    bool HasNightDutyConflict,
    int RotationPriorityScore,           // 2 = ⭐⭐ Đền bù, 1 = ⭐ Luân phiên, 0 = Đã duyệt ca hot
    string RotationBadgeText,
    int ApprovedShiftsThisPeriod,
    short MinShiftsPerWeek,
    short MaxShiftsPerWeek,
    int ContinuityOfCareFollowUpCount
);

public record DecisionMatrixResultDto(
    int DepartmentId,
    DateOnly WorkDate,
    ExamShift Shift,
    int TotalActiveRooms,
    bool IsHotShift,
    IReadOnlyList<DecisionMatrixCandidateDto> Candidates,
    IReadOnlyList<ClinicRoomDto> AvailableRooms
);

public record ApproveShiftRequest(
    int ScheduleId,
    int RoomId,
    int ReviewedByAccountId
);

public record ReallocateShiftRequest(
    int OldScheduleId,
    DateOnly TargetDate,
    ExamShift TargetShift,
    int TargetRoomId,
    int ReviewedByAccountId
);

public record DoctorPublicDto(
    int Id,
    string FullName,
    string? Specialty,
    string? AcademicTitle,
    string? AvatarUrl,
    string? ShortBio,
    string? Biography,
    short YearsOfExperience,
    int DisplayOrder,
    string DepartmentName
);

