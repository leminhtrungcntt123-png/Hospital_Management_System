using HIS.DAL.Converters;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class InsurancePlanConfiguration : IEntityTypeConfiguration<InsurancePlan>
{
    public void Configure(EntityTypeBuilder<InsurancePlan> builder)
    {
        builder.ToTable("insurance_plans", t =>
        {
            t.HasCheckConstraint("ck_insurance_plans_referral_type", "referral_type IN ('IN_NETWORK','OUT_OF_NETWORK')");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.CoveragePct).HasPrecision(5, 2).IsRequired();
        builder.Property(e => e.ReferralType).HasConversion<UpperSnakeCaseEnumConverter<ReferralType>>().HasMaxLength(20);
    }
}
