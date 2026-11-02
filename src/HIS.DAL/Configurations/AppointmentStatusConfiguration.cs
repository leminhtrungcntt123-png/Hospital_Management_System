using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class AppointmentStatusConfiguration : IEntityTypeConfiguration<AppointmentStatus>
{
    public void Configure(EntityTypeBuilder<AppointmentStatus> builder)
    {
        builder.ToTable("appointment_statuses");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(e => e.Code).IsUnique();

        builder.Property(e => e.Name).HasMaxLength(50).IsRequired();
        builder.Property(e => e.SortOrder).IsRequired();
        builder.Property(e => e.IsTerminal).HasDefaultValue(false);

        builder.HasData(
            new AppointmentStatus { Id = 1, Code = "PENDING", Name = "Chờ xác nhận", SortOrder = 1, IsTerminal = false },
            new AppointmentStatus { Id = 2, Code = "CONFIRMED", Name = "Đã xác nhận", SortOrder = 2, IsTerminal = false },
            new AppointmentStatus { Id = 3, Code = "CHECKED_IN", Name = "Đã check-in", SortOrder = 3, IsTerminal = false },
            new AppointmentStatus { Id = 4, Code = "READY_FOR_DOCTOR", Name = "Chờ bác sĩ", SortOrder = 4, IsTerminal = false },
            new AppointmentStatus { Id = 5, Code = "IN_PROGRESS", Name = "Đang khám", SortOrder = 5, IsTerminal = false },
            new AppointmentStatus { Id = 6, Code = "COMPLETED", Name = "Hoàn thành", SortOrder = 6, IsTerminal = true },
            new AppointmentStatus { Id = 7, Code = "CANCELLED", Name = "Đã hủy", SortOrder = 7, IsTerminal = true },
            new AppointmentStatus { Id = 8, Code = "NO_SHOW", Name = "Không đến", SortOrder = 8, IsTerminal = true },
            new AppointmentStatus { Id = 9, Code = "EXPIRED", Name = "Hết hạn giữ chỗ", SortOrder = 9, IsTerminal = true },
            new AppointmentStatus { Id = 10, Code = "RESCHEDULE_REQUIRED", Name = "Cần đặt lại lịch", SortOrder = 10, IsTerminal = false }
        );
    }
}
