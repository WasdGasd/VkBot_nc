using Microsoft.AspNetCore.Mvc;
using VKB_WA.Models;
using System.Diagnostics;
using VKB_WA.Services;

namespace VKB_WA.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StatsController : ControllerBase
    {
        private readonly BotService _botService;
        private static readonly Random _random = new Random();

        public StatsController(BotService botService)
        {
            _botService = botService;
        }

        [HttpGet("commands")]
        public IActionResult GetCommandStats()
        {
            // РЕАЛЬНЫЕ ДАННЫЕ ИЗ BOT SERVICE
            var stats = _botService.GetCommandStats();
            return Ok(stats);
        }

        [HttpGet("users")]
        public IActionResult GetUserStats()
        {
            var stats = new UserStats
            {
                TotalUsers = _botService.GetOnlineUsersCount(),
                ActiveToday = _botService.GetActiveUsersToday(),
                HourlyActivity = GenerateHourlyActivity()
            };

            return Ok(stats);
        }

        [HttpGet("system")]
        public IActionResult GetSystemStats()
        {
            var process = Process.GetCurrentProcess();
            var stats = new SystemStats
            {
                ResponseTime = "124ms",
                MemoryUsage = $"{Math.Round((double)process.WorkingSet64 / 1024 / 1024, 1)} MB",
                CpuLoad = "23%",
                Uptime = $"{DateTime.Now - _botService.GetStartTime():h\'h \'m\'m\'}"
            };

            return Ok(stats);
        }

        [HttpGet("live")]
        public IActionResult GetLiveStats()
        {
            // РЕАЛЬНЫЕ ДАННЫЕ ИЗ BOT SERVICE
            var stats = _botService.GetLiveStats();
            return Ok(stats);
        }

        [HttpPost("simulate")]
        public IActionResult SimulateActivity()
        {
            return Ok(new
            {
                Message = "Симуляция отключена. Используйте реального бота для статистики"
            });
        }

        private List<CommandUsage> GenerateDailyUsage()
        {
            var days = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            var usage = new List<CommandUsage>();

            foreach (var day in days)
            {
                usage.Add(new CommandUsage
                {
                    Date = day,
                    Count = _random.Next(50, 150)
                });
            }

            return usage;
        }

        private List<UserActivity> GenerateHourlyActivity()
        {
            var hours = new[] { "00:00", "04:00", "08:00", "12:00", "16:00", "20:00" };
            var activity = new List<UserActivity>();

            foreach (var hour in hours)
            {
                activity.Add(new UserActivity
                {
                    Time = hour,
                    Count = _random.Next(5, 25)
                });
            }

            return activity;
        }
    }
}