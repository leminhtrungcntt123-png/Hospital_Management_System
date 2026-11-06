using FluentValidation;
using HIS.BLL.Common;
using HIS.BLL.Options;
using HIS.BLL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HIS.BLL;

public static class BusinessServiceCollectionExtensions
{
    public static IServiceCollection AddHisBusiness(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<TimeProvider, VietnamTimeProvider>();

        services.AddOptions<BookingOptions>()
            .Bind(configuration.GetSection(BookingOptions.SectionName))
            .Validate(options =>
            {
                var context = new System.ComponentModel.DataAnnotations.ValidationContext(options);
                var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
                return System.ComponentModel.DataAnnotations.Validator.TryValidateObject(options, context, validationResults, true);
            }, "BookingOptions validation failed")
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName));

        services.AddValidatorsFromAssembly(typeof(BusinessServiceCollectionExtensions).Assembly);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IPatientProfileService, PatientProfileService>();
        services.AddScoped<IPatientAccessService, PatientAccessService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<ISlotGenerationService, SlotGenerationService>();
        services.AddScoped<IScheduleChangeService, ScheduleChangeService>();
        services.AddScoped<IQueueService, QueueService>();
        services.AddScoped<IReceptionService, ReceptionService>();
        services.AddScoped<IBillingService, BillingService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IDoctorService, DoctorService>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddScoped<IAdminReportService, AdminReportService>();
        services.AddScoped<IDepartmentHeadService, DepartmentHeadService>();
        services.AddScoped<IHospitalCatalogService, HospitalCatalogService>();

        return services;
    }
}
