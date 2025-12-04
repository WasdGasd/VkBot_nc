using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;
using AdminPanel.Models;

namespace AdminPanel.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ApiController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly BotApiConfig _botSettings;
        private readonly ILogger<ApiController> _logger;
        private readonly IConfiguration _configuration;

        public ApiController(
            IHttpClientFactory httpClientFactory,
            IOptions<BotApiConfig> botSettings,
            ILogger<ApiController> logger,
            IConfiguration configuration)
        {
            _httpClient = httpClientFactory.CreateClient("BotApi");
            _botSettings = botSettings.Value;
            _logger = logger;
            _configuration = configuration;

            // Настраиваем таймауты
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        // GET: api/stats
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var requestId = Guid.NewGuid();
            _logger.LogInformation("[{RequestId}] Запрос статистики от бота...", requestId);

            try
            {
                var response = await _httpClient.GetAsync($"{_botSettings.BaseUrl}/api/adminapi/stats");

                _logger.LogInformation("[{RequestId}] Статус ответа от бота: {StatusCode}",
                    requestId, response.StatusCode);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("[{RequestId}] Бот недоступен. Status: {StatusCode}",
                        requestId, response.StatusCode);

                    return StatusCode(502, new ApiResponse(
                        false,
                        "Бот недоступен",
                        new { status = (int)response.StatusCode, error = await GetErrorDetails(response) }
                    ));
                }

                var content = await response.Content.ReadAsStringAsync();

                try
                {
                    var stats = JsonSerializer.Deserialize<BotStats>(content);
                    _logger.LogInformation("[{RequestId}] Статистика получена: {TotalUsers} пользователей",
                        requestId, stats?.TotalUsers ?? 0);

                    return Ok(new ApiResponse(true, "Статистика получена", stats));
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "[{RequestId}] Ошибка десериализации статистики", requestId);
                    return Content(content, "application/json");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[{RequestId}] Ошибка соединения с ботом", requestId);
                return StatusCode(503, new ApiResponse(false, "Ошибка соединения с ботом", ex.Message));
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "[{RequestId}] Таймаут запроса к боту", requestId);
                return StatusCode(504, new ApiResponse(false, "Таймаут запроса к боту", ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{RequestId}] Неожиданная ошибка получения статистики", requestId);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}", null));
            }
        }

        // POST: api/broadcast
        [HttpPost("broadcast")]
        public async Task<IActionResult> Broadcast([FromBody] BroadcastRequest request)
        {
            var requestId = Guid.NewGuid();
            _logger.LogInformation("[{RequestId}] Отправка рассылки", requestId);

            try
            {
                // Валидация
                if (string.IsNullOrWhiteSpace(request.Message))
                {
                    return BadRequest(new ApiResponse(false, "Сообщение не может быть пустым"));
                }

                if (request.Message.Length > 4000)
                {
                    return BadRequest(new ApiResponse(false, "Сообщение не должно превышать 4000 символов"));
                }

                // Можно добавить проверку на спам
                if (IsSpamMessage(request.Message))
                {
                    return BadRequest(new ApiResponse(false, "Сообщение содержит запрещённые слова"));
                }

                var content = new StringContent(
                    JsonSerializer.Serialize(request),
                    System.Text.Encoding.UTF8,
                    "application/json");

                _logger.LogInformation("[{RequestId}] Отправка рассылки: {MessageLength} символов",
                    requestId, request.Message.Length);

                var response = await _httpClient.PostAsync(
                    $"{_botSettings.BaseUrl}/api/adminapi/broadcast",
                    content);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("[{RequestId}] Ошибка отправки рассылки. Status: {StatusCode}",
                        requestId, response.StatusCode);

                    return StatusCode(502, new ApiResponse(
                        false,
                        "Бот недоступен",
                        new { status = (int)response.StatusCode }
                    ));
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("[{RequestId}] Рассылка отправлена успешно", requestId);

                return Ok(new ApiResponse(true, "Рассылка отправлена", new { success = true }));
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[{RequestId}] Ошибка соединения при отправке рассылки", requestId);
                return StatusCode(503, new ApiResponse(false, "Ошибка соединения", ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{RequestId}] Ошибка отправки рассылки", requestId);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}", null));
            }
        }

        // GET: api/users
        [HttpGet("users")]
        public async Task<IActionResult> GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var requestId = Guid.NewGuid();
            _logger.LogInformation("[{RequestId}] Получение пользователей, страница {Page}",
                requestId, page);

            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_botSettings.BaseUrl}/api/adminapi/users?page={page}&pageSize={pageSize}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("[{RequestId}] Бот недоступен. Status: {StatusCode}",
                        requestId, response.StatusCode);

                    return StatusCode(502, new ApiResponse(false, "Бот недоступен"));
                }

                var content = await response.Content.ReadAsStringAsync();

                try
                {
                    var usersData = JsonSerializer.Deserialize<UserListResponse>(content);
                    _logger.LogInformation("[{RequestId}] Получено {Count} пользователей",
                        requestId, usersData?.Users?.Count ?? 0);

                    return Ok(new ApiResponse(true, "Пользователи получены", usersData));
                }
                catch (JsonException)
                {
                    return Content(content, "application/json");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{RequestId}] Ошибка получения пользователей", requestId);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}", null));
            }
        }

        // GET: api/commands-usage
        [HttpGet("commands-usage")]
        public async Task<IActionResult> GetCommandsUsage([FromQuery] string period = "today")
        {
            var requestId = Guid.NewGuid();
            _logger.LogInformation("[{RequestId}] Получение статистики команд, период: {Period}",
                requestId, period);

            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_botSettings.BaseUrl}/api/adminapi/commands-usage?period={period}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("[{RequestId}] Бот недоступен. Status: {StatusCode}",
                        requestId, response.StatusCode);

                    return StatusCode(502, new ApiResponse(false, "Бот недоступен"));
                }

                var content = await response.Content.ReadAsStringAsync();

                try
                {
                    var commandsData = JsonSerializer.Deserialize<CommandsUsageResponse>(content);
                    _logger.LogInformation("[{RequestId}] Получена статистика {Count} команд",
                        requestId, commandsData?.Commands?.Count ?? 0);

                    return Ok(new ApiResponse(true, "Статистика команд получена", commandsData));
                }
                catch (JsonException)
                {
                    return Content(content, "application/json");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{RequestId}] Ошибка получения статистики команд", requestId);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}", null));
            }
        }

        // GET: api/health
        [HttpGet("health")]
        public IActionResult Health()
        {
            var healthInfo = new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
                environment = _configuration["ASPNETCORE_ENVIRONMENT"] ?? "Development",
                services = new
                {
                    database = CheckDatabaseConnection(),
                    botApi = CheckBotApiConnection()
                }
            };

            return Ok(healthInfo);
        }

        // ==================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ====================

        private async Task<string> GetErrorDetails(HttpResponseMessage response)
        {
            try
            {
                var content = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(content) && content.Length < 500)
                {
                    return content;
                }
            }
            catch
            {
                // Игнорируем ошибки при чтении контента
            }
            return response.ReasonPhrase ?? "Unknown error";
        }

        private bool IsSpamMessage(string message)
        {
            // Простая проверка на спам (можно расширить)
            var spamWords = new[] { "http://", "https://", "www.", ".ru", ".com", "купить", "дешево" };
            return spamWords.Any(word =>
                message.Contains(word, StringComparison.OrdinalIgnoreCase));
        }

        private bool CheckDatabaseConnection()
        {
            try
            {
                var dbPath = _configuration.GetConnectionString("DefaultConnection") ??
                            @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db";

                return System.IO.File.Exists(dbPath);
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CheckBotApiConnection()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_botSettings.BaseUrl}/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }

    // ==================== МОДЕЛИ (только здесь) ====================

    public class BroadcastRequest
    {
        public string? Message { get; set; }
        public string? RecipientType { get; set; } = "all";
        public List<string>? UserIds { get; set; }
    }

    public class BotStats
    {
        public int TotalUsers { get; set; }
        public int ActiveUsers { get; set; }
        public int MessagesToday { get; set; }
        public int OnlineUsers { get; set; }
        public int NewUsersToday { get; set; }
        public int CommandsUsedToday { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    public class CommandsUsageResponse
    {
        public List<CommandUsage> Commands { get; set; } = new();
        public string Period { get; set; } = "today";
        public int TotalUsage { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class CommandUsage
    {
        public string Command { get; set; } = string.Empty;
        public int UsageCount { get; set; }
        public double Percentage { get; set; }
        public List<DateTime> RecentUses { get; set; } = new();
    }
}