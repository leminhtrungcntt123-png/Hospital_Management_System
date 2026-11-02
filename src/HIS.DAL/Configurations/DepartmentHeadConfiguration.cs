using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class DepartmentHeadConfiguration : IEntityTypeConfiguration<DepartmentHead>
{
    public void Configure(EntityTypeBuilder<DepartmentHead> builder)
    {
        builder.ToTable("department_heads");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.FullName).HasMaxLength(100).IsRequired();

        builder.HasOne(e => e.Account)
            .WithOne(a => a.DepartmentHead)
            .HasForeignKey<DepartmentHead>(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Department)
            .WithOne(d => d.DepartmentHead)
            .HasForeignKey<DepartmentHead>(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
