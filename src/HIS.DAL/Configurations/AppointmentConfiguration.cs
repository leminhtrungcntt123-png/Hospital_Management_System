using HIS.DAL.Converters;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments", t =>
        {
            t.HasCheckConstraint("ck_appointments_booking_channel", "booking_channel IN ('WEB','ON_SITE')");
            t.HasCheckConstraint("ck_appointments_visit_type", "visit_type IN ('SCHEDULED','WALK_IN')");
            t.HasCheckConstraint("ck_appointments_priority_level", "priority_level BETWEEN 0 AND 3");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.AppointmentCode).HasMaxLength(30).IsRequired();
        builder.HasIndex(e => e.AppointmentCode).IsUnique();

        builder.Property(e => e.BookingChannel).HasConversion<UpperSnakeCaseEnumConverter<BookingChannel>>().HasMaxLength(15).HasDefaultValue(BookingChannel.Web);
        builder.Property(e => e.VisitType).HasConversion<UpperSnakeCaseEnumConverter<VisitType>>().HasMaxLength(15).HasDefaultValue(VisitType.Scheduled);
        builder.Property(e => e.PriorityLevel).HasDefaultValue(0);
        builder.Property(e => e.QueueNumber).HasMaxLength(10);
        builder.Property(e => e.Reason).HasMaxLength(255);
        builder.Property(e => e.HoldUntil).HasColumnType("timestamp without time zone");
        builder.Property(e => e.CheckedInAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.CalledAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");
        builder.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Patient)
            .WithMany(p => p.Appointments)
            .HasForeignKey(e => e.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.BookedByAccount)
            .WithMany(a => a.BookedAppointments)
            .HasForeignKey(e => e.BookedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Slot)
            .WithMany(s => s.Appointments)
            .HasForeignKey(e => e.SlotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Doctor)
            .WithMany(d => d.Appointments)
            .HasForeignKey(e => e.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Department)
            .WithMany(d => d.Appointments)
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Room)
            .WithMany(r => r.Appointments)
            .HasForeignKey(e => e.RoomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Status)
            .WithMany(s => s.Appointments)
            .HasForeignKey(e => e.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ParentAppointment)
            .WithMany(a => a.ChildAppointments)
            .HasForeignKey(e => e.ParentAppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.PatientId).HasDatabaseName("idx_appointments_patient");
        builder.HasIndex(e => e.StatusId).HasDatabaseName("idx_appointments_status_id");
        builder.HasIndex(e => new { e.BookedBy, e.StatusId }).HasDatabaseName("idx_appointments_booked_by");
        builder.HasIndex(e => e.SlotId).HasDatabaseName("idx_appointments_slot");

        builder.HasIndex(e => e.HoldUntil)
            .HasFilter("status_id = 1")
            .HasDatabaseName("idx_appointments_hold");

        builder.HasIndex(e => new { e.PatientId, e.SlotId })
            .IsUnique()
            .HasFilter("slot_id IS NOT NULL AND status_id IN (1,2,3,4,5)")
            .HasDatabaseName("uq_patient_slot_active");
    }
}
