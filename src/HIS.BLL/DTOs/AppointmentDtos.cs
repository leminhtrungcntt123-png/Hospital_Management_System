namespace HIS.BLL.DTOs;

public record HoldSlotRequest(int SlotId, int PatientId);

public record ConfirmAppointmentRequest(int AppointmentId);

public record WalkInAppointmentRequest(
    int PatientId,
    int DepartmentId,
    int DoctorId,
    int RoomId,
    string? Reason = null
);

