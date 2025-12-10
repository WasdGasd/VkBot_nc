using Microsoft.AspNetCore.Mvc;
using VKBot_nordciti.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace VKBot_nordciti.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StatsController : ControllerBase
    {
        private readonly IBotStatsService _statsService;
        private readonly ILogger<StatsController> _logger;
        private readonly IConfiguration _configuration;

        public StatsController(
            IBotStatsService statsService,
            ILogger<StatsController> logger,
            IConfiguration configuration)
        {
            _statsService = statsService;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet("memory")]
        public async Task<IActionResult> GetMemoryStats()
        {
            try
            {
                var stats = _statsService.GetStats();
                var commands = _statsService.GetCommandStatsFromDatabase();

                // ПОЛУЧАЕМ ДАННЫЕ ИЗ DailyStats ТАБЛИЦЫ!
                var dailyStats = await GetDailyStatsFromDbAsync();

                var response = new
                {
                    success = true,
                    message = "📊 Статистика из DailyStats таблицы",
                    data = new
                    {
                        // ВСЕ данные теперь из DailyStats таблицы!
                        totalUsers = dailyStats.TotalUsers,          // Из DailyStats.TotalUsers
                        activeUsers = dailyStats.ActiveUsers,        // Из DailyStats.ActiveUsers  
                        onlineUsers = stats.OnlineUsers,             // Из памяти бота (только это)
                        messagesToday = dailyStats.MessagesCount,    // Из DailyStats.MessagesCount
                        totalCommands = commands.Values.Sum(),       // Из CommandStats

                        commands = commands
                    },
                    source = "DAILY_STATS_TABLE_ONLY"
                };

                _logger.LogInformation($"Stats from DailyStats: Total={dailyStats.TotalUsers}, Active={dailyStats.ActiveUsers}, Msgs={dailyStats.MessagesCount}");
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting stats");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ЕДИНСТВЕННЫЙ метод для получения данных из DailyStats
        // ЕДИНСТВЕННЫЙ метод для получения данных из DailyStats
        private async Task<DailyStatsRecord> GetDailyStatsFromDbAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_configuration.GetConnectionString("DefaultConnection"));
                await connection.OpenAsync();

                var today = DateTime.Today.ToString("yyyy-MM-dd");

                var command = connection.CreateCommand();
                command.CommandText = @"
            SELECT 
                COALESCE(TotalUsers, 0) as TotalUsers,
                COALESCE(ActiveUsers, 0) as ActiveUsers,
                COALESCE(MessagesCount, 0) as MessagesCount
            FROM DailyStats 
            WHERE Date = @date";
                command.Parameters.AddWithValue("@date", today);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new DailyStatsRecord
                    {
                        TotalUsers = reader.GetInt32(0),
                        ActiveUsers = reader.GetInt32(1),
                        MessagesCount = reader.GetInt32(2)
                    };
                }
                else
                {
                    // Если записи нет - создаем пустую с нулевыми значениями
                    // Но активные пользователи берем ИЗ UserActivity
                    var activeUsersCommand = connection.CreateCommand();
                    activeUsersCommand.CommandText = "SELECT COUNT(DISTINCT UserId) FROM UserActivity WHERE ActivityDate = @date";
                    activeUsersCommand.Parameters.AddWithValue("@date", today);

                    var activeUsers = Convert.ToInt32(await activeUsersCommand.ExecuteScalarAsync());

                    // TotalUsers из Users таблицы
                    var totalUsersCommand = connection.CreateCommand();
                    totalUsersCommand.CommandText = "SELECT COUNT(DISTINCT VkUserId) FROM Users";
                    var totalUsers = Convert.ToInt32(await totalUsersCommand.ExecuteScalarAsync());

                    _logger.LogWarning($"No DailyStats record found for {today}, создаем с ActiveUsers={activeUsers} из UserActivity");

                    return new DailyStatsRecord
                    {
                        TotalUsers = totalUsers,
                        ActiveUsers = activeUsers,
                        MessagesCount = 0
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting DailyStats from DB");
                return new DailyStatsRecord();
            }
        }

        [HttpGet("database")]
        public IActionResult GetDatabaseStats([FromQuery] string? date = null)
        {
            try
            {
                var targetDate = date != null
                    ? DateTime.Parse(date)
                    : DateTime.Today;

                var dbCommands = _statsService.GetCommandStatsFromDatabase();

                return Ok(new
                {
                    success = true,
                    message = $"📊 Статистика из БД за {targetDate:dd.MM.yyyy}",
                    data = new
                    {
                        date = targetDate.ToString("yyyy-MM-dd"),
                        commands = dbCommands,
                        commandsCount = dbCommands.Values.Sum()
                    },
                    source = "DATABASE_ONLY"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting database stats");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("save")]
        public async Task<IActionResult> SaveStats()
        {
            try
            {
                await _statsService.SaveDailyStatsAsync();
                return Ok(new
                {
                    success = true,
                    message = "Статистика сохранена в БД"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving stats");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("save-daily")]
        public async Task<IActionResult> SaveDailyStats()
        {
            try
            {
                await _statsService.SaveDailyStatsAsync();
                return Ok(new
                {
                    success = true,
                    message = "Ежедневная статистика сохранена"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving daily stats");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Класс для хранения данных из DailyStats
        public class DailyStatsRecord
        {
            public int TotalUsers { get; set; }
            public int ActiveUsers { get; set; }
            public int MessagesCount { get; set; }
        }
    }
}