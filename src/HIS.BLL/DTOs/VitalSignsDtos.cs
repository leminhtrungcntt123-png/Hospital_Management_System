using System.ComponentModel.DataAnnotations;

namespace HIS.BLL.DTOs;

public sealed class RecordVitalSignsRequest : IValidatableObject
{
    [Required(ErrorMessage = "Thiếu mã lượt khám (AppointmentId).")]
    public int AppointmentId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập chỉ số Mạch.")]
    [Range(30, 220, ErrorMessage = "Mạch phải nằm trong khoảng 30 – 220 lần/phút.")]
    public int? Pulse { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập Huyết áp tâm thu.")]
    [Range(50, 260, ErrorMessage = "Huyết áp tâm thu phải nằm trong khoảng 50 – 260 mmHg.")]
    public int? SystolicBp { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập Huyết áp tâm trương.")]
    [Range(30, 160, ErrorMessage = "Huyết áp tâm trương phải nằm trong khoảng 30 – 160 mmHg.")]
    public int? DiastolicBp { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập Thân nhiệt.")]
    [Range(34.0, 43.0, ErrorMessage = "Thân nhiệt phải nằm trong khoảng 34.0 – 43.0 °C.")]
    public decimal? Temperature { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập Cân nặng.")]
    [Range(1.0, 300.0, ErrorMessage = "Cân nặng phải nằm trong khoảng 1.0 – 300.0 kg.")]
    public decimal? Weight { get; set; }

    [Range(8, 60, ErrorMessage = "Nhịp thở phải nằm trong khoảng 8 – 60 lần/phút.")]
    public int? RespiratoryRate { get; set; }

    [Range(50, 100, ErrorMessage = "Nồng độ SpO2 phải nằm trong khoảng 50 – 100%.")]
    public int? Spo2 { get; set; }

    [Range(30.0, 250.0, ErrorMessage = "Chiều cao phải nằm trong khoảng 30.0 – 250.0 cm.")]
    public decimal? Height { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SystolicBp.HasValue && DiastolicBp.HasValue)
        {
            if (SystolicBp.Value <= DiastolicBp.Value)
            {
                yield return new ValidationResult(
                    "Huyết áp tâm thu phải lớn hơn huyết áp tâm trương.",
                    [nameof(SystolicBp), nameof(DiastolicBp)]);
            }
            else if (SystolicBp.Value - DiastolicBp.Value < 10)
            {
                yield return new ValidationResult(
                    "Hiệu áp (Tâm thu - Tâm trương) tối thiểu phải đạt 10 mmHg.",
                    [nameof(SystolicBp), nameof(DiastolicBp)]);
            }
        }
    }
}

public sealed record VitalSignsDetailDto(
    int Id,
    int AppointmentId,
    int? Pulse,
    int? SystolicBp,
    int? DiastolicBp,
    decimal? Temperature,
    int? RespiratoryRate,
    int? Spo2,
    decimal? Weight,
    decimal? Height,
    int RecordedBy,
    string? RecordedByNurseName,
    DateTime RecordedAt);

public sealed record VitalSignsQueueItemDto(
    int AppointmentId,
    string AppointmentCode,
    string? QueueNumber,
    int PatientId,
    string PatientName,
    DateOnly BirthDate,
    string Gender,
    string PhoneNumber,
    string? DoctorName,
    string? RoomName,
    DateTime ScheduledTime,
    int StatusId,
    bool HasVitalSigns,
    VitalSignsDetailDto? VitalSigns);

public sealed class StartExamRequest
{
    [Required(ErrorMessage = "Thiếu mã lượt khám (AppointmentId).")]
    public int AppointmentId { get; set; }
}
