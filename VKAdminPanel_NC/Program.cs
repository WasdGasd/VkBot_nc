using VKAdminPanel_NC.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Регистрируем сервисы
builder.Services.AddSingleton<BotStateService>();
builder.Services.AddSingleton<LogsService>();
builder.Services.AddSingleton<SimpleStatsService>();
builder.Services.AddScoped<StatsService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

app.Run();
// Добавьте эту строку чтобы приложение не закрывалось
Thread.Sleep(Timeout.Infinite);