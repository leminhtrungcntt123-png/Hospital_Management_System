using HIS.DAL.Converters;
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
            t.HasCheckConstraint("ck_invoices_coverage_pct_applied", "coverage_pct_applied BETWEEN 0 AND 100");
            t.HasCheckConstraint("ck_invoices_status", "status IN ('UNPAID','PARTIALLY_PAID','PAID')");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.InvoiceNumber)
            .HasMaxLength(20)
            .HasDefaultValueSql("'HD-' || substr(md5(random()::text || clock_timestamp()::text), 1, 10)")
            .IsRequired();
        builder.HasIndex(e => e.InvoiceNumber).IsUnique();
        builder.Property(e => e.ExamDate);

        builder.Property(e => e.CoveragePctApplied).HasPrecision(5, 2);
        builder.Property(e => e.TotalAmount).HasPrecision(18, 0).HasDefaultValue(0m);
        builder.Property(e => e.InsuranceCoveredAmount).HasPrecision(18, 0).HasDefaultValue(0m);
        builder.Property(e => e.PatientAmount).HasPrecision(18, 0).HasDefaultValue(0m);
        builder.Property(e => e.Status).HasConversion<UpperSnakeCaseEnumConverter<InvoiceStatus>>().HasMaxLength(20).HasDefaultValue(InvoiceStatus.Unpaid);
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Appointment)
            .WithOne(a => a.Invoice)
            .HasForeignKey<Invoice>(e => e.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Patient)
            .WithMany(p => p.Invoices)
            .HasForeignKey(e => e.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.InsurancePlan)
            .WithMany(ip => ip.Invoices)
            .HasForeignKey(e => e.InsurancePlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.InsuranceCard)
            .WithMany(ic => ic.Invoices)
            .HasForeignKey(e => e.InsuranceCardId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
