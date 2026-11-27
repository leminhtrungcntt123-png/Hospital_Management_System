using HIS.BLL.DTOs;
using HIS.BLL.Services;
using Xunit;

namespace HIS.Tests.Unit;

public class CashierShiftHandoverTests
{
    [Fact]
    public void CashierShift_HandoverAndTopUpCalculations_AreExact()
    {
        var req = new OpenShiftRequest(
            CashierAccountId: 5,
            OpeningCash: 0m,
            Note: "Nhận ca chiều + tiếp quỹ",
            HandoverCash: 50000m,
            TopUpCash: 5000000m,
            PreviousExpectedHandover: 50000m);

        var (effectiveOpening, handover, topUp, handoverDiff) = CashierShiftService.ComputeOpeningComponents(req);

        Assert.Equal(50000m, handover);
        Assert.Equal(5000000m, topUp);
        Assert.Equal(5050000m, effectiveOpening);
        Assert.Equal(0m, handoverDiff);

        var expectedClosing = CashierShiftService.ComputeExpectedClosingCash(
            openingCash: effectiveOpening,
            midShiftTopUpCash: 2000000m,
            cashIn: 800000m,
            cashOut: 259000m);

        Assert.Equal(7591000m, expectedClosing);
    }

    [Fact]
    public void CashierIndexView_SupportsHandoverTopUpVaultDropAndOddAmounts()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var cashierIndex = File.ReadAllText(Path.Combine(repoRoot, "src", "HIS.Web", "Areas", "Staff", "Views", "Cashier", "Index.cshtml"));

        Assert.DoesNotContain("name=\"openingCash\" value=\"10000000\" min=\"0\" step=\"100000\"", cashierIndex);
        Assert.Contains("name=\"handoverCash\"", cashierIndex);
        Assert.Contains("name=\"topUpCash\"", cashierIndex);
        Assert.Contains("name=\"vaultDropCash\"", cashierIndex);
        Assert.Contains("step=\"1\"", cashierIndex);
    }
}
