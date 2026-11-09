using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts", t =>
        {
            // Email luôn lưu chữ thường để UNIQUE không bị qua mặt bởi "A@x" vs "a@x"
            t.HasCheckConstraint("ck_accounts_email_lower", "email IS NULL OR email = lower(email)");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Username).HasMaxLength(50).IsRequired();
        builder.HasIndex(a => a.Username).IsUnique();

        builder.Property(a => a.Email).HasMaxLength(100);
        builder.HasIndex(a => a.Email).IsUnique();
        builder.Property(a => a.PasswordHash).HasMaxLength(255).IsRequired();
        builder.Property(a => a.RoleId).IsRequired();
        builder.Property(a => a.IsActive).HasDefaultValue(true);
        builder.Property(a => a.FailedLoginCount).HasDefaultValue(0);
        builder.Property(a => a.BookingBlockedUntil);
        builder.Property(a => a.NoShowResetAt);
        builder.Property(a => a.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(a => a.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasOne(a => a.Role)
            .WithMany(r => r.Accounts)
            .HasForeignKey(a => a.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}