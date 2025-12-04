using AdminPanel.Configs;
using AdminPanel.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Конфигурация
builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

// Логирование
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

// Сервисы
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = builder.Environment.IsDevelopment();
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// Кэширование
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 1024 * 1024;
    options.CompactionPercentage = 0.2;
    options.ExpirationScanFrequency = TimeSpan.FromMinutes(5);
});

// Защита данных
builder.Services.AddDataProtection()
    .SetApplicationName("AdminPanel")
    .PersistKeysToFileSystem(new DirectoryInfo(@"C:\keys\admin-panel"))
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

// Сессии
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.Name = ".AdminPanel.Session";
});

// Настройки
builder.Services.Configure<BotApiConfig>(builder.Configuration.GetSection("BotApi"));
builder.Services.Configure<DatabaseConfig>(builder.Configuration.GetSection("Database"));

// КОНФИГУРАЦИЯ ПУТЕЙ К БОТУ
builder.Services.Configure<BotPathsConfig>(options =>
{
    options.BotProjectPath = @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti";
    options.DatabaseName = "vkbot.db";
});

// HTTP клиенты
builder.Services.AddHttpClient("BotApi", client =>
{
    var baseUrl = builder.Configuration["BotApi:BaseUrl"] ?? "http://localhost:5000";
    var timeoutSeconds = int.TryParse(builder.Configuration["BotApi:TimeoutSeconds"], out var timeout)
        ? timeout : 30;

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    client.DefaultRequestHeaders.Add("User-Agent", "AdminPanel/1.0");
    client.DefaultRequestHeaders.Add("X-Admin-Panel", "true");
});

// Собственные сервисы - ВАЖНО: UserService должен быть Singleton для гарантированной инициализации
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<BotStatusService>();
builder.Services.AddScoped<CommandValidationService>();
builder.Services.AddSingleton<UserService>();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("Database");

var app = builder.Build();

// ДИАГНОСТИКА ПРИ ЗАПУСКЕ
try
{
    Console.WriteLine("\n=== ДИАГНОСТИКА ЗАПУСКА ===");

    using (var scope = app.Services.CreateScope())
    {
        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
        var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();

        // Проверяем базу данных
        var dbInfo = await dbService.GetDatabaseInfoAsync();
        Console.WriteLine($"1. База данных:");
        Console.WriteLine($"   • Путь: {dbInfo.ConnectionString}");
        Console.WriteLine($"   • Существует: {dbInfo.Exists}");
        Console.WriteLine($"   • Размер: {dbInfo.FileSizeKB} KB");

        // Пробуем получить пользователей
        Console.WriteLine($"2. Пробуем получить пользователей...");
        try
        {
            var usersResponse = await userService.GetUsersAsync(1, 5);
            Console.WriteLine($"   • Успешно! Пользователей: {usersResponse.TotalCount}");
            Console.WriteLine($"   • Загружено: {usersResponse.Users.Count}");

            if (usersResponse.Users.Count > 0)
            {
                Console.WriteLine($"   • Пример: {usersResponse.Users[0].FirstName} {usersResponse.Users[0].LastName}");
            }
            else
            {
                Console.WriteLine($"   • Пользователей нет, будет создан тестовый");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   • ОШИБКА: {ex.Message}");
        }
    }

    Console.WriteLine("=== ДИАГНОСТИКА ЗАВЕРШЕНА ===\n");
}
catch (Exception ex)
{
    Console.WriteLine($"ОШИБКА ДИАГНОСТИКИ: {ex}");
}

// Конвейер middleware
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

// Health check endpoint
app.MapHealthChecks("/health");

// Глобальная обработка ошибок
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        var exceptionHandlerPathFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var exception = exceptionHandlerPathFeature?.Error;

        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            message = "Внутренняя ошибка сервера",
            error = app.Environment.IsDevelopment() ? exception?.Message : null,
            path = exceptionHandlerPathFeature?.Path,
            timestamp = DateTime.UtcNow
        });
    });
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Dashboard}/{id?}");

app.Run();

// Конфигурационные классы
public class BotApiConfig
{
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
}

public class DatabaseConfig
{
    public string ConnectionString { get; set; } = string.Empty;
}

public class DatabaseHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var dbPath = @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db";

            if (System.IO.File.Exists(dbPath))
            {
                var fileInfo = new FileInfo(dbPath);
                if (fileInfo.Length > 0)
                {
                    return Task.FromResult(HealthCheckResult.Healthy("База данных доступна"));
                }
                return Task.FromResult(HealthCheckResult.Degraded("Файл БД пуст"));
            }
            return Task.FromResult(HealthCheckResult.Unhealthy("Файл БД не найден"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Ошибка проверки БД", ex));
        }
    }
}