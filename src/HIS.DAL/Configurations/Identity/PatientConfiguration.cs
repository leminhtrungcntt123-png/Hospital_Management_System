using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("patients", t =>
        {
            t.HasCheckConstraint("ck_patients_gender", "gender IS NULL OR gender IN ('MALE','FEMALE','OTHER')");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.FullName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.DateOfBirth).IsRequired();
        builder.Property(p => p.Gender).HasMaxLength(10);
        builder.Property(p => p.NationalId).HasMaxLength(20);
        builder.HasIndex(p => p.NationalId).IsUnique();
        builder.Property(p => p.PhoneNumber).HasMaxLength(15).IsRequired();
        builder.Property(p => p.Address).HasMaxLength(255);
        builder.Property(p => p.CreatedAt).HasDefaultValueSql("now()");

        // Quan hệ tài khoản <-> hồ sơ nằm ở account_patient_profiles (nhiều-nhiều),
        // không còn cột patients.account_id.
    }
}