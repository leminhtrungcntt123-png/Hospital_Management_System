using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class AccessLogConfiguration : IEntityTypeConfiguration<AccessLog>
{
    public void Configure(EntityTypeBuilder<AccessLog> builder)
    {
        builder.ToTable("access_logs", t =>
        {
            t.HasCheckConstraint("ck_access_logs_severity", "severity IN ('INFO', 'WARNING', 'SECURITY', 'CRITICAL')");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Action).HasMaxLength(50).IsRequired();
        builder.Property(e => e.EntityName).HasMaxLength(50);
        builder.Property(e => e.IsSuccess).HasDefaultValue(true);
        builder.Property(e => e.Severity).HasMaxLength(20).HasDefaultValue("INFO");
        builder.Property(e => e.OccurredAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");
        builder.Property(e => e.IpAddress).HasMaxLength(45);
        builder.Property(e => e.UserAgent).HasMaxLength(255);
        builder.Property(e => e.Details).HasColumnType("text");

        builder.HasOne(e => e.Account)
            .WithMany(a => a.AccessLogs)
            .HasForeignKey(e => e.AccountId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Patient)
            .WithMany(p => p.AccessLogs)
            .HasForeignKey(e => e.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.MedicalRecord)
            .WithMany(mr => mr.AccessLogs)
            .HasForeignKey(e => e.MedicalRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.AccountId).HasDatabaseName("idx_access_logs_account");
        builder.HasIndex(e => e.PatientId).HasDatabaseName("idx_access_logs_patient");
        builder.HasIndex(e => e.MedicalRecordId).HasDatabaseName("idx_access_logs_record");
        builder.HasIndex(e => e.OccurredAt).IsDescending().HasDatabaseName("idx_access_logs_occurred_at");
        builder.HasIndex(e => new { e.Action, e.OccurredAt }).HasDatabaseName("idx_access_logs_action_time");
        builder.HasIndex(e => new { e.EntityName, e.EntityId })
            .HasDatabaseName("idx_access_logs_entity")
            .HasFilter("entity_name IS NOT NULL");
    }
}
