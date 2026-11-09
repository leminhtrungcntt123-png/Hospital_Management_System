namespace HIS.BLL.DTOs;

/// <summary>
/// Một liên kết tài khoản <-> hồ sơ. Giữ mã (code) + nhãn tiếng Việt riêng vì
/// WinForms/Web hiển thị nhãn còn DB và API nói chuyện bằng mã UPPER_SNAKE.
/// </summary>
public sealed record PatientProfileLinkDto(
    int AccountId,
    string AccountUsername,
    int PatientId,
    string PatientCode,
    string PatientFullName,
    string PatientPhone,
    DateOnly DateOfBirth,
    string GenderLabel,
    string RelationshipCode,
    string RelationshipLabel,
    string StatusCode,
    string StatusLabel,
    bool CanViewRecords,
    bool HasAppointmentToday,
    DateTime CreatedAt,
    DateTime? VerifiedAt);
