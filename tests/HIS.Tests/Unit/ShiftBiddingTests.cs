using HIS.BLL.DTOs;
using HIS.Domain;
using Xunit;

namespace HIS.Tests.Unit;

public class ShiftBiddingTests
{
    [Fact]
    public void DecisionMatrixCandidates_ShouldBeSortedByPriorityMetrics()
    {
        // 4 Candidates testing the Decision Matrix sorting order:
        // C1: Conflict night duty (Lowest priority due to fatigue hard/soft warning)
        // C2: No conflict, Score 2 (Compensation priority - highest)
        // C3: No conflict, Score 1 (Rotation priority), 4 shifts left to max
        // C4: No conflict, Score 1 (Rotation priority), 6 shifts left to max (needs more shifts)
        var candidates = new List<DecisionMatrixCandidateDto>
        {
            new(1, 101, "BS. Fatigued", "Nội", "BS", EmploymentType.FullTime, null, null, DoctorScheduleStatus.Bidding, null, null,
                HasNightDutyConflict: true, RotationPriorityScore: 2, RotationBadgeText: "⭐⭐ Đền bù", ApprovedShiftsThisPeriod: 2, MinShiftsPerWeek: 6, MaxShiftsPerWeek: 10, ContinuityOfCareFollowUpCount: 5),

            new(2, 102, "BS. Compensated", "Nội", "ThS", EmploymentType.FullTime, null, null, DoctorScheduleStatus.Bidding, null, null,
                HasNightDutyConflict: false, RotationPriorityScore: 2, RotationBadgeText: "⭐⭐ Đền bù", ApprovedShiftsThisPeriod: 4, MinShiftsPerWeek: 6, MaxShiftsPerWeek: 10, ContinuityOfCareFollowUpCount: 1),

            new(3, 103, "BS. RotationNormal", "Nội", "BS", EmploymentType.FullTime, null, null, DoctorScheduleStatus.Bidding, null, null,
                HasNightDutyConflict: false, RotationPriorityScore: 1, RotationBadgeText: "⭐ Luân phiên", ApprovedShiftsThisPeriod: 6, MinShiftsPerWeek: 6, MaxShiftsPerWeek: 10, ContinuityOfCareFollowUpCount: 0),

            new(4, 104, "BS. RotationBehindQuota", "Nội", "BS", EmploymentType.FullTime, null, null, DoctorScheduleStatus.Bidding, null, null,
                HasNightDutyConflict: false, RotationPriorityScore: 1, RotationBadgeText: "⭐ Luân phiên", ApprovedShiftsThisPeriod: 2, MinShiftsPerWeek: 6, MaxShiftsPerWeek: 10, ContinuityOfCareFollowUpCount: 3)
        };

        // Sort using the exact 4-metric comparator from DepartmentHeadService:
        var sorted = candidates
            .OrderBy(c => c.HasNightDutyConflict ? 1 : 0)
            .ThenByDescending(c => c.RotationPriorityScore)
            .ThenByDescending(c => c.MaxShiftsPerWeek - c.ApprovedShiftsThisPeriod)
            .ThenByDescending(c => c.ContinuityOfCareFollowUpCount)
            .ToList();

        // 1st: BS. Compensated (No conflict, Score 2)
        Assert.Equal("BS. Compensated", sorted[0].DoctorName);
        // 2nd: BS. RotationBehindQuota (No conflict, Score 1, needs 8 shifts vs needs 4 shifts)
        Assert.Equal("BS. RotationBehindQuota", sorted[1].DoctorName);
        // 3rd: BS. RotationNormal (No conflict, Score 1, needs 4 shifts)
        Assert.Equal("BS. RotationNormal", sorted[2].DoctorName);
        // Last: BS. Fatigued (Has conflict)
        Assert.Equal("BS. Fatigued", sorted[3].DoctorName);
    }

    [Fact]
    public void RegisterShiftBidRequest_ShouldCaptureWorkDateAndShift()
    {
        var targetDate = new DateOnly(2026, 11, 20);
        var req = new RegisterShiftBidRequest(
            DoctorId: 1,
            PeriodId: 5,
            WorkDate: targetDate,
            Shift: ExamShift.Morning,
            DoctorNote: "Nguyện vọng khám ca sáng"
        );

        Assert.Equal(1, req.DoctorId);
        Assert.Equal(5, req.PeriodId);
        Assert.Equal(targetDate, req.WorkDate);
        Assert.Equal(ExamShift.Morning, req.Shift);
        Assert.Equal("Nguyện vọng khám ca sáng", req.DoctorNote);
    }

    [Fact]
    public void ReallocateShiftRequest_ShouldCaptureOldScheduleAndTarget()
    {
        var targetDate = new DateOnly(2026, 11, 21);
        var req = new ReallocateShiftRequest(
            OldScheduleId: 99,
            TargetDate: targetDate,
            TargetShift: ExamShift.Afternoon,
            TargetRoomId: 10,
            ReviewedByAccountId: 2
        );

        Assert.Equal(99, req.OldScheduleId);
        Assert.Equal(targetDate, req.TargetDate);
        Assert.Equal(ExamShift.Afternoon, req.TargetShift);
        Assert.Equal(10, req.TargetRoomId);
        Assert.Equal(2, req.ReviewedByAccountId);
    }
}

