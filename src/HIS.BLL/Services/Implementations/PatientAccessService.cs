namespace HIS.BLL.Services;

public class PatientAccessService : IPatientAccessService
{
    public Task<bool> CanBookAsync(int accountId, int patientId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<bool> CanViewRecordsAsync(int accountId, int patientId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<bool> CanStaffAccessRecordAsync(int staffAccountId, int appointmentId, CancellationToken ct = default)
        => throw new NotImplementedException();
}
