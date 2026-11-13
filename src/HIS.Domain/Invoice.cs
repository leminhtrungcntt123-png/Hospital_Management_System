namespace HIS.Domain;

public class Invoice
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public int? InsurancePlanId { get; set; }
    public int? InsuranceCardId { get; set; }
    public decimal? CoveragePctApplied { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal InsuranceCoveredAmount { get; set; }
    public decimal PatientAmount { get; set; }
    /// <summary>Số hóa đơn dạng HD-000001, sinh bằng sequence DB.</summary>
    public string InvoiceNumber { get; set; } = $"HD-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
    /// <summary>Ngày khám - mốc kiểm tra hạn thẻ BHYT (không dùng ngày thanh toán).</summary>
    public DateOnly? ExamDate { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;
    public DateTime CreatedAt { get; set; }

    public Appointment Appointment { get; set; } = null!;
    public Patient Patient { get; set; } = null!;
    public InsurancePlan? InsurancePlan { get; set; }
    public InsuranceCard? InsuranceCard { get; set; }
    public ICollection<Payment> Payments { get; set; } = [];
    public ICollection<InvoiceItem> InvoiceItems { get; set; } = [];
}
