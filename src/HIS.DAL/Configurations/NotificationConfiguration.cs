using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title).HasMaxLength(150).IsRequired();
        builder.Property(e => e.IsRead).HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Account)
            .WithMany(a => a.Notifications)
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Appointment)
            .WithMany(a => a.Notifications)
            .HasForeignKey(e => e.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.AccountId, e.IsRead }).HasDatabaseName("idx_notifications_account");
    }
}
