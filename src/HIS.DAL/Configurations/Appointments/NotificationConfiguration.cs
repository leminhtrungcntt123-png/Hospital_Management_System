using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Title).HasMaxLength(150).IsRequired();
        builder.Property(n => n.Content).HasColumnType("text");
        builder.Property(n => n.IsRead).HasDefaultValue(false).IsRequired();
        builder.Property(n => n.CreatedAt).HasDefaultValueSql("now()");

        // Thông báo luôn gửi về TÀI KHOẢN ĐẶT LỊCH (booked_by), không gửi thẳng cho bệnh nhân
        builder.HasOne(n => n.Account)
            .WithMany(a => a.Notifications)
            .HasForeignKey(n => n.AccountId);

        builder.HasOne(n => n.Appointment)
            .WithMany()
            .HasForeignKey(n => n.AppointmentId);

        builder.HasIndex(n => new { n.AccountId, n.IsRead }).HasDatabaseName("idx_notifications_account");
    }
}
