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

// HTTP клиент для VK API
builder.Services.AddHttpClient("VkApi", client =>
{
    client.BaseAddress = new Uri("https://api.vk.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "AdminPanel-VK/1.0");
});

// Собственные сервисы
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<BotStatusService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<CommandValidationService>();
builder.Services.AddSingleton<VkApiService>(); // Новый сервис VK API

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("Database");

var app = builder.Build();

// ДИАГНОСТИКА ПРИ ЗАПУСКЕ
try
{
    Console.WriteLine("\n=== ДИАГНОСТИКА ЗАПУСКА ===");

    // Объявляем переменную здесь, чтобы она была доступна во всей области видимости
    object? usersResponse = null;

    using (var scope = app.Services.CreateScope())
    {
        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
        var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
        var vkApiService = scope.ServiceProvider.GetRequiredService<VkApiService>();

        // Проверяем базу данных
        var dbInfo = await dbService.GetDatabaseInfoAsync();
        Console.WriteLine($"1. База данных:");
        Console.WriteLine($"   • Путь: {dbInfo.ConnectionString}");
        Console.WriteLine($"   • Существует: {dbInfo.Exists}");
        Console.WriteLine($"   • Размер: {dbInfo.FileSizeKB} KB");

        // Исправляем структуру базы данных
        Console.WriteLine($"2. Проверка и исправление структуры базы данных...");
        await userService.FixDatabaseSchema();

        // Проверяем VK API
        Console.WriteLine($"3. VK API:");
        Console.WriteLine($"   • Включен: {vkApiService.IsEnabled}");
        if (vkApiService.IsEnabled)
        {
            var accessToken = builder.Configuration["VkApi:AccessToken"];
            var groupId = builder.Configuration["VkApi:GroupId"];

            Console.WriteLine($"   • Токен: {(string.IsNullOrEmpty(accessToken) || accessToken == "YOUR_VK_API_TOKEN_HERE" ? "❌ Не настроен" : "✓ Есть")}");
            Console.WriteLine($"   • Группа: {(string.IsNullOrEmpty(groupId) || groupId == "YOUR_GROUP_ID" ? "❌ Не настроена" : "✓ Есть")}");

            // Пробуем получить количество бесед
            try
            {
                var conversationsCount = await vkApiService.GetTotalConversationsCountAsync();
                Console.WriteLine($"   • Бесед в VK: {conversationsCount}");

                if (conversationsCount > 0)
                {
                    var userIds = await vkApiService.GetAllUserIdsFromConversationsAsync();
                    Console.WriteLine($"   • Уникальных пользователей: {userIds.Count}");

                    if (userIds.Count > 0)
                    {
                        var vkUsers = await vkApiService.GetUsersInfoAsync(userIds.Take(3).ToList());
                        Console.WriteLine($"   • Примеры пользователей из VK:");
                        foreach (var vkUser in vkUsers.Take(3))
                        {
                            Console.WriteLine($"     • {vkUser.FirstName} {vkUser.LastName} (ID: {vkUser.Id}) {(vkUser.IsOnline ? "🟢 онлайн" : "⚫ офлайн")}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   • ОШИБКА VK API: {ex.Message}");
                Console.WriteLine($"   • Подсказка: проверьте access token и права доступа в VK");
            }
        }
        else
        {
            Console.WriteLine($"   • ⚠ ВНИМАНИЕ: VK API отключен");
            Console.WriteLine($"   • Для реального взаимодействия с пользователями VK:");
            Console.WriteLine($"     1. Получите access token для VK API");
            Console.WriteLine($"     2. В файле appsettings.json замените:");
            Console.WriteLine($"        - \"YOUR_VK_API_TOKEN_HERE\" на ваш токен");
            Console.WriteLine($"        - \"YOUR_GROUP_ID\" на ID вашей группы VK");
            Console.WriteLine($"     3. Добавьте ваши ID в \"AdminIds\"");
        }

        // Пробуем получить пользователей
        Console.WriteLine($"4. Тестирование UserService...");
        try
        {
            usersResponse = await userService.GetUsersAsync(1, 5);
            var totalCount = 0;
            var usersCount = 0;

            // Получаем свойства через рефлексию для безопасности
            var totalCountProp = usersResponse.GetType().GetProperty("TotalCount");
            var usersProp = usersResponse.GetType().GetProperty("Users");

            if (totalCountProp != null && usersProp != null)
            {
                totalCount = (int)totalCountProp.GetValue(usersResponse);
                var users = usersProp.GetValue(usersResponse) as System.Collections.IList;
                usersCount = users?.Count ?? 0;

                Console.WriteLine($"   • Успешно! Пользователей в базе: {totalCount}");
                Console.WriteLine($"   • Загружено для отображения: {usersCount}");

                if (usersCount > 0 && users != null)
                {
                    var firstUser = users[0];
                    var userType = firstUser.GetType();

                    var idProp = userType.GetProperty("Id");
                    var vkUserIdProp = userType.GetProperty("VkUserId");
                    var firstNameProp = userType.GetProperty("FirstName");
                    var lastNameProp = userType.GetProperty("LastName");
                    var usernameProp = userType.GetProperty("Username");
                    var emailProp = userType.GetProperty("Email");
                    var phoneProp = userType.GetProperty("Phone");
                    var statusProp = userType.GetProperty("Status");
                    var isActiveProp = userType.GetProperty("IsActive");
                    var isOnlineProp = userType.GetProperty("IsOnline");
                    var messageCountProp = userType.GetProperty("MessageCount");
                    var photoUrlProp = userType.GetProperty("PhotoUrl");

                    Console.WriteLine($"   • Пример пользователя:");
                    Console.WriteLine($"     • ID: {idProp?.GetValue(firstUser)}, VK ID: {vkUserIdProp?.GetValue(firstUser)}");
                    Console.WriteLine($"     • Имя: {firstNameProp?.GetValue(firstUser)} {lastNameProp?.GetValue(firstUser)}");
                    Console.WriteLine($"     • Username: {usernameProp?.GetValue(firstUser)}");
                    Console.WriteLine($"     • Email: {emailProp?.GetValue(firstUser)}");
                    Console.WriteLine($"     • Телефон: {phoneProp?.GetValue(firstUser)}");
                    Console.WriteLine($"     • Статус: {statusProp?.GetValue(firstUser)}, Активен: {isActiveProp?.GetValue(firstUser)}, Онлайн: {isOnlineProp?.GetValue(firstUser)}");
                    Console.WriteLine($"     • Сообщений: {messageCountProp?.GetValue(firstUser)}");
                    Console.WriteLine($"     • Фото: {(string.IsNullOrEmpty(photoUrlProp?.GetValue(firstUser) as string) ? "нет" : "есть")}");

                    var photoUrl = photoUrlProp?.GetValue(firstUser) as string;
                    if (!string.IsNullOrEmpty(photoUrl))
                    {
                        Console.WriteLine($"     • URL фото: {photoUrl}");
                    }
                }
                else
                {
                    Console.WriteLine($"   • Пользователей нет, будет создан тестовый при первом обращении");
                    Console.WriteLine($"   • Для создания тестовых пользователей откройте страницу пользователей");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   • ОШИБКА получения пользователей: {ex.Message}");
            Console.WriteLine($"   • Проверьте: {dbInfo.ConnectionString}");
            Console.WriteLine($"   • Файл существует: {dbInfo.Exists}");
        }

        // Проверяем VK API статус
        Console.WriteLine($"5. Проверка подключения к VK API...");
        if (vkApiService.IsEnabled)
        {
            try
            {
                var testUser = await vkApiService.GetUserInfoAsync(1); // ID 1 - это Павел Дуров
                if (testUser != null)
                {
                    Console.WriteLine($"   • ✓ Успешно подключились к VK API");
                    Console.WriteLine($"   • Тестовый запрос: {testUser.FirstName} {testUser.LastName}");
                }
                else
                {
                    Console.WriteLine($"   • ✗ Не удалось подключиться к VK API");
                    Console.WriteLine($"   • Проверьте access token и права доступа");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   • ✗ Ошибка подключения к VK API: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"   • ⚠ VK API отключен (режим демо)");
            Console.WriteLine($"   • Все функции работают в локальном режиме");
        }

        // Сводка
        Console.WriteLine($"\n=== СВОДКА ===");
        Console.WriteLine($"• База данных: {(dbInfo.Exists ? "✓" : "✗")}");
        Console.WriteLine($"• VK API: {(vkApiService.IsEnabled ? "✓ (режим реального взаимодействия)" : "⚡ (демо-режим)")}");

        // Безопасно получаем количество пользователей
        int userCount = 0;
        if (usersResponse != null)
        {
            var totalCountProp = usersResponse.GetType().GetProperty("TotalCount");
            if (totalCountProp != null)
            {
                userCount = (int)totalCountProp.GetValue(usersResponse);
            }
        }
        Console.WriteLine($"• Пользователей в базе: {userCount}");
        Console.WriteLine($"• Готовность: {(dbInfo.Exists ? "ГОТОВ К РАБОТЕ" : "ТРЕБУЕТСЯ НАСТРОЙКА")}");

        if (!vkApiService.IsEnabled)
        {
            Console.WriteLine($"\n⚠ РЕКОМЕНДАЦИИ:");
            Console.WriteLine($"1. Для реальной работы с VK пользователями настройте VK API");
            Console.WriteLine($"2. Откройте файл appsettings.json и замените значения:");
            Console.WriteLine($"   - \"YOUR_VK_API_TOKEN_HERE\" на ваш VK API токен");
            Console.WriteLine($"   - \"YOUR_GROUP_ID\" на ID вашей группы VK");
            Console.WriteLine($"3. Перезапустите приложение");
        }
    }

    Console.WriteLine("\n=== ДИАГНОСТИКА ЗАВЕРШЕНА ===");
    Console.WriteLine("Приложение запущено: http://localhost:5215");
    Console.WriteLine("Панель администратора: http://localhost:5215/users");
    Console.WriteLine("Нажмите Ctrl+C для остановки\n");
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ ОШИБКА ДИАГНОСТИКИ: {ex.Message}");
    Console.WriteLine($"Стек вызовов: {ex.StackTrace}");
    Console.WriteLine("\n⚠ РЕКОМЕНДАЦИИ ПО УСТРАНЕНИЮ:");
    Console.WriteLine("1. Проверьте наличие файла базы данных: C:\\Users\\kde\\source\\repos\\VkBot_nordciti\\VKBot_nordciti\\vkbot.db");
    Console.WriteLine("2. Проверьте права доступа к файлу базы данных");
    Console.WriteLine("3. Если файла нет - создайте пустой файл или скопируйте из бота");
    Console.WriteLine("4. Перезапустите приложение");
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

// API endpoint для диагностики
app.MapGet("/api/diagnostic", async context =>
{
    using var scope = app.Services.CreateScope();
    var userService = scope.ServiceProvider.GetRequiredService<UserService>();
    var vkApiService = scope.ServiceProvider.GetRequiredService<VkApiService>();

    var result = new
    {
        status = "running",
        timestamp = DateTime.UtcNow,
        database = new
        {
            exists = System.IO.File.Exists(@"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db"),
            path = @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db"
        },
        vkApi = new
        {
            enabled = vkApiService.IsEnabled,
            configured = !string.IsNullOrEmpty(builder.Configuration["VkApi:AccessToken"]) &&
                         builder.Configuration["VkApi:AccessToken"] != "YOUR_VK_API_TOKEN_HERE"
        },
        services = new
        {
            userService = "available",
            vkApiService = "available"
        }
    };

    await context.Response.WriteAsJsonAsync(result);
});

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

// Кастомный обработчик 404
app.UseStatusCodePages(async context =>
{
    if (context.HttpContext.Response.StatusCode == 404)
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            success = false,
            message = "Страница не найдена",
            path = context.HttpContext.Request.Path,
            timestamp = DateTime.UtcNow
        });
    }
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Dashboard}/{id?}");

// Запуск приложения
try
{
    await app.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ КРИТИЧЕСКАЯ ОШИБКА ПРИ ЗАПУСКЕ: {ex.Message}");
    Console.WriteLine($"Детали: {ex}");
    throw;
}

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
    private readonly IConfiguration _configuration;

    public DatabaseHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var dbPath = _configuration["ConnectionStrings:DefaultConnection"] ??
                        @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db";

            // Извлекаем путь из строки подключения
            if (dbPath.Contains("Data Source="))
            {
                var start = dbPath.IndexOf("Data Source=") + 12;
                var end = dbPath.IndexOf(';', start);
                if (end == -1) end = dbPath.Length;
                dbPath = dbPath.Substring(start, end - start);
            }

            if (System.IO.File.Exists(dbPath))
            {
                var fileInfo = new FileInfo(dbPath);
                if (fileInfo.Length > 0)
                {
                    return Task.FromResult(HealthCheckResult.Healthy($"База данных доступна ({fileInfo.Length / 1024} KB)"));
                }
                return Task.FromResult(HealthCheckResult.Degraded("Файл БД пуст"));
            }
            return Task.FromResult(HealthCheckResult.Unhealthy($"Файл БД не найден: {dbPath}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Ошибка проверки БД", ex));
        }
    }
}