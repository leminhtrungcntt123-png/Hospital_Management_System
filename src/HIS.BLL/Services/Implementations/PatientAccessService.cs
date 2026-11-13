using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class PatientAccessService : IPatientAccessService
{
    private readonly AppDbContext _context;

    public PatientAccessService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<bool> CanBookAsync(int accountId, int patientId, CancellationToken ct = default)
    {
        return await _context.AccountPatientProfiles
            .AnyAsync(p => p.AccountId == accountId && p.PatientId == patientId && p.Status == LinkStatus.Active, ct);
    }

    public async Task<bool> CanViewRecordsAsync(int accountId, int patientId, CancellationToken ct = default)
    {
        return await _context.AccountPatientProfiles
            .AnyAsync(p => p.AccountId == accountId && p.PatientId == patientId && p.Status == LinkStatus.Active && p.CanViewRecords, ct);
    }

    public async Task<bool> CanStaffAccessRecordAsync(int staffAccountId, int appointmentId, CancellationToken ct = default)
    {
        var appointment = await _context.Appointments
            .Include(a => a.Doctor)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);
        if (appointment == null) return false;

        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.AccountId == staffAccountId, ct);
        if (doctor != null)
        {
            return appointment.DepartmentId == doctor.DepartmentId || appointment.DoctorId == doctor.Id;
        }

        var nurse = await _context.Nurses.FirstOrDefaultAsync(n => n.AccountId == staffAccountId, ct);
        if (nurse != null)
        {
            return appointment.DepartmentId == nurse.DepartmentId;
        }

        var deptHead = await _context.DepartmentHeads.FirstOrDefaultAsync(h => h.AccountId == staffAccountId, ct);
        if (deptHead != null)
        {
            return appointment.DepartmentId == deptHead.DepartmentId;
        }

        return false;
    }
}
