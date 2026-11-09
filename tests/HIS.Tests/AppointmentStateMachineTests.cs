using HIS.BLL.Services;
using HIS.Domain;

namespace HIS.Tests;

/// <summary>
/// Test thuần bảng chuyển trạng thái lượt khám (init.md mục 7), không chạm CSDL.
/// </summary>
public class AppointmentStateMachineTests
{
    private static readonly string[] All =
    [
        AppointmentStatus.Codes.Pending,
        AppointmentStatus.Codes.Confirmed,
        AppointmentStatus.Codes.CheckedIn,
        AppointmentStatus.Codes.ReadyForDoctor,
        AppointmentStatus.Codes.InProgress,
        AppointmentStatus.Codes.Completed,
        AppointmentStatus.Codes.Cancelled,
        AppointmentStatus.Codes.NoShow,
        AppointmentStatus.Codes.Expired,
        AppointmentStatus.Codes.RescheduleRequired
    ];

    private static readonly (string From, string To)[] Legal =
    [
        (AppointmentStatus.Codes.Pending, AppointmentStatus.Codes.Confirmed),
        (AppointmentStatus.Codes.Pending, AppointmentStatus.Codes.Expired),
        (AppointmentStatus.Codes.Pending, AppointmentStatus.Codes.Cancelled),
        (AppointmentStatus.Codes.Confirmed, AppointmentStatus.Codes.CheckedIn),
        (AppointmentStatus.Codes.Confirmed, AppointmentStatus.Codes.Cancelled),
        (AppointmentStatus.Codes.Confirmed, AppointmentStatus.Codes.NoShow),
        (AppointmentStatus.Codes.Confirmed, AppointmentStatus.Codes.RescheduleRequired),
        (AppointmentStatus.Codes.CheckedIn, AppointmentStatus.Codes.ReadyForDoctor),
        (AppointmentStatus.Codes.CheckedIn, AppointmentStatus.Codes.Cancelled),
        (AppointmentStatus.Codes.ReadyForDoctor, AppointmentStatus.Codes.InProgress),
        (AppointmentStatus.Codes.InProgress, AppointmentStatus.Codes.Completed),
        (AppointmentStatus.Codes.RescheduleRequired, AppointmentStatus.Codes.Cancelled)
    ];

    [Theory]
    [InlineData(AppointmentStatus.Codes.Pending, AppointmentStatus.Codes.Completed)]
    [InlineData(AppointmentStatus.Codes.Pending, AppointmentStatus.Codes.NoShow)]
    [InlineData(AppointmentStatus.Codes.Pending, AppointmentStatus.Codes.CheckedIn)]
    [InlineData(AppointmentStatus.Codes.Confirmed, AppointmentStatus.Codes.Expired)]
    [InlineData(AppointmentStatus.Codes.Confirmed, AppointmentStatus.Codes.InProgress)]
    [InlineData(AppointmentStatus.Codes.Confirmed, AppointmentStatus.Codes.Pending)]
    [InlineData(AppointmentStatus.Codes.CheckedIn, AppointmentStatus.Codes.NoShow)]
    [InlineData(AppointmentStatus.Codes.CheckedIn, AppointmentStatus.Codes.Completed)]
    [InlineData(AppointmentStatus.Codes.InProgress, AppointmentStatus.Codes.Cancelled)]
    [InlineData(AppointmentStatus.Codes.InProgress, AppointmentStatus.Codes.NoShow)]
    [InlineData(AppointmentStatus.Codes.Completed, AppointmentStatus.Codes.CheckedIn)]
    [InlineData(AppointmentStatus.Codes.Cancelled, AppointmentStatus.Codes.Pending)]
    [InlineData(AppointmentStatus.Codes.NoShow, AppointmentStatus.Codes.Confirmed)]
    [InlineData(AppointmentStatus.Codes.Expired, AppointmentStatus.Codes.Confirmed)]
    [InlineData(AppointmentStatus.Codes.RescheduleRequired, AppointmentStatus.Codes.CheckedIn)]
    public void Chuyen_trang_thai_ngoai_bang_chuan_bi_tu_choi(string from, string to) =>
        Assert.False(AppointmentStateMachine.CanTransition(from, to));

    [Fact]
    public void Dai_100_cap_trang_thai_khop_dung_bang_chuan()
    {
        Assert.Equal(All.Length, AppointmentStateMachine.Codes.Count);

        foreach (string from in All)
        {
            foreach (string to in All)
            {
                bool expected = Legal.Any(e => e.From == from && e.To == to);
                Assert.Equal(expected, AppointmentStateMachine.CanTransition(from, to));
            }
        }
    }

    [Fact]
    public void Bon_trang_thai_ket_thuc_khong_di_duoc_tiep()
    {
        string[] terminal =
        [
            AppointmentStatus.Codes.Completed,
            AppointmentStatus.Codes.Cancelled,
            AppointmentStatus.Codes.NoShow,
            AppointmentStatus.Codes.Expired
        ];

        Assert.All(terminal, code =>
        {
            Assert.True(AppointmentStateMachine.IsTerminal(code));
            Assert.Empty(AppointmentStateMachine.AllowedNext(code));
        });

        Assert.All(All.Except(terminal), code => Assert.False(AppointmentStateMachine.IsTerminal(code)));
    }

    [Fact]
    public void Cho_du_sau_chuyen_khop_bang_chuan()
    {
        foreach ((string from, _) in Legal)
        {
            IReadOnlyList<string> allowed = AppointmentStateMachine.AllowedNext(from);
            Assert.NotEmpty(allowed);
            Assert.All(allowed, to => Assert.Contains((from, to), Legal));
        }

        Assert.Equal(
            new[] { AppointmentStatus.Codes.ReadyForDoctor, AppointmentStatus.Codes.Cancelled },
            AppointmentStateMachine.AllowedNext(AppointmentStatus.Codes.CheckedIn));
    }

    [Theory]
    [InlineData("PENDING_TT", AppointmentStatus.Codes.Confirmed)]
    [InlineData(AppointmentStatus.Codes.Confirmed, "")]
    [InlineData("SAI", "SAI")]
    public void Ma_trang_thai_khong_ho_le_nem_exception(string from, string to) =>
        Assert.Throws<ArgumentException>(() => AppointmentStateMachine.CanTransition(from, to));
}
