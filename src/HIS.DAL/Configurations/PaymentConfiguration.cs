using HIS.DAL.Converters;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", t =>
        {
            t.HasCheckConstraint("ck_payments_amount", "amount > 0");
            t.HasCheckConstraint("ck_payments_payment_type", "payment_type IN ('DEPOSIT','PAYMENT','REFUND')");
            t.HasCheckConstraint("ck_payments_payment_method", "payment_method IN ('CASH','BANK_TRANSFER')");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Amount).HasPrecision(12, 2).IsRequired();
        builder.Property(e => e.PaymentType).HasConversion<UpperSnakeCaseEnumConverter<PaymentType>>().HasMaxLength(15).HasDefaultValue(PaymentType.Payment);
        builder.Property(e => e.PaymentMethod).HasConversion<UpperSnakeCaseEnumConverter<PaymentMethod>>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.ReceiptEmail).HasMaxLength(100);
        builder.Property(e => e.Note).HasMaxLength(255);
        builder.Property(e => e.PaidAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Invoice)
            .WithMany(i => i.Payments)
            .HasForeignKey(e => e.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Cashier)
            .WithMany(a => a.CashierPayments)
            .HasForeignKey(e => e.CashierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.InvoiceId).HasDatabaseName("idx_payments_invoice");
    }
}
