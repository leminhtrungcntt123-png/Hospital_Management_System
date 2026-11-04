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
            t.HasCheckConstraint("ck_schedule_exceptions_shift", "shift IS NULL OR shift IN ('MORNING','AFTERNOON')");
            t.HasCheckConstraint("ck_schedule_exceptions_type", "type IN ('LEAVE','TRAINING','SURGERY','SHIFT_SWAP')");
            t.HasCheckConstraint("ck_schedule_exceptions_status", "status IN ('PENDING','APPROVED','REJECTED')");
            t.HasCheckConstraint("ck_exception_replacement", "replacement_doctor_id IS NULL OR replacement_doctor_id <> doctor_id");
            t.HasCheckConstraint("ck_exception_approved", "status <> 'APPROVED' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL)");
        });

        builder.HasKey(se => se.Id);
        builder.Property(se => se.Date).IsRequired();
        builder.Property(se => se.Shift).HasMaxLength(10);
        builder.Property(se => se.Type).HasMaxLength(20).IsRequired();
        builder.Property(se => se.Status).HasMaxLength(10).HasDefaultValue(ScheduleExceptionStatus.Pending).IsRequired();
        builder.Property(se => se.Reason).HasMaxLength(255);
        builder.Property(se => se.CreatedAt).HasDefaultValueSql("now()");

        builder.HasOne(se => se.Doctor)
            .WithMany(d => d.ScheduleExceptions)
            .HasForeignKey(se => se.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(se => se.ReplacementDoctor)
            .WithMany(d => d.ReplacementScheduleExceptions)
            .HasForeignKey(se => se.ReplacementDoctorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(se => se.Approver)
            .WithMany(a => a.ApprovedScheduleExceptions)
            .HasForeignKey(se => se.ApprovedBy)
            .OnDelete(DeleteBehavior.SetNull);
    }
}