using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class ClinicRoomConfiguration : IEntityTypeConfiguration<ClinicRoom>
{
    public void Configure(EntityTypeBuilder<ClinicRoom> builder)
    {
        builder.ToTable("clinic_rooms");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.RoomName).HasMaxLength(50).IsRequired();
        builder.Property(e => e.RoomNumber).HasMaxLength(20).IsRequired();

        builder.HasOne(e => e.Department)
            .WithMany(d => d.Rooms)
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
