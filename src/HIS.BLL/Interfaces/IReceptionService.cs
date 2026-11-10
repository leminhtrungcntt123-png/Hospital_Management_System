using HIS.Domain;
using HIS.BLL.Common;
using HIS.BLL.DTOs;

namespace HIS.BLL.Interfaces;

public sealed class CreateReceptionRequest
{
    public int? PatientId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? NationalId { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? InsuranceCardNumber { get; set; }
    public int DepartmentId { get; set; }
    public int? DoctorId { get; set; }
    public int PriorityLevel { get; set; }
    public string? Reason { get; set; }

    /// <summary>
    /// Ca cấp cứu không bốc số: không giữ chỗ trong khung giờ, không có số thứ tự,
    /// vào thẳng trạng thái đang khám để bác sĩ xử lý ngay.
    /// </summary>
    public bool IsEmergency { get; set; }

    /// <summary>Tài khoản nhân viên đang thao tác (ghi vào appointments.booked_by để biết ai mở ca).</summary>
    public int? OperatorAccountId { get; set; }
}

public sealed record CreateReceptionResult(
    int AppointmentId,
    string AppointmentCode,
    int PatientId,
    string PatientCode,
    string QueueNumber,
    bool IsNewPatient);

public sealed record ReceptionActionResult(
    bool Success, 
    string Message, 
    string QueueNumber = "", 
    bool IsOverdue = false, 
    int? AppointmentId = null);

public interface IReceptionService
{
    Task<ReceptionCountsDto> GetCountsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReceptionItemDto>> GetReceptionListAsync(ReceptionTab tab, string? search = null, CancellationToken cancellationToken = default);
    Task<ReceptionPatientDto?> FindPatientAsync(string keyword, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DepartmentOptionDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DoctorOptionDto>> GetDoctorsAsync(int? departmentId = null, CancellationToken cancellationToken = default);
    Task<CreateReceptionResult> CreateReceptionAsync(CreateReceptionRequest request, CancellationToken cancellationToken = default);
    Task<ReceptionActionResult> CheckInAsync(int appointmentId, int? operatorAccountId = null, CancellationToken cancellationToken = default);
    Task<int> ExpireOverdueAppointmentsAsync(CancellationToken cancellationToken = default);
    Task<ReceptionActionResult> ConvertToWalkInAsync(int appointmentId, int? operatorAccountId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nhân viên quầy gán mức ưu tiên 2: lượt này được chen lên trước tối đa MaxOvertakeAhead người đang chờ,
    /// mỗi người bị chen tăng overtaken_count và không bị chen quá MaxTimesOvertaken lần.
    /// </summary>
    Task<ReceptionActionResult> AssignStaffPriorityAsync(int appointmentId, int? operatorAccountId = null, CancellationToken cancellationToken = default);

    // --- Web Quầy Tiếp Nhận (Reception Desk) ---
    Task<Result<ReceptionDesk>> GetDeskAsync(int staffAccountId, string? search, CancellationToken ct = default);
    Task<Result<string>> CheckInAsync(int staffAccountId, int appointmentId, bool identityVerified, CancellationToken ct = default);
    Task<Result<WalkInTicketDto>> RegisterWalkInAsync(int staffAccountId, WalkInReceptionRequest request, CancellationToken ct = default);
    Task<Result<PatientSearchResultDto?>> FindPatientByNationalIdAsync(string nationalId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PatientSearchResultDto>>> SearchPatientsAsync(string keyword, CancellationToken ct = default);
    Task<Result<IReadOnlyList<DepartmentDoctorScheduleDto>>> GetDepartmentSchedulesWithLoadAsync(int departmentId, DateOnly? date = null, CancellationToken ct = default);
    Task<Result<WalkInTicketDto?>> GetWalkInTicketAsync(string? queueNumber = null, int? appointmentId = null, CancellationToken ct = default);
}
