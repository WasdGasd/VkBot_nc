using VKB_WA.Services;
using VKBot.Services;
using WKBD_nc.Data;
using WKBD_nc.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Регистрация сервисов бота
builder.Services.AddScoped<BotService>();
builder.Services.AddScoped<ErrorLogger>();
builder.Services.AddHostedService<BotHostedService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
// ... остальной код
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
