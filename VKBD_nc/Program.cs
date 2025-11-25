using Microsoft.EntityFrameworkCore;
using VKBD_nc.Data;
using VKBD_nc.Services;

var builder = WebApplication.CreateBuilder(args);

// Читаем строку подключения для SQLite из appsettings.json
var connectionString = builder.Configuration.GetConnectionString("Default");

// Регистрируем DbContext с SQLite
builder.Services.AddDbContext<BotDbContext>(options =>
    options.UseSqlite(connectionString));

// Регистрируем другие сервисы
builder.Services.AddScoped<DbService>();

// Контроллеры и Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Конфигурация HTTP пайплайна
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
