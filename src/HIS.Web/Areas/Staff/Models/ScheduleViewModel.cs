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
    public IReadOnlyList<DepartmentPatientListItemDto> Patients { get; set; } = [];

    public IReadOnlyList<DoctorListItemDto> AvailableDoctors { get; set; } = [];
    public IReadOnlyList<StaffListItemDto> AvailableStaff { get; set; } = [];
    public IReadOnlyList<ClinicRoomDto> AvailableRooms { get; set; } = [];

    public int? CurrentHeadDoctorId { get; set; }
    public DoctorListItemDto? CurrentHeadDoctor { get; set; }

    public SchedulingPeriodDto? ActivePeriod { get; set; }
    public DepartmentDemandGridDto? DemandGrid { get; set; }

    // Quản lý Lịch khám & Khung giờ (Slot)
    public IReadOnlyList<DepartmentListItemDto> AllDepartments { get; set; } = [];
    public IReadOnlyList<AppointmentSlotItemDto> Slots { get; set; } = [];
    public DateOnly SlotFilterStartDate { get; set; }
    public DateOnly SlotFilterEndDate { get; set; }
    public int? SlotFilterDepartmentId { get; set; }
    public int? SlotFilterRoomId { get; set; }
    public int? SlotFilterDoctorId { get; set; }
    public bool SlotShiftMorning { get; set; } = true;
    public bool SlotShiftAfternoon { get; set; } = true;
}
