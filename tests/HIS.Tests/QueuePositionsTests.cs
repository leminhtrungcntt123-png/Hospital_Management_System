using HIS.Domain;
using Xunit;

namespace HIS.Tests;

/// <summary>
/// Chia suất A/T và cách dựng số hiển thị theo vị trí (không dùng bộ đếm), chạy không cần CSDL.
/// </summary>
public class QueuePositionsTests
{
    [Theory]
    [InlineData(5, 60, 3)]   // 3 suất A + 2 suất T theo ví dụ đã chốt
    [InlineData(4, 60, 2)]   // 2A + 2T
    [InlineData(3, 60, 2)]   // 1.8 làm tròn lên 2
    [InlineData(6, 60, 4)]   // 3.6 làm tròn lên 4
    [InlineData(8, 50, 4)]
    [InlineData(1, 60, 1)]   // khung chỉ có một suất thì dành cho web
    [InlineData(2, 30, 1)]   // 0.6 làm tròn lên 1, không được phép ra 0
    [InlineData(5, 0, 0)]    // không dành suất nào cho web
    [InlineData(5, 100, 5)]  // dành hết cho web, quầy không còn suất T
    [InlineData(4, 150, 4)]  // tỉ lệ ngoài range bị kẹp nên quota không vượt capacity
    [InlineData(0, 60, 0)]
    public void OnlineQuota_lam_tron_cap_nhat_va_khong_vuot_capacity(int capacity, int ratioPct, int expected)
    {
        int quota = QueuePositions.OnlineQuota(capacity, ratioPct);

        Assert.Equal(expected, quota);
        Assert.InRange(quota, 0, capacity);
    }

    [Fact]
    public void OnlineQuota_khong_bao_gio_vuot_capacity_voice_ty_le()
    {
        for (int capacity = 1; capacity <= 12; capacity++)
        {
            for (int ratio = 0; ratio <= 100; ratio++)
            {
                int quota = QueuePositions.OnlineQuota(capacity, ratio);
                Assert.InRange(quota, 0, capacity);
            }
        }
    }

    [Fact]
    public void Vi_tri_A_xep_truoc_vi_tri_T_trong_cung_khung()
    {
        const int capacity = 5;
        int quota = QueuePositions.OnlineQuota(capacity, 60);

        Assert.Equal(3, quota);
        Assert.True(QueuePositions.IsOnlinePosition(1, quota));
        Assert.True(QueuePositions.IsOnlinePosition(quota, quota));
        Assert.False(QueuePositions.IsOnlinePosition(quota + 1, quota));
        Assert.False(QueuePositions.IsOnlinePosition(capacity, quota));
    }

    [Fact]
    public void So_chay_lien_tuc_qua_cac_khung_cua_mot_ca()
    {
        // Khung 07:00-08:00 capacity 5 chiếm 1..5, khung 08:00-09:00 tiếp tục từ 6 nên không trùng số.
        Assert.Equal(1, QueuePositions.ShiftNumber(capacityBefore: 0, slotPosition: 1));
        Assert.Equal(5, QueuePositions.ShiftNumber(capacityBefore: 0, slotPosition: 5));
        Assert.Equal(6, QueuePositions.ShiftNumber(capacityBefore: 5, slotPosition: 1));
        Assert.Equal(10, QueuePositions.ShiftNumber(capacityBefore: 5, slotPosition: 5));
    }

    [Theory]
    [InlineData(BookingChannel.Web, 0, 1, "A01")]
    [InlineData(BookingChannel.Web, 5, 1, "A06")]
    [InlineData(BookingChannel.OnSite, 0, 4, "T04")]
    [InlineData(BookingChannel.OnSite, 5, 3, "T08")]
    public void So_hien_thi_gan_ky_hieu_kenh_va_pad_hai_chu_so(
        BookingChannel channel, int capacityBefore, int slotPosition, string expected)
    {
        string number = QueueNumberPrefix.Format(channel, QueuePositions.ShiftNumber(capacityBefore, slotPosition));

        Assert.Equal(expected, number);
    }

    [Fact]
    public void Ky_hieu_so_phan_dung_kenh_dat_lich()
    {
        Assert.Equal(QueueNumberPrefix.Web, QueueNumberPrefix.For(BookingChannel.Web));
        Assert.Equal(QueueNumberPrefix.OnSite, QueueNumberPrefix.For(BookingChannel.OnSite));
        Assert.Equal('A', QueueNumberPrefix.For(BookingChannel.Web));
        Assert.Equal('T', QueueNumberPrefix.For(BookingChannel.OnSite));
    }

    [Fact]
    public void Ca_duoi_12_gio_la_ca_sang_tu_12_gio_la_ca_chieu()
    {
        Assert.Equal(ExamShift.Morning, ExamShifts.For(DateTime.Today.AddHours(7)));
        Assert.Equal(ExamShift.Morning, ExamShifts.For(DateTime.Today.AddHours(11).AddMinutes(59)));
        Assert.Equal(ExamShift.Afternoon, ExamShifts.For(DateTime.Today.AddHours(12)));
        Assert.Equal(ExamShift.Afternoon, ExamShifts.For(DateTime.Today.AddHours(17)));
    }
}
