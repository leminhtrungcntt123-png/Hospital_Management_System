using HIS.DAL.Converters;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class CashierShiftConfiguration : IEntityTypeConfiguration<CashierShift>
{
    public void Configure(EntityTypeBuilder<CashierShift> builder)
    {
        builder.ToTable("cashier_shifts");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.OpenedAt).HasColumnType("timestamp without time zone").IsRequired();
        builder.Property(e => e.ClosedAt).HasColumnType("timestamp without time zone");

        builder.Property(e => e.OpeningCash).HasPrecision(18, 0).HasDefaultValue(0m);
        builder.Property(e => e.ExpectedCash).HasPrecision(18, 0);
        builder.Property(e => e.CountedCash).HasPrecision(18, 0);
        builder.Property(e => e.Difference).HasPrecision(18, 0);

        builder.Property(e => e.Status)
            .HasConversion<UpperSnakeCaseEnumConverter<ShiftStatus>>()
            .HasMaxLength(20)
            .HasDefaultValue(ShiftStatus.Open);

        builder.Property(e => e.Note).HasMaxLength(255);

        builder.HasOne(e => e.Cashier)
            .WithMany()
            .HasForeignKey(e => e.CashierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.CashierId, e.Status }).HasDatabaseName("idx_cashier_shifts_cashier_status");
    }
}

