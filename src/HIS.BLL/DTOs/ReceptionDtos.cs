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