using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HIS.DAL;

public class UtcToUnspecifiedDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcToUnspecifiedDateTimeConverter()
        : base(
            v => v.Kind == DateTimeKind.Utc ? DateTime.SpecifyKind(v, DateTimeKind.Unspecified) : v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}

public class UtcToUnspecifiedNullableDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public UtcToUnspecifiedNullableDateTimeConverter()
        : base(
            v => v.HasValue && v.Value.Kind == DateTimeKind.Utc ? DateTime.SpecifyKind(v.Value, DateTimeKind.Unspecified) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
    {
    }
}

public class AppDbContext : DbContext
{
    private readonly TimeProvider _timeProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider timeProvider)
        : base(options)
    {
        _timeProvider = timeProvider;
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcToUnspecifiedDateTimeConverter>();

        configurationBuilder.Properties<DateTime?>()
            .HaveConversion<UtcToUnspecifiedNullableDateTimeConverter>();
    }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();
    public DbSet<AppointmentStatus> AppointmentStatuses => Set<AppointmentStatus>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<DutyShift> DutyShifts => Set<DutyShift>();
    public DbSet<ClinicRoom> ClinicRooms => Set<ClinicRoom>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<AccountPatientProfile> AccountPatientProfiles => Set<AccountPatientProfile>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<Nurse> Nurses => Set<Nurse>();
    public DbSet<DepartmentHead> DepartmentHeads => Set<DepartmentHead>();
    public DbSet<Medication> Medications => Set<Medication>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<InsurancePlan> InsurancePlans => Set<InsurancePlan>();
    public DbSet<InsuranceCard> InsuranceCards => Set<InsuranceCard>();
    public DbSet<SchedulingPeriod> SchedulingPeriods => Set<SchedulingPeriod>();
    public DbSet<DoctorSchedule> DoctorSchedules => Set<DoctorSchedule>();
    public DbSet<ScheduleException> ScheduleExceptions => Set<ScheduleException>();
    public DbSet<AppointmentSlot> AppointmentSlots => Set<AppointmentSlot>();
    public DbSet<QueueCounter> QueueCounters => Set<QueueCounter>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentStatusHistory> AppointmentStatusHistories => Set<AppointmentStatusHistory>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<VitalSigns> VitalSigns => Set<VitalSigns>();
    public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
    public DbSet<LabOrder> LabOrders => Set<LabOrder>();
    public DbSet<LabResult> LabResults => Set<LabResult>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<AccessLog> AccessLogs => Set<AccessLog>();
    public DbSet<CashierShift> CashierShifts => Set<CashierShift>();
    public DbSet<Deposit> Deposits => Set<Deposit>();
    public DbSet<DepositUsage> DepositUsages => Set<DepositUsage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.UseIdentityAlwaysColumns();

        modelBuilder.HasSequence<int>("seq_invoice_number").StartsAt(1).IncrementsBy(1);
        modelBuilder.HasSequence<int>("seq_transaction_code").StartsAt(1).IncrementsBy(1);
        modelBuilder.HasSequence<int>("seq_deposit_code").StartsAt(1).IncrementsBy(1);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = _timeProvider.GetLocalNow().DateTime;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is Account account)
                {
                    account.UpdatedAt = utcNow;
                }
                else if (entry.Entity is Appointment appointment)
                {
                    appointment.UpdatedAt = utcNow;
                }
                else if (entry.Entity is MedicalRecord medicalRecord)
                {
                    medicalRecord.UpdatedAt = utcNow;
                }
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}

