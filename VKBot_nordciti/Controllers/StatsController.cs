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
                    data = new
                    {
                        general = new
                        {
                            totalUsers = stats.TotalUsers,
                            activeUsersToday = stats.ActiveUsersToday,
                            onlineUsers = stats.OnlineUsers,
                            messagesLastHour = stats.MessagesLastHour,
                            totalCommands = stats.TotalCommands
                        },
                        commands = commands
                    }
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
