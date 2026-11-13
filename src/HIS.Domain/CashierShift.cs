namespace HIS.Domain;

/// <summary>Ca thu ngân: mở ca với tiền đầu ca, kết ca khi đối chiếu tiền mặt thực đếm.</summary>
public class CashierShift
{
    public int Id { get; set; }
    public int CashierId { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningCash { get; set; }
    /// <summary>Tiền mặt dự kiến khi kết ca = tiền đầu ca + thu tiền mặt - hoàn tiền mặt.</summary>
    public decimal? ExpectedCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? Difference { get; set; }
    public ShiftStatus Status { get; set; } = ShiftStatus.Open;
    public string? Note { get; set; }

    public Account Cashier { get; set; } = null!;
    public ICollection<Payment> Payments { get; set; } = [];
}