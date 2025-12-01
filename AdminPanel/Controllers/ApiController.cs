using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
public class ApiController : ControllerBase
{
    private readonly HttpClient _httpClient;
    private readonly BotApiSettings _botSettings;
    private readonly ILogger<ApiController> _logger;

    public ApiController(
        IHttpClientFactory httpClientFactory,
        IOptions<BotApiSettings> botSettings,
        ILogger<ApiController> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _botSettings = botSettings.Value;
        _logger = logger;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        try
        {
            _logger.LogInformation("📊 Запрос статистики от бота...");

            var response = await _httpClient.GetAsync($"{_botSettings.BaseUrl}/api/adminapi/stats");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"❌ Бот недоступен. Status: {response.StatusCode}");
                return StatusCode(502, new { error = "Бот недоступен", status = response.StatusCode });
            }

            var content = await response.Content.ReadAsStringAsync();
            _logger.LogInformation($"✅ Статистика получена");
            return Content(content, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка получения статистики от бота");
            return StatusCode(500, new { error = $"Ошибка: {ex.Message}" });
        }
    }

    [HttpPost("broadcast")]
    public async Task<IActionResult> Broadcast([FromBody] BroadcastRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.Message))
            {
                return BadRequest(new { error = "Сообщение не может быть пустым" });
            }

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(
                $"{_botSettings.BaseUrl}/api/adminapi/broadcast",
                content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"❌ Ошибка отправки рассылки. Status: {response.StatusCode}");
                return StatusCode(502, new { error = "Бот недоступен" });
            }

            _logger.LogInformation($"✅ Рассылка отправлена: {request.Message}");
            return Ok(new { success = true, message = "Рассылка отправлена" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка отправки рассылки");
            return StatusCode(500, new { error = $"Ошибка: {ex.Message}" });
        }
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_botSettings.BaseUrl}/api/adminapi/users");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"❌ Бот недоступен. Status: {response.StatusCode}");
                return StatusCode(502, new { error = "Бот недоступен" });
            }

            var content = await response.Content.ReadAsStringAsync();
            return Content(content, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка получения пользователей от бота");
            return StatusCode(500, new { error = $"Ошибка: {ex.Message}" });
        }
    }

    [HttpGet("commands-usage")]
    public async Task<IActionResult> GetCommandsUsage()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_botSettings.BaseUrl}/api/adminapi/commands-usage");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"❌ Бот недоступен. Status: {response.StatusCode}");
                return StatusCode(502, new { error = "Бот недоступен" });
            }

            var content = await response.Content.ReadAsStringAsync();
            return Content(content, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка получения статистики команд от бота");
            return StatusCode(500, new { error = $"Ошибка: {ex.Message}" });
        }
    }
}

public class BroadcastRequest
{
    public string? Message { get; set; }
}