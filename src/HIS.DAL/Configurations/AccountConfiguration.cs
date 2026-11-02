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
            t.HasCheckConstraint("ck_accounts_email_lower", "email IS NULL OR email = lower(email)");
        });
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Username).HasMaxLength(50).IsRequired();
        builder.HasIndex(e => e.Username).IsUnique();

        builder.Property(e => e.Email).HasMaxLength(100);
        builder.HasIndex(e => e.Email).IsUnique();

        builder.Property(e => e.PasswordHash).HasMaxLength(255).IsRequired();
        builder.Property(e => e.IsActive).HasDefaultValue(true);
        builder.Property(e => e.FailedLoginCount).HasDefaultValue(0);
        builder.Property(e => e.LockedUntil).HasColumnType("timestamp without time zone");

        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");
        builder.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Role)
            .WithMany(r => r.Accounts)
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
