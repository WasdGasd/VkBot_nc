using AdminPanel.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

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

// HTTP клиенты
builder.Services.AddHttpClient("BotApi", client =>
{
    var settings = builder.Configuration.GetSection("BotApi").Get<BotApiConfig>();
    client.BaseAddress = new Uri(settings?.BaseUrl ?? "http://localhost:5000");
    client.Timeout = TimeSpan.FromSeconds(settings?.TimeoutSeconds ?? 30);
    client.DefaultRequestHeaders.Add("User-Agent", "AdminPanel/1.0");
    client.DefaultRequestHeaders.Add("X-Admin-Panel", "true");
});

// Кэширование
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 1024;
    options.CompactionPercentage = 0.25;
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

// Собственные сервисы
builder.Services.AddSingleton<BotStatusService>();
builder.Services.AddSingleton<DatabaseService>(); // Добавьте эту строку если используете DatabaseService

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("Database");

var app = builder.Build();

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

public class DatabaseHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Путь к БД бота (в другом проекте)
            var connectionString = @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db";
            if (System.IO.File.Exists(connectionString))
            {
                var fileInfo = new FileInfo(connectionString);
                if (fileInfo.Length > 0)
                {
                    return Task.FromResult(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("База данных доступна"));
                }
            }
            return Task.FromResult(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("База данных недоступна"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Ошибка проверки БД", ex));
        }
    }
}