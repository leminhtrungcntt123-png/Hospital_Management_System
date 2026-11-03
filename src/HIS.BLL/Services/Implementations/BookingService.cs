using HIS.BLL.Common;
using HIS.BLL.DTOs;

namespace HIS.BLL.Services;

public class BookingService : IBookingService
{
    public Task<Result<int>> HoldSlotAsync(int accountId, HoldSlotRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Result<string>> ConfirmAppointmentAsync(int accountId, ConfirmAppointmentRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Result> CancelAppointmentAsync(int accountId, int appointmentId, string reason, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<Result<int>> CreateWalkInAppointmentAsync(int staffAccountId, WalkInAppointmentRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();
}
