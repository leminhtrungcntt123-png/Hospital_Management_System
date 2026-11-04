using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", t =>
        {
            t.HasCheckConstraint("ck_invoices_status", "status IN ('UNPAID','PARTIALLY_PAID','PAID')");
            t.HasCheckConstraint("ck_invoices_coverage_pct_applied", "coverage_pct_applied IS NULL OR coverage_pct_applied BETWEEN 0 AND 100");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.CoveragePctApplied).HasPrecision(5, 2);
        builder.Property(i => i.TotalAmount).HasPrecision(12, 2).HasDefaultValue(0m);
        builder.Property(i => i.InsuranceCoveredAmount).HasPrecision(12, 2).HasDefaultValue(0m);
        builder.Property(i => i.PatientAmount).HasPrecision(12, 2).HasDefaultValue(0m);
        builder.Property(i => i.Status).HasMaxLength(20).HasDefaultValue(InvoiceStatus.Unpaid).IsRequired();
        builder.Property(i => i.CreatedAt).HasDefaultValueSql("now()");

        builder.HasOne(i => i.Appointment)
            .WithOne(a => a.Invoice)
            .HasForeignKey<Invoice>(i => i.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.AppointmentId).IsUnique();

        builder.HasOne(i => i.Patient)
            .WithMany(p => p.Invoices)
            .HasForeignKey(i => i.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.InsurancePlan)
            .WithMany(ip => ip.Invoices)
            .HasForeignKey(i => i.InsurancePlanId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(i => i.InsuranceCard)
            .WithMany(ic => ic.Invoices)
            .HasForeignKey(i => i.InsuranceCardId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}