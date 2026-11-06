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

// MVC (Controllers + Views) thay cho Razor Pages mặc định
builder.Services.AddControllersWithViews();

// Trang đăng nhập gửi JSON kèm token chống giả mạo trong header.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

// Đăng nhập bằng cookie xác thực: giữ được "Ghi nhớ đăng nhập" mà Session demo trước đây không có.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    });
builder.Services.AddAuthorization();

// Chặn dò mật khẩu: tối đa 5 lượt đăng nhập mỗi phút cho một địa chỉ IP.
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

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// Trang mặc định: trang bắt đầu đặt lịch
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Booking}/{action=Index}/{id?}");

app.Run();