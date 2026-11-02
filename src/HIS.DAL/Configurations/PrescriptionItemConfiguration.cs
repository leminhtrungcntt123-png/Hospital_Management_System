using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class PrescriptionItemConfiguration : IEntityTypeConfiguration<PrescriptionItem>
{
    public void Configure(EntityTypeBuilder<PrescriptionItem> builder)
    {
        builder.ToTable("prescription_items");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Dosage).HasMaxLength(100);
        builder.Property(e => e.Quantity).IsRequired();
        builder.Property(e => e.UsageInstructions).HasMaxLength(255);

        builder.HasOne(e => e.Prescription)
            .WithMany(p => p.PrescriptionItems)
            .HasForeignKey(e => e.PrescriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Medication)
            .WithMany(m => m.PrescriptionItems)
            .HasForeignKey(e => e.MedicationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
