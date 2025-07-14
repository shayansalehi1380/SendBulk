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
    options.IncludeXmlComments(xmlPath);
});


// اضافه کردن تنظیمات SMS
builder.Services.Configure<FarapayamakSettings>(
    builder.Configuration.GetSection("Farapayamak"));

// اضافه کردن SmsService
builder.Services.AddScoped<SmsService>();

// اضافه کردن CORS برای front-end
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
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

app.Run();
