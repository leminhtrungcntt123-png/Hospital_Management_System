using HIS.BLL.DTOs;

namespace HIS.BLL.Services;

public interface IAdminReportService
{
    Task<RevenueReport> GetRevenueAsync(int actorId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<byte[]> ExportRevenueAsync(int actorId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<DepartmentVisitStat>> GetDepartmentStatsAsync(int actorId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<AdminAuditLogRow>> GetAuditLogsAsync(int actorId, DateOnly? from = null, DateOnly? to = null, string? action = null, string? search = null, int limit = 200, CancellationToken ct = default);
}
