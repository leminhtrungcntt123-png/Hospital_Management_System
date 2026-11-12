using HIS.DAL.Converters;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class DutyShiftConfiguration : IEntityTypeConfiguration<DutyShift>
{
    public void Configure(EntityTypeBuilder<DutyShift> builder)
    {
        builder.ToTable("duty_shifts", t =>
        {
            t.HasCheckConstraint("ck_duty_time", "end_time > start_time");
            t.HasCheckConstraint("ck_duty_shifts_status", "status IN ('BIDDING', 'APPROVED', 'REJECTED', 'CANCELLED')");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.StartTime).HasColumnType("timestamp without time zone").IsRequired();
        builder.Property(e => e.EndTime).HasColumnType("timestamp without time zone").IsRequired();
        builder.Property(e => e.Status)
            .HasConversion<UpperSnakeCaseEnumConverter<DutyShiftStatus>>()
            .HasMaxLength(20)
            .HasDefaultValue(DutyShiftStatus.Approved);
        builder.Property(e => e.Note).HasMaxLength(255);
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Period)
            .WithMany(p => p.DutyShifts)
            .HasForeignKey(e => e.PeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Account)
            .WithMany(a => a.DutyShifts)
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Department)
            .WithMany(d => d.DutyShifts)
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.AssignedByAccount)
            .WithMany(a => a.AssignedDutyShifts)
            .HasForeignKey(e => e.AssignedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.AccountId, e.StartTime }).HasDatabaseName("idx_duty_shifts_account");
    }
}
