using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class LabResultConfiguration : IEntityTypeConfiguration<LabResult>
{
    public void Configure(EntityTypeBuilder<LabResult> builder)
    {
        builder.ToTable("lab_results");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.RecordedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Order)
            .WithOne(o => o.LabResult)
            .HasForeignKey<LabResult>(e => e.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.PerformedByAccount)
            .WithMany(a => a.PerformedLabResults)
            .HasForeignKey(e => e.PerformedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
