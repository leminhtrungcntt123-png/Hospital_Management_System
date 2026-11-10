namespace HIS.BLL.DTOs;

// --- DTOs Đếm & Thống kê ---
public sealed record ReceptionCountsDto(
    int Waiting,
    int Received,
    int Scheduled,
    int Cancelled);

// --- DTOs Hiển thị Danh sách & Tra cứu ---
public sealed record ReceptionItemDto(
    int AppointmentId,
    string AppointmentCode,
    int PatientId,
    string PatientCode,
    string PatientName,
    string Gender,
    DateOnly DateOfBirth,
    string InsuranceCardNumber,
    string DepartmentName,
    string DoctorName,
    string StatusCode,
    string StatusName,
    DateTime AppointmentTime,
    string BookingChannel,
    string VisitType,
    int PriorityLevel,
    string PriorityLabel,
    string QueueNumber,
    string Reason,
    // "Đặt bởi": tài khoản web của bệnh nhân hoặc tài khoản quầy đã mở ca.
    string BookedByLabel,
    // Bệnh nhân có liên kết hồ sơ đang chờ quầy xác nhận (account_patient_profiles.status = PENDING).
    bool HasPendingLink,
    // Hai khai báo bảo hiểm người bệnh tick khi đặt lịch: "BHYT", "Tư nhân", "BHYT + tư nhân" hoặc rỗng.
    string InsuranceDeclaration);

public sealed record ReceptionPatientDto(
    int PatientId,
    string PatientCode,
    string FullName,
    DateOnly DateOfBirth,
    string Gender,
    string PhoneNumber,
    string NationalId,
    string Address,
    string InsuranceCardNumber);

// --- DTOs Thẻ chọn (Dropdown Options) ---
public sealed record DepartmentOptionDto(
    int Id, 
    string Name);

public sealed record DoctorOptionDto(
    int Id, 
    int DepartmentId, 
    string Name, 
    string Specialty);

// --- DTOs Dành cho Web Quầy Tiếp Nhận (Reception Desk) ---
public sealed class WalkInReceptionRequest
{
    public int? PatientId { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(100)]
    public string? FullName { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public HIS.Domain.Gender? Gender { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(20)]
    public string? NationalId { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(15)]
    public string? PhoneNumber { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(100)]
    [System.ComponentModel.DataAnnotations.EmailAddress]
    public string? Email { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(255)]
    public string? Address { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(30)]
    public string? InsuranceCardNumber { get; set; }

    public bool IsChild { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(100)]
    public string? GuardianName { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(50)]
    public string? GuardianRelationship { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(15)]
    public string? GuardianPhone { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(20)]
    public string? GuardianNationalId { get; set; }

    public int? DepartmentId { get; set; }

    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
    public int ScheduleId { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(255)]
    public string? Reason { get; set; }

    [System.ComponentModel.DataAnnotations.Range(0, 3)]
    public int PriorityLevel { get; set; }
    public bool IdentityVerified { get; set; }
}

public record DepartmentDoctorScheduleDto(
    int ScheduleId,
    int DoctorId,
    string DoctorName,
    string Specialty,
    string RoomNumber,
    int WaitingCount,
    bool IsFull,
    string ShiftLabel,
    string ShiftName = "",
    string ShiftTime = "",
    bool IsCurrentShift = false,
    bool IsShiftEnded = false);

public record PatientSearchResultDto(
    int Id,
    string FullName,
    string DateOfBirth,
    string Gender,
    string PhoneNumber,
    string? NationalId,
    string? Email,
    string? Address,
    string? InsuranceCardNumber,
    bool IsChild);

public record WalkInTicketDto(
    int AppointmentId,
    string AppointmentCode,
    string QueueNumber,
    string PatientName,
    string DepartmentName,
    string RoomNumber,
    string DoctorName,
    int WaitingCount,
    DateTime CreatedAt);

public record ReceptionVisit(
    int Id,
    string Code,
    string PatientName,
    DateOnly BirthDate,
    string Phone,
    string Doctor,
    string Room,
    DateTime ArrivalTime,
    HIS.Domain.BookingChannel Channel,
    int StatusId,
    string? QueueNumber,
    int PriorityLevel);

public record ReceptionSchedule(int Id, string Label);

public record ReceptionPatient(
    int Id,
    string Name,
    DateOnly BirthDate,
    string Phone);

public record ReceptionDesk(
    string Department,
    DateOnly Date,
    IReadOnlyList<ReceptionVisit> Visits,
    IReadOnlyList<ReceptionSchedule> Schedules,
    IReadOnlyList<ReceptionPatient> Patients,
    IReadOnlyList<DepartmentOptionDto>? Departments = null);
