using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class NurseConfiguration : IEntityTypeConfiguration<Nurse>
{
    public void Configure(EntityTypeBuilder<Nurse> builder)
    {
        builder.ToTable("nurses");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.FullName).HasMaxLength(100).IsRequired();

        builder.HasOne(e => e.Account)
            .WithOne(a => a.Nurse)
            .HasForeignKey<Nurse>(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Department)
            .WithMany(d => d.Nurses)
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
