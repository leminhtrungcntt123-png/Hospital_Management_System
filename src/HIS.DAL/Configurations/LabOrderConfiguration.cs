using HIS.DAL.Converters;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class LabOrderConfiguration : IEntityTypeConfiguration<LabOrder>
{
    public void Configure(EntityTypeBuilder<LabOrder> builder)
    {
        builder.ToTable("lab_orders", t =>
        {
            t.HasCheckConstraint("ck_lab_orders_status", "status IN ('PENDING','COMPLETED')");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Note).HasMaxLength(255);
        builder.Property(e => e.Status).HasConversion<UpperSnakeCaseEnumConverter<LabOrderStatus>>().HasMaxLength(20).HasDefaultValue(LabOrderStatus.Pending);
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.MedicalRecord)
            .WithMany(mr => mr.LabOrders)
            .HasForeignKey(e => e.MedicalRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Service)
            .WithMany(s => s.LabOrders)
            .HasForeignKey(e => e.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
