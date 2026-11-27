using HIS.BLL.Common;
using HIS.BLL.DTOs;

namespace HIS.BLL.Services;

public interface IPatientPortalService
{
    Task<Result<IReadOnlyList<PatientAppointmentItemDto>>> GetAppointmentsAsync(int accountId, CancellationToken ct = default);
    Task<Result> CancelAppointmentAsync(int accountId, int appointmentId, CancellationToken ct = default);
    Task<Result<PatientNotificationsDto>> GetNotificationsAsync(int accountId, CancellationToken ct = default);
    Task<Result> MarkNotificationsReadAsync(int accountId, int? notificationId = null, CancellationToken ct = default);
}
