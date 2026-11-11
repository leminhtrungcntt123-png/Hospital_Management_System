using System.ComponentModel.DataAnnotations;
using HIS.Domain;

namespace HIS.BLL.DTOs;

public sealed class GuestBookingRequest
{
    [Range(1, int.MaxValue)]
    public int ScheduleId { get; set; }

    public DateTime StartTime { get; set; }

    [Required, StringLength(100, MinimumLength = 2), RegularExpression(@"^[^\p{Cc}<>]+$")]
    public string FullName { get; set; } = "";

    public DateOnly DateOfBirth { get; set; }

    [Required, RegularExpression(@"^(0[0-9]{9}|\+84[0-9]{9})$")]
    public string PhoneNumber { get; set; } = "";

    [EnumDataType(typeof(Gender))]
    public Gender? Gender { get; set; }

    [StringLength(255)]
    public string? Address { get; set; }

    [StringLength(255)]
    public string? Reason { get; set; }
}

public sealed record GuestBookingSlot(
    int ScheduleId,
    DateTime StartTime,
    DateTime EndTime,
    string Room,
    int Remaining);

public sealed record GuestBookingReceipt(
    string AppointmentCode,
    string PatientName,
    string DoctorName,
    string Department,
    string Room,
    DateTime StartTime,
    DateTime EndTime);
