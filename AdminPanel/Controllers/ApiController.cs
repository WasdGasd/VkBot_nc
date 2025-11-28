using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class ApiController : ControllerBase
{
    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        // Заглушка - потом заменим на реальные данные от бота
        var stats = new
        {
            TotalUsers = 150,
            ActiveUsers = 23,
            MessagesToday = 456,
            OnlineUsers = 12
        };
        return Ok(stats);
    }

    [HttpPost("broadcast")]
    public IActionResult Broadcast([FromBody] BroadcastRequest request)
    {
        // Заглушка - потом заменим на реальную отправку боту
        Console.WriteLine($"Сообщение для рассылки: {request.Message}");
        return Ok(new { success = true, message = "Рассылка отправлена (демо)" });
    }

    [HttpGet("users")]
    public IActionResult GetUsers()
    {
        // Заглушка - потом заменим на реальные данные от бота
        var users = new[]
        {
        new { Id = 1, Name = "Иван Иванов", Status = "active", LastActivity = "2024-01-15 14:30", MessageCount = 156 },
        new { Id = 2, Name = "Мария Петрова", Status = "active", LastActivity = "2024-01-15 13:45", MessageCount = 89 },
        new { Id = 3, Name = "Алексей Сидоров", Status = "inactive", LastActivity = "2024-01-14 10:20", MessageCount = 45 }
    };

        return Ok(users);
    }

    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        // Заглушка - потом заменим на реальные настройки
        var settings = new
        {
            BotName = "Мой VK Бот",
            GroupId = "123456789",
            AutoStart = true,
            NotifyNewUsers = true,
            NotifyErrors = true,
            WelcomeMessage = "Добро пожаловать! 👋",
            HelpMessage = "Для получения помощи введите /help",
            MessageLimit = 100
        };

        return Ok(settings);
    }

    [HttpPost("settings")]
    public IActionResult SaveSettings([FromBody] object settings)
    {
        // Заглушка - потом заменим на сохранение настроек
        Console.WriteLine("Сохранение настроек: " + settings);
        return Ok(new { success = true, message = "Настройки сохранены" });
    }
}

public class BroadcastRequest
{
    public string? Message { get; set; }
}