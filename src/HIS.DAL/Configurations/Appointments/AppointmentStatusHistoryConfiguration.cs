using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class AppointmentStatusHistoryConfiguration : IEntityTypeConfiguration<AppointmentStatusHistory>
{
    public void Configure(EntityTypeBuilder<AppointmentStatusHistory> builder)
    {
        builder.ToTable("appointment_status_history");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.OldStatusId);
        builder.Property(h => h.NewStatusId).IsRequired();
        builder.Property(h => h.ChangedAt).HasDefaultValueSql("now()");
        builder.Property(h => h.IsLateCancel).HasDefaultValue(false);

        // Đếm hạn mức hủy sát giờ theo tài khoản trong tháng dương lịch
        builder.HasIndex(h => new { h.PerformedBy, h.ChangedAt })
            .HasDatabaseName("idx_history_cancel")
            .HasFilter("is_late_cancel");

        builder.HasOne(h => h.OldStatus)
            .WithMany(s => s.OldStatusHistories)
            .HasForeignKey(h => h.OldStatusId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(h => h.NewStatus)
            .WithMany(s => s.NewStatusHistories)
            .HasForeignKey(h => h.NewStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.Appointment)
            .WithMany(a => a.StatusHistories)
            .HasForeignKey(h => h.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(h => h.Performer)
            .WithMany(a => a.PerformedAppointmentHistories)
            .HasForeignKey(h => h.PerformedBy)
            .OnDelete(DeleteBehavior.SetNull);
    }
}