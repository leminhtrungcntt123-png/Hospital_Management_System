using HIS.BLL.Interfaces;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

/// <summary>
/// Mọi quyết định "được đặt lịch / được xem hồ sơ" đi qua đây, không nơi nào khác
/// được tự suy quyền từ cột cũ (patients.account_id đã bị bỏ trong đợt align).
/// </summary>
public class PatientAccessService : IPatientAccessService
{
    private readonly AppDbContext _context;

    public PatientAccessService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<bool> CanBookAsync(int accountId, int patientId, CancellationToken cancellationToken = default)
    {
        return await _context.AccountPatientProfiles.AnyAsync(
            l => l.AccountId == accountId
                 && l.PatientId == patientId
                 && (l.Status == LinkStatus.Active || l.Status == LinkStatus.Pending),
            cancellationToken);
    }

    public async Task<bool> CanViewRecordsAsync(int accountId, int patientId, CancellationToken cancellationToken = default)
    {
        return await _context.AccountPatientProfiles.AnyAsync(
            l => l.AccountId == accountId
                 && l.PatientId == patientId
                 && l.Status == LinkStatus.Active
                 && l.CanViewRecords,
            cancellationToken);
    }

    public async Task<bool> LogRecordAccessAsync(
        int accountId,
        int patientId,
        string action,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        bool allowed = await CanViewRecordsAsync(accountId, patientId, cancellationToken);

        // Cột access_logs.action VARCHAR(50); nhãn DENIED_... vẫn nằm trong giới hạn.
        string deniedAction = ("DENIED_" + action).ToUpperInvariant();
        if (deniedAction.Length > 50)
        {
            deniedAction = deniedAction[..50];
        }

        _context.AccessLogs.Add(new AccessLog
        {
            AccountId = accountId,
            PatientId = patientId,
            Action = allowed ? action.ToUpperInvariant() : deniedAction,
            OccurredAt = DateTime.Now,
            IpAddress = ipAddress
        });

        await _context.SaveChangesAsync(cancellationToken);
        return allowed;
    }
}
