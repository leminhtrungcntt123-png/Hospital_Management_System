using HIS.BLL.Services;
using HIS.Domain;
using Xunit;

namespace HIS.Tests;

/// <summary>
/// Test thuần, không chạm CSDL: quy tắc chen hàng của hàng đợi và ngưỡng tuổi của mức ưu tiên 1.
/// </summary>
public class QueueCallOrderTests
{
    private const int MaxOvertakeAhead = 3;
    private const int MaxTimesOvertaken = 3;

    private static readonly DateTime FrameStart = new(2026, 11, 5, 7, 30, 0);

    private static Appointment Waiting(int id, int slotPosition, int priorityLevel = 0, int overtakenCount = 0) =>
        new()
        {
            Id = id,
            AppointmentCode = $"HIS-T-{id:D3}",
            Slot = new AppointmentSlot
            {
                Id = 1,
                RoomId = 1,
                DoctorId = 1,
                StartTime = FrameStart,
                EndTime = FrameStart.AddHours(1),
                Capacity = 8,
                OnlineQuota = 4
            },
            SlotPosition = slotPosition,
            PriorityLevel = priorityLevel,
            OvertakenCount = overtakenCount,
            CreatedAt = FrameStart
        };

    private static int[] PositionsOf(IReadOnlyList<Appointment> queue) => queue.Select(a => a.SlotPosition!.Value).ToArray();

    [Fact]
    public void Luot_uu_tien_muc_2_chi_chen_toi_da_MaxOvertakeAhead_nguoi()
    {
        var queue = new List<Appointment>
        {
            Waiting(1, 1), Waiting(2, 2), Waiting(3, 3), Waiting(4, 4), Waiting(5, 5), Waiting(6, 6, PriorityLevels.StaffPriority)
        };

        IReadOnlyList<Appointment> ordered = QueueCallOrder.Order(queue, MaxOvertakeAhead, MaxTimesOvertaken);

        // Vị trí 6 nhảy lên trước đúng 3 người: 1,2 giữ chỗ, 6 chen vào trước 3,4,5.
        Assert.Equal([1, 2, 6, 3, 4, 5], PositionsOf(ordered));
    }

    [Fact]
    public void Nguoi_da_bi_chen_du_tran_khong_bi_chen_them()
    {
        // Người đứng đầu đã bị chen đủ 3 lần: lượt ưu tiên chen qua được 2 người phía sau rồi dừng lại.
        var queue = new List<Appointment>
        {
            Waiting(1, 1, overtakenCount: MaxTimesOvertaken), Waiting(2, 2), Waiting(3, 3), Waiting(4, 4, PriorityLevels.StaffPriority)
        };

        IReadOnlyList<Appointment> ordered = QueueCallOrder.Order(queue, MaxOvertakeAhead, MaxTimesOvertaken);

        Assert.Equal([1, 4, 2, 3], PositionsOf(ordered));
    }

    [Fact]
    public void Luot_uu_tien_khong_chen_qua_mot_luu_uu_tien_khac()
    {
        var queue = new List<Appointment>
        {
            Waiting(1, 1, PriorityLevels.StaffPriority), Waiting(2, 2, PriorityLevels.StaffPriority)
        };

        IReadOnlyList<Appointment> ordered = QueueCallOrder.Order(queue, MaxOvertakeAhead, MaxTimesOvertaken);

        Assert.Equal([1, 2], PositionsOf(ordered));
    }

    [Fact]
    public void Hai_luu_uu_tien_lien_tiep_khong_cung_chen_qua_mot_nguoi_qua_tran()
    {
        // Người đứng đầu đã bị chen 2 lần: lượt ưu tiên thứ nhất dùng nốt lần thứ ba,
        // lượt thứ hai vì vậy không chen qua nổi ai nữa.
        var queue = new List<Appointment>
        {
            Waiting(1, 1, overtakenCount: 2),
            Waiting(2, 2, PriorityLevels.StaffPriority),
            Waiting(3, 3, PriorityLevels.StaffPriority)
        };

        IReadOnlyList<Appointment> ordered = QueueCallOrder.Order(queue, MaxOvertakeAhead, MaxTimesOvertaken);

        Assert.Equal([2, 1, 3], PositionsOf(ordered));
    }

    [Fact]
    public void Cung_khung_gio_thi_muc_uu_tien_cao_hon_goi_truoc()
    {
        var queue = new List<Appointment>
        {
            Waiting(1, 1), Waiting(2, 2, PriorityLevels.AgePriority), Waiting(3, 3)
        };

        IReadOnlyList<Appointment> ordered = QueueCallOrder.Order(queue, MaxOvertakeAhead, MaxTimesOvertaken);

        // Mức 1 chỉ đổi thứ tự trong cùng khung giờ, không chen qua khung khác.
        Assert.Equal([2, 1, 3], PositionsOf(ordered));
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(69, false)]
    [InlineData(70, true)]
    public void Muc_uu_tien_1_tu_tinh_theo_tuo(int age, bool expected)
    {
        var today = new DateOnly(2026, 11, 5);
        DateOnly dateOfBirth = today.AddYears(-age);

        Assert.Equal(
            expected,
            PatientAge.NeedsAgePriority(dateOfBirth, today, childUnderAge: 6, seniorFromAge: 70));
    }

    [Fact]
    public void Thu_tu_goi_khong_trao_doi_luu_khong_co_khung_gio()
    {
        var noSlot = Waiting(9, 1);
        noSlot.Slot = null;
        noSlot.SlotPosition = null;

        IReadOnlyList<Appointment> ordered = QueueCallOrder.Order([noSlot, Waiting(1, 1)], MaxOvertakeAhead, MaxTimesOvertaken);

        Assert.Equal(1, ordered[0].Id);
        Assert.Equal(9, ordered[1].Id);
    }
}
