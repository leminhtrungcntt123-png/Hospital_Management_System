using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        builder.ToTable("doctors");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.FullName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Specialty).HasMaxLength(100);
        builder.Property(e => e.LicenseNumber).HasMaxLength(30);
        builder.Property(e => e.AvgConsultationMinutes).HasDefaultValue(15);

        builder.HasOne(e => e.Account)
            .WithOne(a => a.Doctor)
            .HasForeignKey<Doctor>(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Department)
            .WithMany(d => d.Doctors)
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
