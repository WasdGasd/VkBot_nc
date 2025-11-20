using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using VKB_WA.Services;
using VKBD_nc.Data;
using VKBD_nc.Models;
using VKBot.Services;
using VKBot_nordciti.Config;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

//Swagger services
var swaggerConfig = new SwaggerConfig();

// Add services to the container.
ConfigurationManager configuration = builder.Configuration;
configuration.GetSection(nameof(SwaggerConfig)).Bind(swaggerConfig);

builder.Services.AddSwaggerGen(x =>
{
    x.SwaggerDoc("v1", new OpenApiInfo { Title = swaggerConfig.ApiDescription, Version = "v1" });
    var xmlFile = Path.ChangeExtension(typeof(Program).Assembly.Location, ".xml");
    x.IncludeXmlComments(xmlFile);
});

// Регистрация сервисов бота
builder.Services.AddHttpClient();
builder.Services.AddScoped<ErrorLogger>();
builder.Services.AddScoped<BotService>();
builder.Services.AddHostedService<BotHostedService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger(config => { config.RouteTemplate = swaggerConfig.JsonRoute; });

app.UseSwaggerUI(config =>
{
#if DEBUG
    // For Debug in Kestrel
    config.SwaggerEndpoint(swaggerConfig.UIEndpoint, swaggerConfig.ApiDescription);
#else
    // To deploy on IIS
    config.SwaggerEndpoint("/Api/CertService" + swaggerConfig.UIEndpoint, swaggerConfig.ApiDescription);
#endif
});
// ... остальной код
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
