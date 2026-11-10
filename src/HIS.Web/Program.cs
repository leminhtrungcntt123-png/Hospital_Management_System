using HIS.DAL;
using HIS.BLL;
using HIS.BLL.Interfaces;
using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.Web.Options;
using HIS.Web.Realtime;
using HIS.Web.Services;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// App Password SMTP để gửi mail OTP thật: cất trong file này (đã .gitignore vì khớp *.local.json)
// hoặc biến môi trường Otp:Smtp:User / Otp:Smtp:Password. Không để vào appsettings.json vì file đó đang được commit.
builder.Configuration.AddJsonFile("appsettings.Otp.local.json", optional: true, reloadOnChange: false);
// Cấu hình cục bộ máy cá nhân (chứa chuỗi kết nối riêng, không bao giờ bị push lên Git)
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

// MVC (Controllers + Views) thay cho Razor Pages mặc định
builder.Services.AddControllersWithViews();

// Trang đăng nhập gửi JSON kèm token chống giả mạo trong header.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

// Đăng nhập bằng cookie xác thực: giữ được "Ghi nhớ đăng nhập" mà Session demo trước đây không có.
builder.Services.ConfigureOptions<HIS.Web.Common.AccountStatusCookieOptions>();
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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "ADMIN"));
    options.AddPolicy("DepartmentHeadOnly", policy => policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "DEPT_HEAD", "DEPARTMENT_HEAD"));
    options.AddPolicy("DoctorOnly", policy => policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "DOCTOR", "DEPT_HEAD", "DEPARTMENT_HEAD"));
    options.AddPolicy("NurseOnly", policy => policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "NURSE", "RECEPTIONIST"));
    options.AddPolicy("PatientOnly", policy => policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "PATIENT"));
    options.AddPolicy("StaffAny", policy => policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "ADMIN", "DEPT_HEAD", "DEPARTMENT_HEAD", "DOCTOR", "NURSE", "RECEPTIONIST", "CASHIER"));
});

// Chặn dò mật khẩu và bảo vệ các endpoint xác thực
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        var policy = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        if (policy is "register" or "password-reset-request" or "password-reset-confirm")
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

    foreach (var (name, limit) in new[] { ("register", 3), ("password-reset-request", 3), ("password-reset-confirm", 10) })
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

// Session: lưu lựa chọn của người dùng giữa các bước đặt lịch (khoa, ngày, ca, người khám)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Đăng ký DbContext với chuỗi kết nối
// AppDbContext cần TimeProvider để ghi updated_at theo đồng hồ ứng dụng (spec init.md mục 4).
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")).UseSnakeCaseNamingConvention());
builder.Services.AddBookingOptions(builder.Configuration);
builder.Services.AddOtpOptions(builder.Configuration);
builder.Services.Configure<OtpSmtpOptions>(builder.Configuration.GetSection(OtpSmtpOptions.SectionName));

// Nguồn dữ liệu đặt lịch.
// Khi nối database: tạo class mới implement  (gọi HIS.BLL) rồi thay dòng này.
// Đăng ký các Service của BLL vào Web
// QueuePositionAllocator cấp vị trí + số A/T khi đặt lịch web nên BookingService cần nó.
builder.Services.AddScoped<QueuePositionAllocator>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<IDoctorService, DoctorService>();
builder.Services.AddScoped<IPatientProfileService, PatientProfileService>();
builder.Services.AddScoped<IPatientAccessService, PatientAccessService>();
// Trang đăng nhập demo của web gọi thẳng IAuthService (module của thành viên khác) để kiểm mật khẩu.
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IStaffService, StaffService>();
builder.Services.AddScoped<IDepartmentService, DepartmentService>();
builder.Services.AddScoped<IAdminReportService, AdminReportService>();
builder.Services.AddScoped<IScheduleChangeService, ScheduleChangeService>();
builder.Services.AddScoped<IQueueService, QueueService>();
builder.Services.AddScoped<IDepartmentHeadService, DepartmentHeadService>();
builder.Services.AddScoped<IReceptionService, ReceptionService>();
builder.Services.AddScoped<IBillingService, BillingService>();
builder.Services.AddScoped<IHealthRecordService, HealthRecordService>();

// Mã OTP khi thêm hồ sơ: có SMTP thật thì gửi mail. Chưa có SMTP thì chỉ môi trường Development mới được
// fallback sang kênh demo (in mã ra log + màn hình); môi trường khác mà thiếu SMTP là KHÔNG có kênh gửi,
// OtpService trả "Chưa cấu hình kênh gửi mã OTP." thay vì âm thầm phát mã cho người không kiểm soát được.
builder.Services.AddScoped<IOtpService, OtpService>();
var smtpOptions = builder.Configuration.GetSection(OtpSmtpOptions.SectionName).Get<OtpSmtpOptions>() ?? new OtpSmtpOptions();
if (smtpOptions.IsConfigured)
{
    builder.Services.AddScoped<IOtpDelivery, SmtpOtpDelivery>();
}
else if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<IOtpDelivery, DemoOtpDelivery>();
}

