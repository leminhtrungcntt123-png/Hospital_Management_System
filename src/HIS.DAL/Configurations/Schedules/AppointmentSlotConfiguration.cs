using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class AppointmentSlotConfiguration : IEntityTypeConfiguration<AppointmentSlot>
{
    public void Configure(EntityTypeBuilder<AppointmentSlot> builder)
    {
        builder.ToTable("appointment_slots", t =>
        {
            t.HasCheckConstraint("ck_appointment_slots_status", "status IN ('AVAILABLE','FULL','BLOCKED')");
            t.HasCheckConstraint("ck_appointment_slots_capacity", "booked_count <= capacity");
            t.HasCheckConstraint("ck_online_quota", "online_quota <= capacity");
            t.HasCheckConstraint("ck_online_booked", "online_booked <= online_quota AND online_booked <= booked_count");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Capacity).HasDefaultValue(1);
        builder.Property(s => s.BookedCount).HasDefaultValue(0);
        builder.Property(s => s.OnlineQuota).HasDefaultValue(3);
        builder.Property(s => s.OnlineBooked).HasDefaultValue(0);
        builder.Property(s => s.Status).HasMaxLength(15).HasDefaultValue(SlotStatus.Available).IsRequired();
        builder.Property(s => s.Version).HasDefaultValue(0).IsConcurrencyToken();

        builder.HasOne(s => s.Doctor)
            .WithMany(d => d.AppointmentSlots)
            .HasForeignKey(s => s.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Room)
            .WithMany(r => r.AppointmentSlots)
            .HasForeignKey(s => s.RoomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.DoctorId, s.StartTime })
            .IsUnique()
            .HasDatabaseName("uq_slot");
    }
}