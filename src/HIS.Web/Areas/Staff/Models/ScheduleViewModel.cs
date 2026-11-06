using HIS.BLL.DTOs;

namespace HIS.Web.Areas.Staff.Models;

public class ScheduleViewModel
{
    public string ActiveTab { get; set; } = "schedule";
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = "";
    public int WeekOffset { get; set; } = 0;
    public DateOnly Today { get; set; }
    public DateOnly WeekStart { get; set; }
    public IReadOnlyList<DateOnly> WeekDates { get; set; } = [];

    public DepartmentOverviewDto? Overview { get; set; }
    public IReadOnlyList<DepartmentExceptionItemDto> Exceptions { get; set; } = [];
    public IReadOnlyList<DepartmentScheduleItemDto> Schedules { get; set; } = [];
    public IReadOnlyList<DepartmentDutyShiftDto> DutyShifts { get; set; } = [];
    public IReadOnlyList<DepartmentMedicalRecordDto> MedicalRecords { get; set; } = [];

    public IReadOnlyList<DoctorListItemDto> AvailableDoctors { get; set; } = [];
    public IReadOnlyList<StaffListItemDto> AvailableStaff { get; set; } = [];
    public IReadOnlyList<ClinicRoomDto> AvailableRooms { get; set; } = [];
}

