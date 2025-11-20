using Microsoft.AspNetCore.Mvc;
using VKB_WA.Services;
using VKB_WA.Models;

namespace VKB_WA.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BotController : ControllerBase
    {
        private readonly BotHostedService _bot;
        private readonly CommandCacheService _cache;

        public BotController(BotHostedService bot, CommandCacheService cache)
        {
            _bot = bot;
            _cache = cache;
        }

        [HttpPost("start")]
        public IActionResult Start()
        {
            _bot.StartBot();
            return Ok();
        }

        [HttpPost("stop")]
        public IActionResult Stop()
        {
            _bot.StopBot();
            return Ok();
        }

        [HttpPost("reload")]
        public IActionResult Reload()
        {
            _bot.ReloadCommands(); // убрал параметр _cache
            return Ok();
        }

        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            var status = _bot.IsRunning ? "online" : "offline";
            return Ok(new { status });
        }

        [HttpGet("stats")]
        public IActionResult GetStats()
        {
            // Временная заглушка для статистики
            var stats = new BotStats
            {
                TotalUsers = 1250,
                ActiveUsers = 45,
                ActiveToday = 67,
                CommandsExecuted = 5678,
                MessagesProcessed = 12345,
                ErrorsToday = 3,
                Uptime = "24h 30m",
                StartTime = DateTime.Now.AddHours(-24).AddMinutes(-30)
            };

            return Ok(stats);
        }
    }
}