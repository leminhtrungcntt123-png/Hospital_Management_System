using HIS.DAL.Converters;
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
            t.HasCheckConstraint("ck_capacity", "booked_count <= capacity");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.StartTime).HasColumnType("timestamp without time zone").IsRequired();
        builder.Property(e => e.EndTime).HasColumnType("timestamp without time zone").IsRequired();
        builder.Property(e => e.Capacity).HasDefaultValue(1);
        builder.Property(e => e.BookedCount).HasDefaultValue(0);
        builder.Property(e => e.Status).HasConversion<UpperSnakeCaseEnumConverter<SlotStatus>>().HasMaxLength(15).HasDefaultValue(SlotStatus.Available);
        builder.Property(e => e.Version).IsConcurrencyToken().HasDefaultValue(0);

        builder.HasOne(e => e.Doctor)
            .WithMany(d => d.AppointmentSlots)
            .HasForeignKey(e => e.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Room)
            .WithMany(r => r.AppointmentSlots)
            .HasForeignKey(e => e.RoomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.DoctorId, e.StartTime }).IsUnique().HasDatabaseName("uq_slot");
        builder.HasIndex(e => new { e.DoctorId, e.StartTime }).HasDatabaseName("idx_slots_doctor_date");
    }
}
