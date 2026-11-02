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
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;
    public DateTime CreatedAt { get; set; }

    public Appointment Appointment { get; set; } = null!;
    public Patient Patient { get; set; } = null!;
    public InsurancePlan? InsurancePlan { get; set; }
    public InsuranceCard? InsuranceCard { get; set; }
    public ICollection<Payment> Payments { get; set; } = [];
    public ICollection<InvoiceItem> InvoiceItems { get; set; } = [];
}
