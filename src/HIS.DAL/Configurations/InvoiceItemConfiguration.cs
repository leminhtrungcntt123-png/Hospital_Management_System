using HIS.DAL.Converters;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> builder)
    {
        builder.ToTable("invoice_items", t =>
        {
            t.HasCheckConstraint("ck_invoice_items_category", "category IN ('EXAMINATION','MEDICATION','SERVICE')");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Category).HasConversion<UpperSnakeCaseEnumConverter<InvoiceCategory>>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(255).IsRequired();
        builder.Property(e => e.UnitPrice).HasPrecision(18, 0).HasDefaultValue(0m);
        builder.Property(e => e.Quantity).HasDefaultValue(1);
        builder.Property(e => e.Amount).HasPrecision(18, 0).IsRequired();
        builder.Property(e => e.IsInsuranceCovered).HasDefaultValue(true);
        builder.Property(e => e.CoveragePctApplied).HasPrecision(5, 2).HasDefaultValue(0m);
        builder.Property(e => e.CoveredAmount).HasPrecision(18, 0).HasDefaultValue(0m);
        builder.Property(e => e.PatientAmount).HasPrecision(18, 0).HasDefaultValue(0m);

        builder.HasOne(e => e.Invoice)
            .WithMany(i => i.InvoiceItems)
            .HasForeignKey(e => e.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
