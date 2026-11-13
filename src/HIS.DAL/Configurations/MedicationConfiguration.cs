using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class MedicationConfiguration : IEntityTypeConfiguration<Medication>
{
    public void Configure(EntityTypeBuilder<Medication> builder)
    {
        builder.ToTable("medications");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).HasMaxLength(150).IsRequired();
        builder.Property(e => e.ActiveIngredient).HasMaxLength(150);
        builder.Property(e => e.Strength).HasMaxLength(50);
        builder.Property(e => e.Unit).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Price).HasPrecision(12, 2).HasDefaultValue(0m);
        builder.Property(e => e.IsActive).HasDefaultValue(true);
        builder.Property(e => e.IsInsuranceCovered).HasDefaultValue(true);
    }
}
