using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).HasMaxLength(150).IsRequired();
        builder.Property(e => e.Price).HasPrecision(12, 2).HasDefaultValue(0m);
        builder.Property(e => e.IsActive).HasDefaultValue(true);

        builder.HasOne(e => e.ServiceType)
            .WithMany(st => st.Services)
            .HasForeignKey(e => e.ServiceTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
