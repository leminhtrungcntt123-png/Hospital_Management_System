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
            // 0 = Chủ nhật ... 6 = Thứ bảy, khớp System.DayOfWeek
            t.HasCheckConstraint("ck_doctor_schedules_day_of_week", "day_of_week BETWEEN 0 AND 6");
            t.HasCheckConstraint("ck_doctor_schedules_shift", "shift IN ('MORNING','AFTERNOON')");
            t.HasCheckConstraint("ck_doctor_schedules_online_ratio", "online_ratio_pct BETWEEN 0 AND 100");
        });

        builder.HasKey(ds => ds.Id);
        builder.Property(ds => ds.DayOfWeek).IsRequired();
        builder.Property(ds => ds.Shift).HasMaxLength(10).IsRequired();
        builder.Property(ds => ds.StartTime).IsRequired();
        builder.Property(ds => ds.EndTime).IsRequired();
        builder.Property(ds => ds.SlotDurationMinutes).HasDefaultValue(60);
        builder.Property(ds => ds.OnlineRatioPct).HasDefaultValue(60);
        builder.Property(ds => ds.EffectiveFrom).IsRequired();
        builder.Property(ds => ds.IsActive).HasDefaultValue(true);

        builder.HasOne(ds => ds.Doctor)
            .WithMany(d => d.DoctorSchedules)
            .HasForeignKey(ds => ds.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ds => ds.Room)
            .WithMany(r => r.DoctorSchedules)
            .HasForeignKey(ds => ds.RoomId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}