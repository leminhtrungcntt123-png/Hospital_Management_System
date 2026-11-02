using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class InsuranceCardConfiguration : IEntityTypeConfiguration<InsuranceCard>
{
    public void Configure(EntityTypeBuilder<InsuranceCard> builder)
    {
        builder.ToTable("insurance_cards", t =>
        {
            t.HasCheckConstraint("ck_insurance_dates", "valid_to >= valid_from");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.CardNumber).HasMaxLength(20).IsRequired();
        builder.HasIndex(e => e.CardNumber).IsUnique();

        builder.Property(e => e.RegisteredFacility).HasMaxLength(150);
        builder.Property(e => e.IsRegisteredHere).HasDefaultValue(false);
        builder.Property(e => e.ValidFrom).IsRequired();
        builder.Property(e => e.ValidTo).IsRequired();
        builder.Property(e => e.IsActive).HasDefaultValue(true);
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Patient)
            .WithMany(p => p.InsuranceCards)
            .HasForeignKey(e => e.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.InsurancePlan)
            .WithMany(ip => ip.InsuranceCards)
            .HasForeignKey(e => e.InsurancePlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.PatientId, e.ValidTo }).HasDatabaseName("idx_insurance_cards_patient");
    }
}
