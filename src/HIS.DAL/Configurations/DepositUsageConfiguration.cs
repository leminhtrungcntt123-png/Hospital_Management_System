using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class DepositUsageConfiguration : IEntityTypeConfiguration<DepositUsage>
{
    public void Configure(EntityTypeBuilder<DepositUsage> builder)
    {
        builder.ToTable("deposit_usages", t =>
        {
            t.HasCheckConstraint("ck_deposit_usages_amount", "amount > 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Amount).HasPrecision(18, 0).IsRequired();
        builder.Property(e => e.UsedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Deposit)
            .WithMany(d => d.Usages)
            .HasForeignKey(e => e.DepositId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Invoice)
            .WithMany()
            .HasForeignKey(e => e.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Cashier)
            .WithMany()
            .HasForeignKey(e => e.CashierId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.DepositId).HasDatabaseName("idx_deposit_usages_deposit");
        builder.HasIndex(e => e.InvoiceId).HasDatabaseName("idx_deposit_usages_invoice");
    }
}

