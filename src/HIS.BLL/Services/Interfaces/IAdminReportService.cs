using HIS.BLL.DTOs;

namespace HIS.BLL.Services;

public interface IAdminReportService
{
    Task<RevenueReport> GetRevenueAsync(int actorId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<byte[]> ExportRevenueAsync(int actorId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
