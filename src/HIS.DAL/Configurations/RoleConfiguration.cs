using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(e => e.Code).IsUnique();

        builder.Property(e => e.Name).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(255);

        builder.HasData(
            new Role { Id = 1, Code = "ADMIN", Name = "Quản trị viên" },
            new Role { Id = 2, Code = "DEPARTMENT_HEAD", Name = "Trưởng khoa" },
            new Role { Id = 3, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 4, Code = "NURSE", Name = "Y tá / Lễ tân" },
            new Role { Id = 5, Code = "PATIENT", Name = "Bệnh nhân" }
        );
    }
}
