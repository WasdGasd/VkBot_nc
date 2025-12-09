using Microsoft.EntityFrameworkCore;
using VKBot_nordciti.Data;
using VKBot_nordciti.Services;
using VKBot_nordciti.VK;

var builder = WebApplication.CreateBuilder(args);

// 1. Добавляем контроллеры
builder.Services.AddControllers();

builder.Services.AddSingleton<IBotStatsService, BotStatsService>();

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

// 3. Настройка DbContext
builder.Services.AddDbContext<BotDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// 4. Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "VK Bot API",
        Version = "v1"
    });
});

// 5. Http clients and services
builder.Services.AddHttpClient();

// 6. Сервисы бота
builder.Services.AddSingleton<VkApiManager>(provider =>
{
    var httpClient = provider.GetRequiredService<HttpClient>();
    var configuration = provider.GetRequiredService<IConfiguration>();
    var logger = provider.GetRequiredService<ILogger<VkApiManager>>();
    return new VkApiManager(httpClient, configuration, logger);
});

builder.Services.AddSingleton<KeyboardProvider>();
builder.Services.AddSingleton<ConversationStateService>();

// Сервисы с внедрением конфигурации
builder.Services.AddSingleton<FileLogger>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var logger = new FileLogger(configuration);
    logger.Info("FileLogger инициализирован");
    return logger;
});

// НОВЫЕ СЕРВИСЫ ДЛЯ СИНХРОНИЗАЦИИ С АДМИН-ПАНЕЛЬЮ
builder.Services.AddScoped<IVkUserService, VkUserService>();
builder.Services.AddScoped<IUserSyncService, UserSyncService>();

// Существующие сервисы
builder.Services.AddScoped<ICommandService, CommandService>();
builder.Services.AddScoped<IMessageService, MesService>();
builder.Services.AddScoped<IDataInitializer, DataInitializer>();

var app = builder.Build();

// Конфигурация middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "VK Bot API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.UseCors(MyAllowSpecificOrigins);
app.UseRouting();
app.UseAuthorization();
app.MapControllers();

// Инициализация базы данных
using (var scope = app.Services.CreateScope())
{
    try
    {
        var logger = scope.ServiceProvider.GetRequiredService<FileLogger>();
        logger.Info("🚀 Начинается инициализация базы данных...");

        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        dbContext.Database.EnsureCreated();
        logger.Info("✅ SQLite база данных создана успешно");

        var initializer = scope.ServiceProvider.GetRequiredService<IDataInitializer>();
        await initializer.InitializeAsync();
        logger.Info("✅ Инициализатор данных завершен");

        logger.Info("🎉 Бот успешно запущен и готов к работе!");

        // Проверяем настройки
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var adminUrl = config["AdminPanel:BaseUrl"];
        logger.Info($"🌐 Админ-панель: {(string.IsNullOrEmpty(adminUrl) ? "Не настроена" : adminUrl)}");
        logger.Info($"🔧 VK Token: {config["VkSettings:Token"]?.Substring(0, Math.Min(20, config["VkSettings:Token"]?.Length ?? 0))}...");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Ошибка инициализации базы данных: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
    }
}

app.Run();
