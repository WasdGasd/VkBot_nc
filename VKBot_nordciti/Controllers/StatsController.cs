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
                var dailyStats = await GetDailyStatsFromDbAsync();

                // 🔥 НОВОЕ: Получаем онлайн пользователей из таблицы Users
                int onlineUsersFromDb = await GetOnlineUsersFromDatabaseAsync();

                var response = new
                {
                    success = true,
                    message = "📊 Статистика из DailyStats таблицы",
                    data = new
                    {
                        totalUsers = dailyStats.TotalUsers,
                        activeUsers = dailyStats.ActiveUsers,
                        onlineUsers = onlineUsersFromDb, // ← ИЗ БД, а не из памяти!
                        messagesToday = dailyStats.MessagesCount,
                        totalCommands = commands.Values.Sum(),
                        commands = commands
                    },
                    source = "DAILY_STATS_TABLE_ONLY"
                };

                _logger.LogInformation($"Stats from DailyStats: Total={dailyStats.TotalUsers}, Active={dailyStats.ActiveUsers}, Online={onlineUsersFromDb}, Msgs={dailyStats.MessagesCount}");
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting stats");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // 🔥 НОВЫЙ метод: Получает онлайн пользователей из таблицы Users
        private async Task<int> GetOnlineUsersFromDatabaseAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_configuration.GetConnectionString("DefaultConnection"));
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM Users WHERE IsOnline = 1";

                return Convert.ToInt32(await command.ExecuteScalarAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения онлайн пользователей из БД");
                return 0;
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

        [HttpGet("weekly-messages")]
        public async Task<IActionResult> GetWeeklyMessagesStats()
        {
            try
            {
                // Вызов метода из сервиса
                var weeklyStats = await GetWeeklyMessagesFromDbAsync();

                return Ok(new
                {
                    success = true,
                    data = weeklyStats
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting weekly messages stats");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        private async Task<Dictionary<string, object>> GetWeeklyMessagesFromDbAsync()
        {
            var result = new Dictionary<string, object>();

            try
            {
                using var connection = new SqliteConnection(_configuration.GetConnectionString("DefaultConnection"));
                await connection.OpenAsync();

                // Определяем начало недели (понедельник)
                var today = DateTime.Today;
                var dayOfWeek = (int)today.DayOfWeek;

                // В C# DayOfWeek: Sunday=0, Monday=1, ..., Saturday=6
                // Нам нужно найти понедельник текущей недели
                var startOfWeek = today.AddDays(-(dayOfWeek == 0 ? 6 : dayOfWeek - 1));

                _logger.LogInformation($"📅 Неделя с {startOfWeek:dd.MM.yyyy} (понедельник)");

                // Получаем данные с понедельника по воскресенье текущей недели
                var command = connection.CreateCommand();
                command.CommandText = @"
            SELECT 
                Date,
                COALESCE(MessagesCount, 0) as MessagesCount
            FROM DailyStats 
            WHERE Date >= @startDate AND Date <= @endDate
            ORDER BY Date ASC";

                command.Parameters.AddWithValue("@startDate", startOfWeek.ToString("yyyy-MM-dd"));
                command.Parameters.AddWithValue("@endDate", startOfWeek.AddDays(6).ToString("yyyy-MM-dd")); // или startOfWeek.AddDays(6)

                var dateDataDict = new Dictionary<string, int>();

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var dateStr = reader.GetString(0);
                        var messagesCount = reader.GetInt32(1);

                        var date = DateTime.Parse(dateStr);
                        dateDataDict[date.ToString("dd.MM")] = messagesCount;
                    }
                }

                // Генерируем метки с понедельника по воскресенье
                var labels = new List<string>();
                var messagesData = new List<int>();

                // Проходим по всем дням недели
                for (int i = 0; i < 7; i++)
                {
                    var currentDate = startOfWeek.AddDays(i);
                    var dateKey = currentDate.ToString("dd.MM");

                    // Русские сокращения дней недели
                    var dayName = currentDate.DayOfWeek switch
                    {
                        DayOfWeek.Monday => "Пн",
                        DayOfWeek.Tuesday => "Вт",
                        DayOfWeek.Wednesday => "Ср",
                        DayOfWeek.Thursday => "Чт",
                        DayOfWeek.Friday => "Пт",
                        DayOfWeek.Saturday => "Сб",
                        DayOfWeek.Sunday => "Вс",
                        _ => "??"
                    };

                    labels.Add($"{dayName} {currentDate:dd}");

                    // Берем данные из БД или 0, если нет
                    if (dateDataDict.TryGetValue(dateKey, out var count))
                    {
                        messagesData.Add(count);
                    }
                    else
                    {
                        messagesData.Add(0);
                    }

                    _logger.LogInformation($"День недели: {dayName} {currentDate:dd} = {messagesData.Last()}");
                }

                result["labels"] = labels;
                result["messagesData"] = messagesData;

                _logger.LogInformation($"📊 Текущая неделя: {string.Join(" → ", labels)}");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting weekly messages from DB");
                result["labels"] = new List<string>();
                result["messagesData"] = new List<int>();
                return result;
            }
        }

        [HttpGet("users")]
        public IActionResult GetUsers()
        {
            try
            {
                // Получаем всех пользователей из статистики
                var userIds = _statsService.GetAllUserIds();

                return Ok(new
                {
                    success = true,
                    userIds = userIds,
                    count = userIds.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения списка пользователей");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("health")]
        public async Task<IActionResult> HealthCheck()
        {
            try
            {
                // 1. Проверяем базу данных
                using var connection = new SqliteConnection(_configuration.GetConnectionString("DefaultConnection"));
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table'";
                var tablesCount = Convert.ToInt32(await command.ExecuteScalarAsync());

                // 2. Проверяем таблицу команд
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Commands'";
                var hasCommandsTable = Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;

                // 3. Проверяем количество команд
                var commandsCount = 0;
                if (hasCommandsTable)
                {
                    command.CommandText = "SELECT COUNT(*) FROM Commands";
                    commandsCount = Convert.ToInt32(await command.ExecuteScalarAsync());
                }

                return Ok(new
                {
                    success = true,
                    status = "healthy",
                    timestamp = DateTime.Now,
                    database = new
                    {
                        connected = true,
                        tablesCount = tablesCount,
                        hasCommandsTable = hasCommandsTable,
                        commandsCount = commandsCount
                    },
                    api = new
                    {
                        version = "1.0.0",
                        uptime = GetUptime(),
                        endpoints = new[] { "memory", "database", "weekly-messages", "users", "health" }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
                return StatusCode(500, new
                {
                    success = false,
                    status = "unhealthy",
                    error = ex.Message,
                    timestamp = DateTime.Now
                });
            }
        }

        // Вспомогательный метод для получения uptime
        private static string GetUptime()
        {
            // Простая реализация - можно улучшить
            return "online";
        }

    }
    }
