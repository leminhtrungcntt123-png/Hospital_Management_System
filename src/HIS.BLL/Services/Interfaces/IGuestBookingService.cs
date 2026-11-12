using HIS.BLL.Common;
using HIS.BLL.DTOs;

namespace HIS.BLL.Services;

public interface IGuestBookingService
{
    Task<Result<IReadOnlyList<GuestBookingSlot>>> GetSlotsAsync(int doctorId, DateOnly date, CancellationToken ct = default);
    Task<Result<GuestBookingReceipt>> ConfirmAsync(GuestBookingRequest request, CancellationToken ct = default);
}
