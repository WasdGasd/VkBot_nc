using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VK;
using VKBD_nc.Data;
using BotServices;

var builder = WebApplication.CreateBuilder(args);

// 1. Добавляем контроллеры (это обязательно!)
builder.Services.AddControllers();

// 2. Настройка CORS
var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: MyAllowSpecificOrigins,
                      policy =>
                      {
                          policy.AllowAnyOrigin()
                                .AllowAnyHeader()
                                .WithMethods("POST", "GET", "PUT", "DELETE", "UPDATE")
                                .SetPreflightMaxAge(TimeSpan.FromSeconds(5));
                      });
});

// 3. Настройка DbContext - ТОЛЬКО ОДИН ПРОВАЙДЕР!
builder.Services.AddDbContext<BotDbContext>(options =>
    options.UseSqlite("Data Source=vkbot.db;"));

// 4. Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 5. Http clients and services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<VkApiManager>();
builder.Services.AddSingleton<KeyboardProvider>();
builder.Services.AddSingleton<ConversationStateService>();
builder.Services.AddSingleton<FileLogger>();
builder.Services.AddScoped<CommandService>();
builder.Services.AddScoped<IMessageService, MesService>();

var app = builder.Build();

// Конфигурация middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// CORS должен быть до маршрутизации и авторизации
app.UseCors(MyAllowSpecificOrigins);

app.UseRouting();
app.UseAuthorization();

// MapControllers должен быть после UseRouting и UseAuthorization
app.MapControllers();

// Создание базы данных при запуске
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        dbContext.Database.EnsureCreated();
        Console.WriteLine("SQLite database created successfully");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database creation failed: {ex.Message}");
    }
}

//app.Run("http://0.0.0.0:5000");
app.Run();