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
            t.HasCheckConstraint("ck_account_patient_profiles_relationship",
                "relationship IN ('SELF','SPOUSE','CHILD','PARENT','SIBLING','OTHER')");
            t.HasCheckConstraint("ck_account_patient_profiles_status",
                "status IN ('ACTIVE','PENDING','REVOKED')");
            // Link chưa duyệt thì chưa được quyền xem hồ sơ
            t.HasCheckConstraint("ck_link_pending_no_records",
                "status = 'ACTIVE' OR can_view_records = FALSE");
            // "Bản thân" do chính chủ sở hữu, không có chuyện chờ duyệt
            t.HasCheckConstraint("ck_link_self_not_pending",
                "NOT (relationship = 'SELF' AND status = 'PENDING')");
            // Chỉ SELF/CHILD được tự bật quyền xem; trường hợp khác phải có người duyệt
            t.HasCheckConstraint("ck_link_adult_records_consent",
                "can_view_records = FALSE OR relationship IN ('SELF','CHILD') OR verified_by IS NOT NULL");
        });

        builder.HasKey(x => new { x.AccountId, x.PatientId });

        builder.Property(x => x.Relationship).HasMaxLength(15).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(10).HasDefaultValue(LinkStatus.Active).IsRequired();
        builder.Property(x => x.CanViewRecords).HasDefaultValue(false).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

        builder.HasOne(x => x.Account)
            .WithMany(a => a.PatientLinks)
            .HasForeignKey(x => x.AccountId);

        builder.HasOne(x => x.Patient)
            .WithMany(p => p.AccountLinks)
            .HasForeignKey(x => x.PatientId);

        builder.HasOne(x => x.Creator)
            .WithMany()
            .HasForeignKey(x => x.CreatedBy);

        builder.HasOne(x => x.Verifier)
            .WithMany()
            .HasForeignKey(x => x.VerifiedBy);

        builder.HasIndex(x => new { x.PatientId, x.Status }).HasDatabaseName("idx_link_patient");

        // Mỗi tài khoản chỉ một hồ sơ "bản thân" còn hiệu lực;
        // mỗi bệnh nhân chỉ là "bản thân" của tối đa một tài khoản đang ACTIVE.
        builder.HasIndex(x => x.AccountId)
            .IsUnique()
            .HasDatabaseName("uq_link_self_account")
            .HasFilter("relationship = 'SELF' AND status <> 'REVOKED'");

        builder.HasIndex(x => x.PatientId)
            .IsUnique()
            .HasDatabaseName("uq_link_self_patient")
            .HasFilter("relationship = 'SELF' AND status = 'ACTIVE'");
    }
}
