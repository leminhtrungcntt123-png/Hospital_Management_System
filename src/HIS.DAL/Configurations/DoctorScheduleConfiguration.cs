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
            t.HasCheckConstraint("ck_doctor_schedules_day_of_week", "day_of_week BETWEEN 0 AND 6");
            t.HasCheckConstraint("ck_doctor_schedules_shift", "shift IN ('MORNING','AFTERNOON')");
            t.HasCheckConstraint("ck_online_slots", "online_slots BETWEEN 0 AND max_patients");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.DayOfWeek).HasConversion<int>().IsRequired();
        builder.Property(e => e.Shift).HasConversion<UpperSnakeCaseEnumConverter<ExamShift>>().HasMaxLength(10).IsRequired();
        builder.Property(e => e.StartTime).IsRequired();
        builder.Property(e => e.EndTime).IsRequired();
        builder.Property(e => e.SlotDurationMinutes).HasDefaultValue(15);
        builder.Property(e => e.MaxPatients).HasDefaultValue(40);
        builder.Property(e => e.OnlineSlots).HasDefaultValue(28);
        builder.Property(e => e.EffectiveFrom).IsRequired();
        builder.Property(e => e.IsActive).HasDefaultValue(true);

        builder.HasOne(e => e.Doctor)
            .WithMany(d => d.Schedules)
            .HasForeignKey(e => e.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Room)
            .WithMany(r => r.Schedules)
            .HasForeignKey(e => e.RoomId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
