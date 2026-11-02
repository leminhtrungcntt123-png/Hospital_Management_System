namespace HIS.BLL.DTOs;

public record RegisterPatientRequest(
    string Username,
    string Password,
    string FullName,
    DateOnly DateOfBirth,
    string PhoneNumber,
    string? Email = null,
    string? NationalId = null,
    string? Address = null
);

public record LoginRequest(string Username, string Password);

public record AuthResponse(int AccountId, string Username, string Role, string? FullName = null);

public record CreateProfileRequest(
    string FullName,
    DateOnly DateOfBirth,
    string PhoneNumber,
    string Relationship,
    string? NationalId = null,
    string? Address = null
);

public record RequestLinkRequest(
    int PatientId,
    string Relationship,
    string FullName,
    DateOnly DateOfBirth,
    string? NationalId = null,
    string? InsuranceCardNumber = null,
    string? PhoneNumber = null
);

public record HoldSlotRequest(int SlotId, int PatientId);

public record ConfirmAppointmentRequest(int AppointmentId);

public record WalkInAppointmentRequest(
    int PatientId,
    int DepartmentId,
    int DoctorId,
    int RoomId,
    string? Reason = null
);

