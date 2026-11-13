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
using Microsoft.AspNetCore.RateLimiting;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Add DAL & BLL
builder.Services.AddHisDataAccess(builder.Configuration);
builder.Services.AddHisBusiness(builder.Configuration);
builder.Services.AddMemoryCache();
builder.Services.ConfigureOptions<HIS.Web.Common.AccountStatusCookieOptions>();

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
    options.AddPolicy("DoctorOnly", policy => policy.RequireClaim(ClaimTypes.Role, "DOCTOR", "DEPARTMENT_HEAD"));
    options.AddPolicy("NurseOnly", policy => policy.RequireClaim(ClaimTypes.Role, "NURSE"));
    options.AddPolicy("PatientOnly", policy => policy.RequireClaim(ClaimTypes.Role, "PATIENT"));
    options.AddPolicy("StaffAny", policy => policy.RequireClaim(ClaimTypes.Role, "ADMIN", "DEPARTMENT_HEAD", "DOCTOR", "NURSE"));
});

// MVC & Antiforgery
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

// Rate limiting for authentication endpoints
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        var policy = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        if (policy is "register" or "password-reset-request" or "password-reset-confirm" or "guest-booking")
        {
            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                success = false,
                message = "Bạn đã gửi quá nhiều yêu cầu. Vui lòng đợi một phút rồi thử lại."
            }, cancellationToken: ct);
            return;
        }

        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await context.HttpContext.Response.WriteAsync(
            "Bạn đã thử đăng nhập quá nhiều lần. Vui lòng đợi một phút rồi thử lại.", ct);
    };
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    foreach (var (name, limit) in new[] { ("register", 3), ("password-reset-request", 3), ("password-reset-confirm", 10), ("guest-booking", 5) })
    {
        options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    }
});

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

// Background workers
builder.Services.AddHostedService<HoldExpirationWorker>();
builder.Services.AddSingleton<PasswordResetQueue>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<PasswordResetQueue>());

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

app.UseStatusCodePages(async statusCodeContext =>
{
    var httpContext = statusCodeContext.HttpContext;
    if (httpContext.Response.StatusCode == StatusCodes.Status404NotFound
        && HttpMethods.IsGet(httpContext.Request.Method)
        && !httpContext.Request.Path.StartsWithSegments("/api"))
    {
        var originalPath = httpContext.Request.Path;
        var originalQueryString = httpContext.Request.QueryString;

        httpContext.Request.Path = "/Home/NotFound";
        httpContext.Request.QueryString = QueryString.Empty;
        try
        {
            await statusCodeContext.Next(httpContext);
        }
        finally
        {
            httpContext.Request.Path = originalPath;
            httpContext.Request.QueryString = originalQueryString;
        }
    }
});

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

// Run DevDataSeeder: tự bỏ qua trừ khi Seed:Enabled=true (Development, hoặc env Seed__Enabled trên Render).
await DevDataSeeder.SeedAsync(app.Services);

app.Run();
