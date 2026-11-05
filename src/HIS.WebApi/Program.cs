using System.Text.Json;
using System.Text.Json.Serialization;
using HIS.BLL;
using HIS.BLL.Interfaces;
using HIS.BLL.Options;
using HIS.BLL.Services;
using HIS.DAL;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Enum serialize theo đúng mã đang lưu trong CSDL (MALE, ON_SITE, WALK_IN...)
// để payload API không đổi so với thời còn dùng cột chuỗi.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 1. Lấy Connection String
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// 2. Đăng ký AppDbContext (snake_case khớp schema CSDL thật;
// kiểu timestamp đã được AppDbContext cấu hình sẵn). TimeProvider nuôi updated_at.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
           .UseSnakeCaseNamingConvention());

// 3. Đăng ký các BLL Services
builder.Services.AddBookingOptions(builder.Configuration);
// BookingService giữ suất A của khung giờ khi tạo lịch hẹn nên cần allocator.
builder.Services.AddScoped<QueuePositionAllocator>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IBookingService, BookingService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();