// Cờ hiện mã chỉ hợp lệ khi lập trình viên tự test; để nó bật ở môi trường khác là lộ mã cho mọi người dùng.
if (!builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue<bool>($"{OtpOptions.SectionName}:{nameof(OtpOptions.RevealCodeOnScreen)}"))
{
    throw new InvalidOperationException(
        "Otp:RevealCodeOnScreen chỉ được bật ở môi trường Development vì ai biết mã bệnh nhân cũng đọc được mã ngay trên màn hình.");
}

// Kênh realtime cho quầy tiếp nhận (WinForms nối vào /ws/quay để biết dữ liệu vừa đổi).
builder.Services.AddSingleton<QueueChangeNotifier>();

// Giữ nguyên cấu hình HttpClient gọi HIS.WebApi (phần của thành viên khác có thể dùng)
builder.Services.AddHttpClient("HisWebApi", client =>
{
    var baseUrl = builder.Configuration["WebApi:BaseUrl"] ?? "https://localhost:7198/";
    client.BaseAddress = new Uri(baseUrl);
});

var app = builder.Build();

// Nối WebSocket phải đứng trước UseHttpsRedirection: quầy WinForms bấm ws:// trên cổng http
// và yêu cầu upgrade này không được phép bị chuyển hướng sang https.
app.UseQueueChangeSocket();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Booking/Error");
    app.UseHsts();
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
        httpContext.Request.Path = "/NotFound";
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

app.UseRouting();

app.UseRateLimiter();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

// Tự động kiểm tra và nạp dữ liệu mẫu chuẩn cho môi trường Development
if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var forceSeed = args.Contains("--seed-fresh") || args.Contains("--seed");
        await HIS.BLL.Data.DbSeeder.SeedAsync(dbContext, forceFresh: forceSeed);
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetService<ILogger<Program>>();
        logger?.LogWarning(ex, "Không thể tự động nạp dữ liệu mẫu (DbSeeder): {Message}", ex.Message);
    }

    // Endpoint dev để làm mới dữ liệu test: Chỉ cho phép gọi bằng POST từ localhost/loopback kèm header xác nhận chống CSRF
    app.MapGet("/dev/seed-fresh", () => Results.Ok(new
    {
        message = "Để làm mới CSDL, hãy gửi request POST tới /dev/seed-fresh từ localhost kèm header 'X-Confirm-Reset: reset-dev-data'."
    }));

    app.MapPost("/dev/seed-fresh", async (HttpContext httpContext, IServiceProvider sp) =>
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress;
        if (remoteIp == null || !System.Net.IPAddress.IsLoopback(remoteIp))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // Chống CSRF từ cross-site POST: Chỉ chấp nhận request có custom header 'X-Confirm-Reset: reset-dev-data'
        var confirmHeader = httpContext.Request.Headers["X-Confirm-Reset"].ToString();
        if (confirmHeader != "reset-dev-data")
        {
            return Results.BadRequest(new
            {
                success = false,
                message = "Hành động nguy hiểm. Yêu cầu truyền header xác nhận chống CSRF: 'X-Confirm-Reset: reset-dev-data'."
            });
        }

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await HIS.BLL.Data.DbSeeder.SeedAsync(db, forceFresh: true);
        return Results.Ok(new
        {
            success = true,
            message = "Đã làm mới toàn bộ dữ liệu test chuẩn thành công!",
            defaultPassword = HIS.BLL.Data.DbSeeder.DefaultPassword,
            accounts = new[]
            {
                new { username = "admin", role = "ADMIN", name = "Quản trị viên" },
                new { username = "receptionist", role = "RECEPTIONIST", name = "Tiếp đón (Trần Thị Vân)" },
                new { username = "nurse", role = "NURSE", name = "Điều dưỡng (Nguyễn Thị Mai)" },
                new { username = "bs.tranthutrang", role = "DOCTOR", name = "BS.CKI Trần Thu Trang (Khoa Tim mạch - P101)" },
                new { username = "bs.phamthuyduong", role = "DOCTOR", name = "BS.CKI Phạm Thùy Dương (Khoa Nhi - P301)" },
                new { username = "patient01", role = "PATIENT", name = "Bệnh nhân mẫu (Nguyễn Minh Anh - CCCD 001190012345)" },
                new { username = "patient02", role = "PATIENT", name = "Bệnh nhân mẫu (Trần Thu Hà & Bé Trần Bảo Nam)" }
            }
        });
    });
}

// Trang mặc định: trang bắt đầu đặt lịch
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Booking}/{action=Index}/{id?}");

app.Run();