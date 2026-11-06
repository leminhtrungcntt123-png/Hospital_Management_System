using HIS.BLL.Common;

namespace HIS.BLL.Services;

public partial class ScheduleChangeService(HIS.DAL.AppDbContext db, TimeProvider clock) : IScheduleChangeService
{
    public Task<Result> ProcessDoctorLeaveAsync(int doctorId, DateOnly date, string? shift, int? replacementDoctorId, CancellationToken ct = default)
        => throw new NotImplementedException();
}
