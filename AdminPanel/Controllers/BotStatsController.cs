using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AdminPanel.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BotStatsController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly BotApiConfig _botSettings;
        private readonly ILogger<BotStatsController> _logger;

        public BotStatsController(
            IHttpClientFactory httpClientFactory,
            IOptions<BotApiConfig> botSettings,
            ILogger<BotStatsController> logger)
        {
            _httpClient = httpClientFactory.CreateClient();
            _botSettings = botSettings.Value;
            _logger = logger;
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
        }

        // GET: api/BotStats/dashboard
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboardStats()
        {
            try
            {
                // 1. Пробуем получить от бота
                var response = await _httpClient.GetAsync($"{_botSettings.BaseUrl}/api/stats/memory");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<BotStatsResponse>(content);

                    return Ok(new
                    {
                        success = true,
                        message = "📊 Статистика из памяти бота",
                        data = new
                        {
                            totalUsers = result?.Data?.General?.TotalUsers ?? 0,
                            activeUsers = result?.Data?.General?.ActiveUsersToday ?? 0,
                            onlineUsers = result?.Data?.General?.OnlineUsers ?? 0,
                            messagesToday = result?.Data?.General?.MessagesLastHour ?? 0,
                            commandsToday = result?.Data?.Commands?.Values.Sum() ?? 0
                        },
                        source = "BOT_MEMORY"
                    });
                }

                // 2. Если бот недоступен
                return Ok(new
                {
                    success = false,
                    message = "Бот недоступен",
                    data = new
                    {
                        totalUsers = 0,
                        activeUsers = 0,
                        onlineUsers = 0,
                        messagesToday = 0,
                        commandsToday = 0
                    },
                    source = "BOT_OFFLINE"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения статистики");
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // GET: api/BotStats/commands
        [HttpGet("commands")]
        public async Task<IActionResult> GetCommandsStats()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_botSettings.BaseUrl}/api/stats/memory");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<BotStatsResponse>(content);

                    return Ok(new
                    {
                        success = true,
                        data = result?.Data?.Commands ?? new Dictionary<string, int>(),
                        source = "BOT_MEMORY"
                    });
                }

                return Ok(new
                {
                    success = false,
                    data = new Dictionary<string, int>(),
                    source = "BOT_OFFLINE"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения команд");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }

    // Модели для десериализации
    public class BotStatsResponse
    {
        public bool Success { get; set; }
        public BotStatsData? Data { get; set; }
    }

    public class BotStatsData
    {
        public BotStatsGeneral? General { get; set; }
        public Dictionary<string, int>? Commands { get; set; }
    }

    public class BotStatsGeneral
    {
        public int TotalUsers { get; set; }
        public int ActiveUsersToday { get; set; }
        public int OnlineUsers { get; set; }
        public int MessagesLastHour { get; set; }
        public int TotalCommands { get; set; }
    }
}
