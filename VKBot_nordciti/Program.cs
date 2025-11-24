using Services;
using VK;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(opt =>
{
    opt.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";


builder.Services.AddCors(options =>
{
    options.AddPolicy(name: MyAllowSpecificOrigins,
                      policy =>
                      {
                          policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .WithMethods("POST", "GET", "PUT", "DELETE", "UPDATE")
                          .SetPreflightMaxAge(TimeSpan.FromSeconds(5))
                          .Build();
                      });
});

// Http clients and services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<VkApiManager>();
builder.Services.AddSingleton<KeyboardProvider>();
builder.Services.AddSingleton<ConversationStateService>();
builder.Services.AddSingleton<FileLogger>();
builder.Services.AddScoped<IMessageService, MessageService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.UseCors(MyAllowSpecificOrigins);
//app.Run("http://0.0.0.0:5000");

app.Run();
