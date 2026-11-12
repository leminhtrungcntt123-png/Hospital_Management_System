using HIS.Domain;
using HIS.BLL.DTOs;

namespace HIS.Web.Areas.Staff.Models;

public record ConsultationQueueItemDto(
    int AppointmentId,
    string AppointmentCode,
    string PatientName,
    int Age,
    string Gender,
    string QueueNumber,
    int PriorityLevel,
    DateTime? CheckedInAt,
    string Reason,
    string? Allergy,
    int? Pulse,
    string? BloodPressure,
    decimal? Temperature,
    int? Spo2,
    decimal? Weight,
    string? DoctorName = null,
    string? RoomNumber = null
);

public record ConsultationLabItemDto(
    int LabOrderId,
    string PatientName,
    string PatientCode,
    int Age,
    string ServiceName,
    string Note,
    DateTime OrderTime,
    string Status,
    string? ResultData
);

public record ConsultationRecordItemDto(
    int RecordId,
    string PatientName,
    string PatientCode,
    int Age,
    string Gender,
    DateTime DateTime,
    string DoctorName,
    string DepartmentName,
    string Symptoms,
    string ClinicalExam,
    string DiagnosisCode,
    string Diagnosis,
    string Conclusion
);

public record DoctorScheduleItemDto(
    int Id,
    DateOnly WorkDate,
    ExamShift Shift,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string DoctorName,
    string? RoomNumber,
    string Specialty,
    int? DoctorId = null,
    DoctorScheduleStatus Status = DoctorScheduleStatus.Approved,
    bool IsHotShift = false,
    string? DoctorNote = null
);

public record DutyShiftItemDto(
    string StaffName,
    DateTime StartTime,
    DateTime EndTime,
    string Note,
    int AccountId = 0
);

public record DoctorSelectItemDto(
    int Id,
    string FullName,
    string Specialty,
    string? RoomNumber = null
);

public record ClinicalServiceOptionDto(int Id, string Name, decimal Price);
public record MedicationOptionDto(int Id, string Name, string? Strength, string Unit, decimal Price);

public record ScheduleExceptionItemDto(
    int Id,
    DateOnly Date,
    ExamShift? Shift,
    string Type,
    string Status,
    string Reason,
    string? ReplacementDoctorName = null,
    int? DoctorId = null,
    int? ReplacementDoctorId = null
);

public class ConsultationViewModel
{
    public string ActiveTab { get; set; } = "consultation";
    public int WeekOffset { get; set; } = 0;
    public DateOnly SelectedDate { get; set; }
    public ExamShift SelectedShift { get; set; }

    public bool IsDepartmentHead { get; set; }
    public int? SelectedDoctorId { get; set; }
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = "";
    public string CurrentDoctorName { get; set; } = "";
    public string CurrentDoctorRoom { get; set; } = "";

    public IReadOnlyList<ConsultationQueueItemDto> QueuePatients { get; set; } = [];
    public ConsultationQueueItemDto? ActivePatient { get; set; }

    public IReadOnlyList<ConsultationLabItemDto> LabOrders { get; set; } = [];
    public IReadOnlyList<ConsultationRecordItemDto> MedicalRecords { get; set; } = [];
    public IReadOnlyList<DoctorScheduleItemDto> DoctorSchedules { get; set; } = [];
    public IReadOnlyList<DutyShiftItemDto> DutyShifts { get; set; } = [];
    public IReadOnlyList<ScheduleExceptionItemDto> Exceptions { get; set; } = [];
    public IReadOnlyList<DoctorSelectItemDto> AvailableDoctors { get; set; } = [];
    public IReadOnlyList<ClinicalServiceOptionDto> AvailableServices { get; set; } = [];
    public IReadOnlyList<MedicationOptionDto> AvailableMedications { get; set; } = [];
    public SchedulingPeriodDto? ActivePeriod { get; set; }
    public DepartmentDemandGridDto? DemandGrid { get; set; }
    public int CurrentDoctorId { get; set; }
    public int MinShiftsPerWeek { get; set; } = 6;
    public int MaxShiftsPerWeek { get; set; } = 10;
}
