using HIS.DAL.Converters;
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
            t.HasCheckConstraint("ck_patients_gender", "gender IN ('MALE','FEMALE','OTHER')");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.FullName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.DateOfBirth).IsRequired();
        builder.Property(e => e.Gender).HasConversion<UpperSnakeCaseEnumConverter<Gender>>().HasMaxLength(10);
        builder.Property(e => e.NationalId).HasMaxLength(20);
        builder.HasIndex(e => e.NationalId).IsUnique();

        builder.Property(e => e.PhoneNumber).HasMaxLength(15).IsRequired();
        builder.Property(e => e.Address).HasMaxLength(255);
        builder.Property(e => e.Allergies).HasMaxLength(500);
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");
    }
}
