using Microsoft.AspNetCore.Mvc;
using VKBot_nordciti.Services;
using Microsoft.Extensions.Logging;

namespace VKBot_nordciti.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StatsController : ControllerBase
    {
        private readonly IBotStatsService _statsService;
        private readonly ILogger<StatsController> _logger;

        public StatsController(IBotStatsService statsService, ILogger<StatsController> logger)
        {
            _statsService = statsService;
            _logger = logger;
        }

        [HttpGet("memory")]
        public IActionResult GetMemoryStats()
        {
            try
            {
                var stats = _statsService.GetStats();
                var commands = _statsService.GetCommandStats();

                var response = new
                {
                    success = true,
                    message = "📊 Статистика из памяти бота",
                    data = new
                    {
                        // Прямые поля для админ-панели
                        totalUsers = stats.TotalUsers,
                        activeUsers = stats.ActiveUsersToday,
                        onlineUsers = stats.OnlineUsers,
                        messagesToday = stats.TotalCommands, // или stats.TotalMessages если есть
                        totalCommands = stats.TotalCommands,

                        // Дополнительно для деталей
                        detailed = new
                        {
                            messagesLastHour = stats.MessagesLastHour,
                            uptime = stats.Uptime.ToString(@"dd\.hh\:mm\:ss"),
                            lastUpdate = stats.LastUpdate.ToString("yyyy-MM-dd HH:mm:ss")
                        },

                        // Команды для графика
                        commands = commands
                    },
                    source = "BOT_MEMORY"
                };

                _logger.LogInformation($"Stats API called: {stats.TotalUsers} users");
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting stats");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
