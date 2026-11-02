using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class AccessLogConfiguration : IEntityTypeConfiguration<AccessLog>
{
    public void Configure(EntityTypeBuilder<AccessLog> builder)
    {
        builder.ToTable("access_logs");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Action).HasMaxLength(50).IsRequired();
        builder.Property(e => e.OccurredAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");
        builder.Property(e => e.IpAddress).HasMaxLength(45);

        builder.HasOne(e => e.Account)
            .WithMany(a => a.AccessLogs)
            .HasForeignKey(e => e.AccountId)
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
    }
}
