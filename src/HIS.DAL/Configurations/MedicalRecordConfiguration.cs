using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HIS.DAL.Configurations;

public class MedicalRecordConfiguration : IEntityTypeConfiguration<MedicalRecord>
{
    public void Configure(EntityTypeBuilder<MedicalRecord> builder)
    {
        builder.ToTable("medical_records");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.PrimaryDiagnosisCode).HasMaxLength(20);
        builder.Property(e => e.PrimaryDiagnosisDesc).HasMaxLength(255);
        builder.Property(e => e.FollowUpDate);
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");
        builder.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone").HasDefaultValueSql("now()");

        builder.HasOne(e => e.Appointment)
            .WithOne(a => a.MedicalRecord)
            .HasForeignKey<MedicalRecord>(e => e.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Doctor)
            .WithMany(d => d.MedicalRecords)
            .HasForeignKey(e => e.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.AppointmentId).HasDatabaseName("idx_records_appointment");

        builder.HasIndex(e => new { e.DoctorId, e.FollowUpDate })
            .HasDatabaseName("idx_medical_records_followup")
            .HasFilter("follow_up_date IS NOT NULL");
    }
}
