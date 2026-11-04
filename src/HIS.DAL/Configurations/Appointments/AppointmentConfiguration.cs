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
            t.HasCheckConstraint("ck_appointments_priority_level", "priority_level BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_appointments_slot_position", "slot_position >= 1");
            t.HasCheckConstraint("ck_appointments_overtaken_count", "overtaken_count BETWEEN 0 AND 3");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.AppointmentCode).HasMaxLength(30).IsRequired();
        builder.HasIndex(a => a.AppointmentCode).IsUnique();

        builder.Property(a => a.StatusId).IsRequired();
        builder.Property(a => a.BookingChannel).HasMaxLength(15).HasDefaultValue(BookingChannel.Web).IsRequired();
        builder.Property(a => a.VisitType).HasMaxLength(15).HasDefaultValue(VisitType.Scheduled).IsRequired();
        builder.Property(a => a.PriorityLevel).HasDefaultValue(0);
        builder.Property(a => a.SlotPosition);
        builder.Property(a => a.OvertakenCount).HasDefaultValue(0);
        builder.Property(a => a.QueueNumber).HasMaxLength(10);
        builder.Property(a => a.Reason).HasMaxLength(255);
        builder.Property(a => a.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(a => a.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasOne(a => a.Status)
            .WithMany(s => s.Appointments)
            .HasForeignKey(a => a.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Patient)
            .WithMany(p => p.Appointments)
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        // Người đặt lịch: bệnh nhân (qua web) hoặc nhân viên quầy (walk-in)
        builder.HasOne(a => a.BookedByAccount)
            .WithMany(u => u.BookedAppointments)
            .HasForeignKey(a => a.BookedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Doctor)
            .WithMany()
            .HasForeignKey(a => a.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Department)
            .WithMany()
            .HasForeignKey(a => a.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Slot)
            .WithMany(s => s.Appointments)
            .HasForeignKey(a => a.SlotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(a => a.Room)
            .WithMany(r => r.Appointments)
            .HasForeignKey(a => a.RoomId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(a => a.ParentAppointment)
            .WithMany(pa => pa.ChildAppointments)
            .HasForeignKey(a => a.ParentAppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.PatientId).HasDatabaseName("idx_appointments_patient");
        builder.HasIndex(a => a.StatusId).HasDatabaseName("idx_appointments_status_id");
        builder.HasIndex(a => new { a.BookedBy, a.StatusId }).HasDatabaseName("idx_appointments_booked_by");
        builder.HasIndex(a => a.SlotId).HasDatabaseName("idx_appointments_slot");

        // Job nhả giữ chỗ: chỉ quan tâm dòng PENDING (id 1)
        builder.HasIndex(a => a.HoldUntil)
            .HasDatabaseName("idx_appointments_hold")
            .HasFilter("status_id = 1");

        // Một bệnh nhân không giữ hai chỗ trùng nhau khi lịch còn hiệu lực (id 1..5 = PENDING..IN_PROGRESS)
        builder.HasIndex(a => new { a.PatientId, a.SlotId })
            .IsUnique()
            .HasDatabaseName("uq_patient_slot_active")
            .HasFilter("slot_id IS NOT NULL AND status_id IN (1,2,3,4,5)");

        // Hai lượt còn hiệu lực không được giữ cùng một vị trí trong cùng khung giờ
        builder.HasIndex(a => new { a.SlotId, a.SlotPosition })
            .IsUnique()
            .HasDatabaseName("uq_slot_position")
            .HasFilter("slot_position IS NOT NULL AND status_id IN (1,2,3,4,5)");
    }
}