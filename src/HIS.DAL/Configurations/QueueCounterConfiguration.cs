using HIS.DAL.Converters;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class QueueCounterConfiguration : IEntityTypeConfiguration<QueueCounter>
{
    public void Configure(EntityTypeBuilder<QueueCounter> builder)
    {
        builder.ToTable("queue_counters", t =>
        {
            t.HasCheckConstraint("ck_queue_counters_shift", "shift IN ('MORNING','AFTERNOON')");
            t.HasCheckConstraint("ck_queue_counters_channel", "channel IN ('WEB','ON_SITE')");
            t.HasCheckConstraint("ck_queue_counters_last_number", "last_number >= 0");
        });

        builder.HasKey(e => new { e.QueueDate, e.RoomId, e.Shift, e.Channel });

        builder.Property(e => e.Shift).HasConversion<UpperSnakeCaseEnumConverter<ExamShift>>().HasMaxLength(10);
        builder.Property(e => e.Channel).HasConversion<UpperSnakeCaseEnumConverter<BookingChannel>>().HasMaxLength(15);
        builder.Property(e => e.LastNumber).HasDefaultValue(0);

        builder.HasOne(e => e.Room)
            .WithMany(r => r.QueueCounters)
            .HasForeignKey(e => e.RoomId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
