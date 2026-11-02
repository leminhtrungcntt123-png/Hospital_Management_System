using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class ServiceTypeConfiguration : IEntityTypeConfiguration<ServiceType>
{
    public void Configure(EntityTypeBuilder<ServiceType> builder)
    {
        builder.ToTable("service_types");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(e => e.Code).IsUnique();

        builder.Property(e => e.Name).HasMaxLength(50).IsRequired();

        builder.HasData(
            new ServiceType { Id = 1, Code = "EXAMINATION", Name = "Khám bệnh" },
            new ServiceType { Id = 2, Code = "LAB_TEST", Name = "Xét nghiệm" },
            new ServiceType { Id = 3, Code = "IMAGING", Name = "Chẩn đoán hình ảnh" }
        );
    }
}
