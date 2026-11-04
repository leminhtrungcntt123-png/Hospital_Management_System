namespace HIS.Domain;

/// <summary>
/// Liên kết nhiều-nhiều giữa tài khoản đặt lịch và hồ sơ người khám.
/// relationship tính từ góc nhìn CHỦ TÀI KHOẢN (mẹ liên kết con thì ghi CHILD).
/// Một hồ sơ được phép thuộc nhiều tài khoản; ngược lại một tài khoản có nhiều hồ sơ.
/// </summary>
public class AccountPatientProfile
{
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public PatientRelationship Relationship { get; set; }
    public LinkStatus Status { get; set; } = LinkStatus.Pending;
    public bool CanViewRecords { get; set; }

    /// <summary>Tài khoản tự tạo hồ sơ thì left NULL; hồ sơ do quầy tạo hộ có thể ghi người tạo.</summary>
    public int? CreatedBy { get; set; }
    public Account? Creator { get; set; }

    /// <summary>Nhân viên (hoặc tài khoản đang liên kết) đã xác minh giấy tờ khi check-in.</summary>
    public int? VerifiedBy { get; set; }
    public Account? Verifier { get; set; }
    public DateTime? VerifiedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
