using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class AppointmentStatusHistoryConfiguration : IEntityTypeConfiguration<AppointmentStatusHistory>
{
    public void Configure(EntityTypeBuilder<AppointmentStatusHistory> builder)
    {
        builder.ToTable("appointment_status_history");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ChangedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Appointment)
            .WithMany(a => a.StatusHistory)
            .HasForeignKey(e => e.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.OldStatus)
            .WithMany(s => s.OldStatusHistories)
            .HasForeignKey(e => e.OldStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.NewStatus)
            .WithMany(s => s.NewStatusHistories)
            .HasForeignKey(e => e.NewStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.PerformedByAccount)
            .WithMany(a => a.PerformedStatusHistories)
            .HasForeignKey(e => e.PerformedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
