using HIS.DAL.Converters;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class SchedulingPeriodConfiguration : IEntityTypeConfiguration<SchedulingPeriod>
{
    public void Configure(EntityTypeBuilder<SchedulingPeriod> builder)
    {
        builder.ToTable("scheduling_periods", t =>
        {
            t.HasCheckConstraint("ck_period_dates", "end_date >= start_date AND bidding_closes_at > bidding_opens_at");
            t.HasCheckConstraint("ck_scheduling_periods_status", "status IN ('DRAFT','OPEN','REVIEWING','PUBLISHED','CLOSED')");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.StartDate).IsRequired();
        builder.Property(e => e.EndDate).IsRequired();
        builder.Property(e => e.BiddingOpensAt).HasColumnType("timestamp without time zone").IsRequired();
        builder.Property(e => e.BiddingClosesAt).HasColumnType("timestamp without time zone").IsRequired();
        builder.Property(e => e.Status)
            .HasConversion<UpperSnakeCaseEnumConverter<SchedulingPeriodStatus>>()
            .HasMaxLength(20)
            .HasDefaultValue(SchedulingPeriodStatus.Open);
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Department)
            .WithMany(d => d.SchedulingPeriods)
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.CreatedByAccount)
            .WithMany(a => a.CreatedSchedulingPeriods)
            .HasForeignKey(e => e.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

