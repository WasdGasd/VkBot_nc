var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient(); // Добавляем HttpClient

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

// Добавляем маршруты для контроллеров
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Dashboard}/{id?}");

// Прямые маршруты для удобства
app.MapGet("/stats", () => Results.Redirect("/"));
app.MapGet("/broadcast", () => Results.Redirect("/home/broadcast"));
app.MapGet("/users", () => Results.Redirect("/home/users"));
app.MapGet("/settings", () => Results.Redirect("/home/settings"));

app.Run();