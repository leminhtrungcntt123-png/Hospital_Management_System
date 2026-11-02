using HIS.BLL.Common;
using HIS.BLL.DTOs;

namespace HIS.BLL.Services;

/// <summary>
/// Quản lý liên kết tài khoản - người khám:
/// - Tạo hồ sơ MỚI -> link ACTIVE ngay. can_view_records mặc định true cho SELF và cho CHILD khi &lt; 18 tuổi; false cho mọi quan hệ còn lại.
/// - Hồ sơ ĐÃ CÓ: được tự yêu cầu liên kết với mọi quan hệ TRỪ SELF. Kiểm tra khớp thông tin. Khớp -> PENDING (can_view_records=false).
/// - PENDING -> ACTIVE: nhân viên xác nhận khi check-in hoặc tài khoản ACTIVE đang liên kết duyệt.
/// - Với người &gt;= 18 tuổi, chỉ bật can_view_records khi người bệnh đồng ý, do nhân viên thực hiện.
/// </summary>
public interface IPatientProfileService
{
    Task<Result<int>> CreateProfileAsync(int accountId, CreateProfileRequest request, CancellationToken ct = default);
    Task<Result> RequestLinkAsync(int accountId, RequestLinkRequest request, CancellationToken ct = default);
    Task<Result> VerifyLinkAsync(int linkId, bool grantRecordAccess, int verifiedByAccountId, CancellationToken ct = default);
    Task<Result> RevokeLinkAsync(int linkId, int revokedByAccountId, CancellationToken ct = default);
}

public class PatientProfileService : IPatientProfileService
{
    public Task<Result<int>> CreateProfileAsync(int accountId, CreateProfileRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Result> RequestLinkAsync(int accountId, RequestLinkRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Result> VerifyLinkAsync(int linkId, bool grantRecordAccess, int verifiedByAccountId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Result> RevokeLinkAsync(int linkId, int revokedByAccountId, CancellationToken ct = default) => throw new NotImplementedException();
}
