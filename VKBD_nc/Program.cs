using Microsoft.EntityFrameworkCore;
using VKBD_nc.Data;
using VKBot_nordciti.Services;
using VKBot_nordciti.VK;

var builder = WebApplication.CreateBuilder(args);

// 1. Добавляем контроллеры
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

// Сервисы с конфигурацией
builder.Services.AddSingleton<VkApiManager>(provider =>
{
    var httpClient = provider.GetRequiredService<HttpClient>();
    var configuration = provider.GetRequiredService<IConfiguration>();
    var logger = provider.GetRequiredService<ILogger<VkApiManager>>();
    return new VkApiManager(httpClient, configuration, logger);
});

builder.Services.AddSingleton<KeyboardProvider>();
builder.Services.AddSingleton<ConversationStateService>();
builder.Services.AddSingleton<FileLogger>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    return new FileLogger(configuration);
});

// Сервисы из второго проекта
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
        var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        dbContext.Database.EnsureCreated();
        Console.WriteLine("SQLite database created successfully");

        var initializer = scope.ServiceProvider.GetRequiredService<IDataInitializer>();
        await initializer.InitializeAsync();
        Console.WriteLine("Data initializer completed");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database initialization failed: {ex.Message}");
    }
}

app.Run();