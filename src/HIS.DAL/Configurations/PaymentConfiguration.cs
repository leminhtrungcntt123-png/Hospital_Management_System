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
            t.HasCheckConstraint("ck_payments_payment_method", "payment_method IN ('CASH','BANK_TRANSFER','DEPOSIT_DEDUCTION')");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.TransactionCode)
            .HasMaxLength(30)
            .HasDefaultValueSql("'PT-' || substr(md5(random()::text || clock_timestamp()::text), 1, 10)")
            .IsRequired();
        builder.HasIndex(e => e.TransactionCode).IsUnique();
        builder.Property(e => e.ReferenceCode).HasMaxLength(100);
        builder.Property(e => e.Status)
            .HasConversion<UpperSnakeCaseEnumConverter<PaymentStatus>>()
            .HasMaxLength(20)
            .HasDefaultValue(PaymentStatus.Confirmed);
        builder.Property(e => e.IdempotencyKey).HasMaxLength(100);
        builder.HasIndex(e => e.IdempotencyKey).IsUnique().HasFilter("idempotency_key IS NOT NULL");

        builder.Property(e => e.Amount).HasPrecision(18, 0).IsRequired();
        builder.Property(e => e.PaymentType).HasConversion<UpperSnakeCaseEnumConverter<PaymentType>>().HasMaxLength(15);
        builder.Property(e => e.PaymentMethod).HasConversion<UpperSnakeCaseEnumConverter<PaymentMethod>>().HasMaxLength(25).IsRequired();
        builder.Property(e => e.ReceiptEmail).HasMaxLength(100);
        builder.Property(e => e.Note).HasMaxLength(255);
        builder.Property(e => e.RefundReason).HasMaxLength(500);
        builder.Property(e => e.PaidAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Invoice)
            .WithMany(i => i.Payments)
            .HasForeignKey(e => e.InvoiceId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Deposit)
            .WithMany()
            .HasForeignKey(e => e.DepositId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.OriginalPayment)
            .WithMany()
            .HasForeignKey(e => e.OriginalPaymentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Cashier)
            .WithMany(a => a.CashierPayments)
            .HasForeignKey(e => e.CashierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Shift)
            .WithMany(s => s.Payments)
            .HasForeignKey(e => e.ShiftId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.InvoiceId).HasDatabaseName("idx_payments_invoice");
        builder.HasIndex(e => e.DepositId).HasDatabaseName("idx_payments_deposit");
    }
}
