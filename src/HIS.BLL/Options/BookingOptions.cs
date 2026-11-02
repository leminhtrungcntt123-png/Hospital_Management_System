using System.ComponentModel.DataAnnotations;

namespace HIS.BLL.Options;

public class BookingOptions
{
    public const string SectionName = "Booking";

    [Range(1, 120)]
    public int HoldMinutes { get; set; } = 10;

    [Range(1, 20)]
    public int MaxPatientProfilesPerAccount { get; set; } = 5;

    [Range(1, 20)]
    public int MaxActiveAppointmentsPerAccount { get; set; } = 5;

    [Range(1, 50)]
    public int MaxLinkRequestsPerDay { get; set; } = 5;

    [Range(1, 90)]
    public int PendingLinkExpiryDays { get; set; } = 14;

    [Range(1, 60)]
    public int SlotGenerationDaysAhead { get; set; } = 14;

    [Range(1, 200)]
    public int DefaultMaxPatients { get; set; } = 40;

    [Range(0, 200)]
    public int DefaultOnlineSlots { get; set; } = 28;

    [Range(1, 1440)]
    public int CancelCutoffMinutes { get; set; } = 120;

    [Range(1, 1440)]
    public int CheckInEarlyMinutes { get; set; } = 60;

    [Range(1, 10)]
    public int MaxNoShowsBeforeBlock { get; set; } = 3;

    [Range(1, 365)]
    public int NoShowWindowDays { get; set; } = 90;
}

