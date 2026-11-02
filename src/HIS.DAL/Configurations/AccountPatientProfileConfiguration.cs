using HIS.DAL.Converters;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class AccountPatientProfileConfiguration : IEntityTypeConfiguration<AccountPatientProfile>
{
    public void Configure(EntityTypeBuilder<AccountPatientProfile> builder)
    {
        builder.ToTable("account_patient_profiles", t =>
        {
            t.HasCheckConstraint("ck_account_patient_profiles_relationship", "relationship IN ('SELF','SPOUSE','CHILD','PARENT','SIBLING','OTHER')");
            t.HasCheckConstraint("ck_account_patient_profiles_status", "status IN ('ACTIVE','PENDING','REVOKED')");
            t.HasCheckConstraint("ck_link_pending_no_records", "status = 'ACTIVE' OR can_view_records = FALSE");
            t.HasCheckConstraint("ck_link_self_not_pending", "NOT (relationship = 'SELF' AND status = 'PENDING')");
            t.HasCheckConstraint("ck_link_adult_records_consent", "can_view_records = FALSE OR relationship IN ('SELF','CHILD') OR verified_by IS NOT NULL");
        });

        builder.HasKey(e => new { e.AccountId, e.PatientId });

        builder.Property(e => e.Relationship).HasConversion<UpperSnakeCaseEnumConverter<PatientRelationship>>().HasMaxLength(15).IsRequired();
        builder.Property(e => e.Status).HasConversion<UpperSnakeCaseEnumConverter<LinkStatus>>().HasMaxLength(10).HasDefaultValue(LinkStatus.Active);
        builder.Property(e => e.CanViewRecords).HasDefaultValue(false);
        builder.Property(e => e.VerifiedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Account)
            .WithMany(a => a.PatientLinks)
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Patient)
            .WithMany(p => p.AccountLinks)
            .HasForeignKey(e => e.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.CreatedByAccount)
            .WithMany(a => a.CreatedPatientLinks)
            .HasForeignKey(e => e.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.VerifiedByAccount)
            .WithMany(a => a.VerifiedPatientLinks)
            .HasForeignKey(e => e.VerifiedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.PatientId, e.Status }).HasDatabaseName("idx_link_patient");

        builder.HasIndex(e => e.AccountId)
            .IsUnique()
            .HasFilter("relationship = 'SELF' AND status <> 'REVOKED'")
            .HasDatabaseName("uq_link_self_account");

        builder.HasIndex(e => e.PatientId)
            .IsUnique()
            .HasFilter("relationship = 'SELF' AND status = 'ACTIVE'")
            .HasDatabaseName("uq_link_self_patient");
    }
}
