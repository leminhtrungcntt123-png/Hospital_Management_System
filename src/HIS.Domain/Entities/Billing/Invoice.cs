namespace HIS.Domain;

public class Invoice
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public int? InsurancePlanId { get; set; }
    public InsurancePlan? InsurancePlan { get; set; }
    public int? InsuranceCardId { get; set; }
    public InsuranceCard? InsuranceCard { get; set; }

    // Bản chụp tỉ lệ BHYT tại thời điểm tính tiền (BHYT chỉ áp dụng ở bước thanh toán)
    public decimal? CoveragePctApplied { get; set; }
    public decimal TotalAmount { get; set; } = 0;
    public decimal InsuranceCoveredAmount { get; set; } = 0;
    public decimal PatientAmount { get; set; } = 0;
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public ICollection<InvoiceItem> InvoiceItems { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
}
