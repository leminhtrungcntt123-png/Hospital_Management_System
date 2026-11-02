using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
{
    public void Configure(EntityTypeBuilder<Prescription> builder)
    {
        builder.ToTable("prescriptions");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.PrescribedDate).HasDefaultValueSql("CURRENT_DATE");
        builder.Property(e => e.Note).HasMaxLength(255);

        builder.HasOne(e => e.MedicalRecord)
            .WithMany(mr => mr.Prescriptions)
            .HasForeignKey(e => e.MedicalRecordId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
