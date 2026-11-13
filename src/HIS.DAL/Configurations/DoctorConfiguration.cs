using HIS.DAL.Converters;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        builder.ToTable("doctors", t =>
        {
            t.HasCheckConstraint("ck_doctor_experience", "years_of_experience >= 0");
            t.HasCheckConstraint("ck_doctor_employment_type", "employment_type IN ('FULL_TIME', 'PART_TIME')");
            t.HasCheckConstraint("ck_doctor_quota", "max_shifts_per_week >= min_shifts_per_week");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.FullName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Specialty).HasMaxLength(100);
        builder.Property(e => e.LicenseNumber).HasMaxLength(30);
        builder.Property(e => e.AvgConsultationMinutes).HasDefaultValue(30);

        // Homepage Showcase
        builder.Property(e => e.AcademicTitle).HasMaxLength(30);
        builder.Property(e => e.AvatarUrl).HasMaxLength(500);
        builder.Property(e => e.ShortBio).HasMaxLength(300);
        builder.Property(e => e.Biography).HasColumnType("text");
        builder.Property(e => e.YearsOfExperience).HasDefaultValue((short)0);
        builder.Property(e => e.IsPublic).HasDefaultValue(true);
        builder.Property(e => e.IsFeatured).HasDefaultValue(false);
        builder.Property(e => e.DisplayOrder).HasDefaultValue(100);

        // Quota
        builder.Property(e => e.EmploymentType)
            .HasConversion<UpperSnakeCaseEnumConverter<EmploymentType>>()
            .HasMaxLength(20)
            .HasDefaultValue(EmploymentType.FullTime);
        builder.Property(e => e.MinShiftsPerWeek).HasDefaultValue((short)2);
        builder.Property(e => e.MaxShiftsPerWeek).HasDefaultValue((short)10);

        builder.HasOne(e => e.Account)
            .WithOne(a => a.Doctor)
            .HasForeignKey<Doctor>(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Department)
            .WithMany(d => d.Doctors)
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.LicenseNumber).IsUnique();

        builder.HasIndex(e => new { e.IsFeatured, e.DisplayOrder })
            .HasDatabaseName("idx_doctors_homepage")
            .HasFilter("is_public = TRUE");
    }
}
