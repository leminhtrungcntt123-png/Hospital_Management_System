using HIS.DAL.Converters;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class DoctorScheduleConfiguration : IEntityTypeConfiguration<DoctorSchedule>
{
    public void Configure(EntityTypeBuilder<DoctorSchedule> builder)
    {
        builder.ToTable("doctor_schedules", t =>
        {
            t.HasCheckConstraint("ck_doctor_schedules_shift", "shift IN ('MORNING','AFTERNOON')");
            t.HasCheckConstraint("ck_doctor_schedules_status", "status IN ('BIDDING','APPROVED','REJECTED','REALLOCATED','CANCELLED')");
            t.HasCheckConstraint("ck_online_slots", "online_slots BETWEEN 0 AND max_patients");
            t.HasCheckConstraint("ck_schedule_time", "end_time > start_time");
            t.HasCheckConstraint("ck_approved_requires_room", "status <> 'APPROVED' OR room_id IS NOT NULL");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.WorkDate).IsRequired();
        builder.Property(e => e.Shift).HasConversion<UpperSnakeCaseEnumConverter<ExamShift>>().HasMaxLength(10).IsRequired();
        builder.Property(e => e.StartTime).HasDefaultValue(new TimeOnly(7, 30));
        builder.Property(e => e.EndTime).HasDefaultValue(new TimeOnly(11, 30));
        builder.Property(e => e.SlotDurationMinutes).HasDefaultValue(30);
        builder.Property(e => e.MaxPatients).HasDefaultValue(40);
        builder.Property(e => e.OnlineSlots).HasDefaultValue(28);
        builder.Property(e => e.IsHotShift).HasDefaultValue(false);
        builder.Property(e => e.Status)
            .HasConversion<UpperSnakeCaseEnumConverter<DoctorScheduleStatus>>()
            .HasMaxLength(20)
            .HasDefaultValue(DoctorScheduleStatus.Bidding);
        builder.Property(e => e.DoctorNote).HasMaxLength(255);
        builder.Property(e => e.ReviewedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Period)
            .WithMany(p => p.Schedules)
            .HasForeignKey(e => e.PeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Doctor)
            .WithMany(d => d.Schedules)
            .HasForeignKey(e => e.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Department)
            .WithMany(d => d.Schedules)
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Room)
            .WithMany(r => r.Schedules)
            .HasForeignKey(e => e.RoomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ReallocatedFrom)
            .WithMany(s => s.ReallocatedSchedules)
            .HasForeignKey(e => e.ReallocatedFromId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ReviewedByAccount)
            .WithMany(a => a.ReviewedSchedules)
            .HasForeignKey(e => e.ReviewedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique indexes
        builder.HasIndex(e => new { e.DoctorId, e.WorkDate, e.Shift })
            .HasDatabaseName("uq_doctor_active_shift")
            .IsUnique()
            .HasFilter("status IN ('BIDDING', 'APPROVED')");

        builder.HasIndex(e => new { e.RoomId, e.WorkDate, e.Shift })
            .HasDatabaseName("uq_room_collision_guard")
            .IsUnique()
            .HasFilter("status = 'APPROVED'");

        // Query performance indexes
        builder.HasIndex(e => new { e.DepartmentId, e.WorkDate, e.Shift, e.Status })
            .HasDatabaseName("idx_schedules_demand_grid");

        builder.HasIndex(e => new { e.DoctorId, e.WorkDate, e.IsHotShift, e.Status })
            .HasDatabaseName("idx_schedules_doctor_rotation");
    }
}
