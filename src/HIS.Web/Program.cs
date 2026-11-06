using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using HIS.BLL;
using HIS.DAL;
using HIS.Web.Data;
using HIS.Web.Workers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add DAL & BLL
builder.Services.AddHisDataAccess(builder.Configuration);
builder.Services.AddHisBusiness(builder.Configuration);

// Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    });

// Authorization Policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireClaim(ClaimTypes.Role, "ADMIN"));
    options.AddPolicy("DepartmentHeadOnly", policy => policy.RequireClaim(ClaimTypes.Role, "DEPARTMENT_HEAD"));
    options.AddPolicy("DoctorOnly", policy => policy.RequireClaim(ClaimTypes.Role, "DOCTOR"));
    options.AddPolicy("NurseOnly", policy => policy.RequireClaim(ClaimTypes.Role, "NURSE"));
    options.AddPolicy("PatientOnly", policy => policy.RequireClaim(ClaimTypes.Role, "PATIENT"));
    options.AddPolicy("StaffAny", policy => policy.RequireClaim(ClaimTypes.Role, "ADMIN", "DEPARTMENT_HEAD", "DOCTOR", "NURSE"));
});

// MVC & Antiforgery
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

// Rate limiting for login
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

// Background workers
builder.Services.AddHostedService<SlotGenerationWorker>();
builder.Services.AddHostedService<HoldExpirationWorker>();
builder.Services.AddHostedService<NoShowWorker>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseStatusCodePagesWithReExecute("/Home/NotFound", "?statusCode={0}");

app.UseHttpsRedirection();
app.UseStaticFiles();

// Request Localization: vi-VN
var supportedCultures = new[] { new CultureInfo("vi-VN") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("vi-VN"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Route mappings
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapDefaultControllerRoute();

app.MapHealthChecks("/health");

// Run DevDataSeeder
if (app.Environment.IsDevelopment())
{
    await DevDataSeeder.SeedAsync(app.Services);
}

app.Run();
