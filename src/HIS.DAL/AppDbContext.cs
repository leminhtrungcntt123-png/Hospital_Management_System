using Microsoft.EntityFrameworkCore;
using HIS.Domain;

namespace HIS.DAL;

public class AppDbContext : DbContext
{
    private readonly TimeProvider _timeProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider timeProvider)
        : base(options)
    {
        _timeProvider = timeProvider;
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
    public DbSet<DoctorSchedule> DoctorSchedules => Set<DoctorSchedule>();
    public DbSet<ScheduleException> ScheduleExceptions => Set<ScheduleException>();
    public DbSet<AppointmentSlot> AppointmentSlots => Set<AppointmentSlot>();
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

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // CSDL thật dùng 'timestamp without time zone' cho mọi cột datetime
        configurationBuilder.Properties<DateTime>()
            .HaveColumnType("timestamp without time zone");

        // Mọi cột CHECK dạng chuỗi trong init.md mục 5 lưu enum PascalCase dưới dạng UPPER_SNAKE
        configurationBuilder.Properties<Gender>().HaveConversion<UpperSnakeCaseEnumConverter<Gender>>();
        configurationBuilder.Properties<ExamShift>().HaveConversion<UpperSnakeCaseEnumConverter<ExamShift>>();
        configurationBuilder.Properties<SlotStatus>().HaveConversion<UpperSnakeCaseEnumConverter<SlotStatus>>();
        configurationBuilder.Properties<BookingChannel>().HaveConversion<UpperSnakeCaseEnumConverter<BookingChannel>>();
        configurationBuilder.Properties<VisitType>().HaveConversion<UpperSnakeCaseEnumConverter<VisitType>>();
        configurationBuilder.Properties<ScheduleExceptionType>().HaveConversion<UpperSnakeCaseEnumConverter<ScheduleExceptionType>>();
        configurationBuilder.Properties<ScheduleExceptionStatus>().HaveConversion<UpperSnakeCaseEnumConverter<ScheduleExceptionStatus>>();
        configurationBuilder.Properties<PatientRelationship>().HaveConversion<UpperSnakeCaseEnumConverter<PatientRelationship>>();
        configurationBuilder.Properties<LinkStatus>().HaveConversion<UpperSnakeCaseEnumConverter<LinkStatus>>();
        configurationBuilder.Properties<InvoiceCategory>().HaveConversion<UpperSnakeCaseEnumConverter<InvoiceCategory>>();
        configurationBuilder.Properties<InvoiceStatus>().HaveConversion<UpperSnakeCaseEnumConverter<InvoiceStatus>>();
        configurationBuilder.Properties<PaymentType>().HaveConversion<UpperSnakeCaseEnumConverter<PaymentType>>();
        configurationBuilder.Properties<PaymentMethod>().HaveConversion<UpperSnakeCaseEnumConverter<PaymentMethod>>();
        configurationBuilder.Properties<LabOrderStatus>().HaveConversion<UpperSnakeCaseEnumConverter<LabOrderStatus>>();
        configurationBuilder.Properties<ReferralType>().HaveConversion<UpperSnakeCaseEnumConverter<ReferralType>>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PK INTEGER GENERATED ALWAYS AS IDENTITY — đúng với CSDL hiện tại.
        // Gọi TRƯỚC khi nạp cấu hình để 3 bảng danh mục (roles/service_types/appointment_statuses)
        // vẫn giữ ValueGeneratedNever() và cho phép seed id cố định.
        modelBuilder.UseIdentityAlwaysColumns();

        // Nạp toàn bộ file IEntityTypeConfiguration<T> trong các thư mục con
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Dữ liệu bệnh viện không được xóa dây chuyền: mọi FK là Restrict (init.md mục 3).
        // Vòng lặp này chạy cuối nên nó thắng mọi OnDelete() khai báo trong từng cấu hình.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        TouchUpdatedAt();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        TouchUpdatedAt();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void TouchUpdatedAt()
    {
        var now = _timeProvider.GetLocalNow().DateTime;

        foreach (var entry in ChangeTracker.Entries()
                    .Where(e => e.State == EntityState.Modified
                                && e.Entity is Account or Appointment or MedicalRecord))
        {
            entry.Property(nameof(Account.UpdatedAt)).CurrentValue = now;
        }
    }
}
