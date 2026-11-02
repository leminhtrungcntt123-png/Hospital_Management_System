using HIS.DAL.Converters;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class ScheduleExceptionConfiguration : IEntityTypeConfiguration<ScheduleException>
{
    public void Configure(EntityTypeBuilder<ScheduleException> builder)
    {
        builder.ToTable("schedule_exceptions", t =>
        {
            t.HasCheckConstraint("ck_schedule_exceptions_shift", "shift IN ('MORNING','AFTERNOON')");
            t.HasCheckConstraint("ck_schedule_exceptions_type", "type IN ('LEAVE','TRAINING','SURGERY','SHIFT_SWAP')");
            t.HasCheckConstraint("ck_schedule_exceptions_status", "status IN ('PENDING','APPROVED','REJECTED')");
            t.HasCheckConstraint("ck_exception_replacement", "replacement_doctor_id IS NULL OR replacement_doctor_id <> doctor_id");
            t.HasCheckConstraint("ck_exception_approved", "status <> 'APPROVED' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL)");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Date).IsRequired();
        builder.Property(e => e.Shift).HasConversion<UpperSnakeCaseEnumConverter<ExamShift>>().HasMaxLength(10);
        builder.Property(e => e.Type).HasConversion<UpperSnakeCaseEnumConverter<ScheduleExceptionType>>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Status).HasConversion<UpperSnakeCaseEnumConverter<ScheduleExceptionStatus>>().HasMaxLength(10).HasDefaultValue(ScheduleExceptionStatus.Pending);
        builder.Property(e => e.Reason).HasMaxLength(255);
        builder.Property(e => e.ApprovedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Doctor)
            .WithMany(d => d.ScheduleExceptions)
            .HasForeignKey(e => e.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ReplacementDoctor)
            .WithMany(d => d.ReplacementScheduleExceptions)
            .HasForeignKey(e => e.ReplacementDoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ApprovedByAccount)
            .WithMany(a => a.ApprovedScheduleExceptions)
            .HasForeignKey(e => e.ApprovedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
