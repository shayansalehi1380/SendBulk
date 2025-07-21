using Microsoft.Extensions.Diagnostics.HealthChecks;
using SendBulk.Models;
using SendBulk.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

builder.Services.AddHostedService<SmsMonitoringService>();

// اضافه کردن تنظیمات SMS
builder.Services.Configure<FarapayamakSettings>(
    builder.Configuration.GetSection("Farapayamak"));

// اضافه کردن SmsService
builder.Services.AddScoped<SmsService>();

// اضافه کردن HttpClient
builder.Services.AddHttpClient();

// اضافه کردن CORS برای front-end
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "SendBulk API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

// فعال کردن CORS
app.UseCors("AllowAll");

// فعال کردن Static Files برای HTML/CSS/JS
app.UseStaticFiles();

// فعال کردن Default Files (index.html)
app.UseDefaultFiles();

app.UseAuthorization();

app.MapControllers();

// اضافه کردن fallback برای SPA
app.MapFallbackToFile("index.html");

// دریافت connection string از appsettings.json (اختیاری برای توکن تست)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// تست اتصال به دیتابیس (اختیاری)
if (!string.IsNullOrEmpty(connectionString))
{
    try
    {
        var dbChecker = new DbHealthCheck(connectionString);
        var isConnected = dbChecker.CheckConnection();
        if (!isConnected)
        {
            Console.WriteLine("Failed Connect To Database.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database test failed: {ex.Message}");
    }
}

Console.WriteLine("Application started successfully!");

app.Run();