namespace HIS.BLL.DTOs;

public sealed record PatientAppointmentItemDto(
    int Id,
    string AppointmentCode,
    string PatientName,
    string DoctorName,
    string DepartmentName,
    string RoomNumber,
    DateTime? StartTime,
    DateTime? EndTime,
    int StatusId,
    string StatusName,
    string? Reason,
    bool CanCancel,
    DateTime CreatedAt);

public sealed record PatientNotificationItemDto(
    int Id,
    int? AppointmentId,
    string Title,
    string? Content,
    bool IsRead,
    DateTime CreatedAt);

public sealed record PatientNotificationsDto(
    int UnreadCount,
    IReadOnlyList<PatientNotificationItemDto> Items);

public sealed record CancelAppointmentRequest(int AppointmentId);

public sealed record MarkNotificationReadRequest(int? NotificationId);
