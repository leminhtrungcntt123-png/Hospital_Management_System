namespace HIS.Domain;

public class InvoiceItem
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public InvoiceCategory Category { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>Thành tiền của dòng (UnitPrice x Quantity).</summary>
    public decimal Amount { get; set; }
    public int? ServiceId { get; set; }
    public int? MedicationId { get; set; }
    /// <summary>Đơn giá chụp tại thời điểm lập hóa đơn; đổi bảng giá sau này không ảnh hưởng.</summary>
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; } = 1;
    public bool IsInsuranceCovered { get; set; }
    /// <summary>Tỉ lệ BHYT thực áp cho riêng dòng này (0 nếu dòng không được hưởng).</summary>
    public decimal CoveragePctApplied { get; set; }
    public decimal CoveredAmount { get; set; }
    public decimal PatientAmount { get; set; }

    public Invoice Invoice { get; set; } = null!;
}
