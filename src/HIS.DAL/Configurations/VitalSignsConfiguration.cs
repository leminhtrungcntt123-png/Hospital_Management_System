using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class VitalSignsConfiguration : IEntityTypeConfiguration<VitalSigns>
{
    public void Configure(EntityTypeBuilder<VitalSigns> builder)
    {
        builder.ToTable("vital_signs");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Temperature).HasPrecision(4, 1);
        builder.Property(e => e.Weight).HasPrecision(5, 2);
        builder.Property(e => e.Height).HasPrecision(5, 2);
        builder.Property(e => e.RecordedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Appointment)
            .WithOne(a => a.VitalSigns)
            .HasForeignKey<VitalSigns>(e => e.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.RecordedByNurse)
            .WithMany(n => n.RecordedVitalSigns)
            .HasForeignKey(e => e.RecordedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
