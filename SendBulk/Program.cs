using SendBulk.Models;
using SendBulk.Services;

var builder = WebApplication.CreateBuilder(args);


builder.Services.Configure<FarapayamakSettings>(
    builder.Configuration.GetSection("Farapayamak"));


builder.Services.AddSingleton<SmsService>();


builder.Services.AddControllers();


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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


app.MapControllers();

app.Run();
