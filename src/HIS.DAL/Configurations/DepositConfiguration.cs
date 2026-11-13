using HIS.DAL.Converters;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class DepositConfiguration : IEntityTypeConfiguration<Deposit>
{
    public void Configure(EntityTypeBuilder<Deposit> builder)
    {
        builder.ToTable("deposits", t =>
        {
            t.HasCheckConstraint("ck_deposits_amount", "amount > 0");
            t.HasCheckConstraint("ck_deposits_used_amount", "used_amount >= 0");
            t.HasCheckConstraint("ck_deposits_refunded_amount", "refunded_amount >= 0");
            t.HasCheckConstraint("ck_deposits_status", "status IN ('ACTIVE','DEPLETED','REFUNDED')");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.DepositCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(e => e.DepositCode).IsUnique();

        builder.Property(e => e.Amount).HasPrecision(18, 0).IsRequired();
        builder.Property(e => e.UsedAmount).HasPrecision(18, 0).HasDefaultValue(0);
        builder.Property(e => e.RefundedAmount).HasPrecision(18, 0).HasDefaultValue(0);

        builder.Property(e => e.Status)
            .HasConversion<UpperSnakeCaseEnumConverter<DepositStatus>>()
            .HasMaxLength(20)
            .HasDefaultValue(DepositStatus.Active);

        builder.Property(e => e.PaymentMethod)
            .HasConversion<UpperSnakeCaseEnumConverter<PaymentMethod>>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.ReferenceCode).HasMaxLength(100);
        builder.Property(e => e.Purpose).HasMaxLength(255);
        builder.Property(e => e.ApprovedBy).HasMaxLength(150);
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Patient)
            .WithMany(p => p.Deposits)
            .HasForeignKey(e => e.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Appointment)
            .WithMany()
            .HasForeignKey(e => e.AppointmentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Cashier)
            .WithMany()
            .HasForeignKey(e => e.CashierId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Shift)
            .WithMany()
            .HasForeignKey(e => e.ShiftId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.PatientId).HasDatabaseName("idx_deposits_patient");
        builder.HasIndex(e => e.AppointmentId).HasDatabaseName("idx_deposits_appointment");
    }
}

