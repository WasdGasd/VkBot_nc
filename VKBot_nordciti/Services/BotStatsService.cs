using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace VKBot_nordciti.Services
{
    public interface IBotStatsService
    {
        void RegisterUserMessage(long userId, string message);
        void RegisterBotMessage(long userId, string message);
        void RegisterCommandUsage(long userId, string command);
        void UpdateUserActivity(long userId, bool isOnline);

        BotStats GetStats();
        Dictionary<string, int> GetCommandStats();
        List<UserActivity> GetHourlyActivity();

        Dictionary<string, int> GetCommandStatsFromDatabase();
        Task SaveDailyStatsAsync();
        Task<Dictionary<string, int>> LoadCommandsFromDatabaseAsync();

        Task<Dictionary<string, object>> GetWeeklyMessagesStatsAsync();
    }

    // КЛАССЫ ВЫНОСИМ СЮДА, ВНЕ КЛАССА BotStatsService
    public class UserStat
    {
        public long UserId { get; set; }
        public int MessagesCount { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastActivity { get; set; }
    }

    public class BotStats
    {
        public int TotalUsers { get; set; }
        public int ActiveUsersToday { get; set; }
        public int OnlineUsers { get; set; }
        public int TotalMessages { get; set; }
        public int MessagesLastHour { get; set; }
        public int TotalCommands { get; set; }
        public TimeSpan Uptime { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    public class UserActivity
    {
        public string Time { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class BotStatsService : IBotStatsService
    {
        private readonly ConcurrentDictionary<long, UserStat> _userStats = new();
        private readonly ConcurrentDictionary<string, int> _commandStats = new();
        private readonly ConcurrentDictionary<int, int> _hourlyMessages = new();

        private readonly string _connectionString;
        private readonly ILogger<BotStatsService> _logger;
        private bool _databaseLoaded = false;

        private DateTime _startTime = DateTime.Now;
        private int _totalMessages = 0;
        private int _totalCommands = 0;

        public BotStatsService(IConfiguration configuration, ILogger<BotStatsService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
            _ = LoadCommandsFromDatabaseAsync();
        }

        public async Task<Dictionary<string, int>> LoadCommandsFromDatabaseAsync()
        {
            var commands = new Dictionary<string, int>();

            try
            {
                if (string.IsNullOrEmpty(_connectionString))
                    return commands;

                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var today = DateTime.Today.ToString("yyyy-MM-dd");

                var commandText = @"
                    SELECT CommandName, SUM(UsageCount) as TotalCount 
                    FROM CommandStats 
                    WHERE Date = @date 
                    GROUP BY CommandName";

                using var cmd = new SqliteCommand(commandText, connection);
                cmd.Parameters.AddWithValue("@date", today);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var commandName = reader.GetString(0);
                    var count = reader.GetInt32(1);

                    _commandStats[commandName] = count;
                    commands[commandName] = count;
                    _totalCommands += count;
                }

                _databaseLoaded = true;
                _logger.LogInformation($"Загружено {commands.Count} команд из БД за {today}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка загрузки команд из БД");
            }

            return commands;
        }

        public Dictionary<string, int> GetCommandStatsFromDatabase()
        {
            var commands = new Dictionary<string, int>();

            try
            {
                if (string.IsNullOrEmpty(_connectionString))
                    return commands;

                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var today = DateTime.Today.ToString("yyyy-MM-dd");

                var commandText = @"
                    SELECT CommandName, UsageCount 
                    FROM CommandStats 
                    WHERE Date = @date";

                using var cmd = new SqliteCommand(commandText, connection);
                cmd.Parameters.AddWithValue("@date", today);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    commands[reader.GetString(0)] = reader.GetInt32(1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения команд из БД");
            }

            return commands;
        }

        public void RegisterUserMessage(long userId, string message)
        {
            _totalMessages++;
            var hour = DateTime.Now.Hour;
            _hourlyMessages.AddOrUpdate(hour, 1, (_, count) => count + 1);

            _userStats.AddOrUpdate(userId,
                new UserStat
                {
                    UserId = userId,
                    MessagesCount = 1,
                    LastActivity = DateTime.Now,
                    IsOnline = true
                },
                (_, stat) =>
                {
                    stat.MessagesCount++;
                    stat.LastActivity = DateTime.Now;
                    stat.IsOnline = true;
                    return stat;
                });

            _ = UpdateDailyStatsInDatabaseAsync(userId);
        }

        public void RegisterBotMessage(long userId, string message)
        {
            _totalMessages++;
            var hour = DateTime.Now.Hour;
            _hourlyMessages.AddOrUpdate(hour, 1, (_, count) => count + 1);

            _ = UpdateDailyStatsInDatabaseAsync(userId);
        }

        public void RegisterCommandUsage(long userId, string command)
        {
            _totalCommands++;

            string normalizedCommand = command.ToLower().Trim();

            Console.WriteLine($"🔍 Original command: '{command}'");
            Console.WriteLine($"🔍 Normalized: '{normalizedCommand}'");

            if (normalizedCommand.Contains("??") || normalizedCommand.Contains("ℹ?"))
            {
                if (normalizedCommand.Contains("?? к информации") || normalizedCommand.Contains("ℹ? к информации"))
                {
                    normalizedCommand = "информация";
                }
                else if (normalizedCommand.Contains("?? время работы") || normalizedCommand.Contains("🕒"))
                {
                    normalizedCommand = "время_работы";
                }
                else if (normalizedCommand.Contains("?? главное меню") || normalizedCommand.Contains("?? назад"))
                {
                    normalizedCommand = "назад";
                }
                else if (normalizedCommand.Contains("?? билеты") || normalizedCommand.Contains("🎫"))
                {
                    normalizedCommand = "билеты";
                }
                else if (normalizedCommand.Contains("📊") || normalizedCommand.Contains("загруженность"))
                {
                    normalizedCommand = "загруженность";
                }
                else
                {
                    normalizedCommand = "кнопка";
                }
            }
            else if (normalizedCommand.Contains("🔙") ||
                     normalizedCommand.Contains("📅") ||
                     normalizedCommand.Contains("📊") ||
                     normalizedCommand.Contains("ℹ️") ||
                     normalizedCommand.Contains("🎫") ||
                     normalizedCommand.Contains("🕒") ||
                     normalizedCommand.Contains("📞") ||
                     normalizedCommand.Contains("📍") ||
                     normalizedCommand.Contains("🎯") ||
                     normalizedCommand.Contains("💳") ||
                     normalizedCommand.Contains("👤") ||
                     normalizedCommand.Contains("👶"))
            {
                normalizedCommand = RemoveEmojis(normalizedCommand).Trim();

                if (string.IsNullOrEmpty(normalizedCommand))
                {
                    normalizedCommand = "кнопка";
                }
                else
                {
                    normalizedCommand = normalizedCommand.Replace("button_", "");
                }
            }
            else if (normalizedCommand.StartsWith("/") ||
                     normalizedCommand.Contains("билет") ||
                     normalizedCommand.Contains("загруженность") ||
                     normalizedCommand.Contains("информация") ||
                     normalizedCommand.Contains("начать") ||
                     normalizedCommand.Contains("меню") ||
                     normalizedCommand.Contains("помощь") ||
                     normalizedCommand.Contains("время") ||
                     normalizedCommand.Contains("контакт"))
            {
                if (normalizedCommand.Contains("время"))
                {
                    normalizedCommand = "время_работы";
                }
                else if (normalizedCommand.Contains("контакт"))
                {
                    normalizedCommand = "контакты";
                }
            }
            else
            {
                normalizedCommand = "сообщение";
            }

            Console.WriteLine($"📊 Final command: '{normalizedCommand}'");

            _commandStats.AddOrUpdate(normalizedCommand, 1, (_, count) => count + 1);
            _ = SaveCommandToDatabaseAsync(normalizedCommand);

            // ВАЖНО: Вызываем обновление DailyStats
            _ = UpdateDailyStatsInDatabaseAsync(userId);

            _userStats.AddOrUpdate(userId,
                new UserStat
                {
                    UserId = userId,
                    LastActivity = DateTime.Now,
                    IsOnline = true
                },
                (_, stat) =>
                {
                    stat.LastActivity = DateTime.Now;
                    stat.IsOnline = true;
                    return stat;
                });
        }

        private async Task SaveCommandToDatabaseAsync(string command)
        {
            try
            {
                if (string.IsNullOrEmpty(_connectionString))
                    return;

                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var today = DateTime.Today.ToString("yyyy-MM-dd");

                var commandText = @"
                    INSERT INTO CommandStats (CommandName, UsageCount, Date)
                    VALUES (@command, 1, @date)
                    ON CONFLICT(CommandName, Date) DO UPDATE SET
                    UsageCount = UsageCount + 1";

                using var cmd = new SqliteCommand(commandText, connection);
                cmd.Parameters.AddWithValue("@command", command);
                cmd.Parameters.AddWithValue("@date", today);

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка сохранения команды '{command}' в БД");
            }
        }

        private string RemoveEmojis(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            string[] emojis = {
                "🔙", "📅", "📊", "ℹ️", "🎫", "🕒", "📞", "📍", "🎯", "💳", "👤", "👶",
                "??", "ℹ?", "🕒?", "📊?", "🎫?"
            };

            foreach (var emoji in emojis)
            {
                text = text.Replace(emoji, "");
            }

            text = text.Replace(" к информации", " информация");
            text = text.Replace(" время работы", " время_работы");
            text = text.Replace(" главное меню", " назад");

            return text.Trim();
        }

        public void UpdateUserActivity(long userId, bool isOnline)
        {
            _userStats.AddOrUpdate(userId,
                new UserStat { UserId = userId, IsOnline = isOnline, LastActivity = DateTime.Now },
                (_, stat) =>
                {
                    stat.IsOnline = isOnline;
                    stat.LastActivity = DateTime.Now;
                    return stat;
                });
        }

        public BotStats GetStats()
        {
            var now = DateTime.Now;
            var today = DateTime.Today;

            var activeToday = _userStats.Values.Count(u => u.LastActivity.Date == today);
            var onlineNow = _userStats.Values.Count(u => u.IsOnline);

            var lastHour = (now.Hour - 1 + 24) % 24;
            var messagesLastHour = _hourlyMessages.TryGetValue(lastHour, out var count) ? count : 0;

            var commandsToday = _commandStats.Values.Sum();

            return new BotStats
            {
                TotalUsers = _userStats.Count,
                ActiveUsersToday = activeToday,
                OnlineUsers = onlineNow,
                TotalMessages = _totalMessages,
                MessagesLastHour = messagesLastHour,
                TotalCommands = _totalCommands,
                Uptime = now - _startTime,
                LastUpdate = now
            };
        }

        public Dictionary<string, int> GetCommandStats()
        {
            return new Dictionary<string, int>(_commandStats);
        }

        public List<UserActivity> GetHourlyActivity()
        {
            var result = new List<UserActivity>();
            var now = DateTime.Now;

            for (int i = 23; i >= 0; i--)
            {
                var hour = (now.Hour - i + 24) % 24;
                var count = _hourlyMessages.TryGetValue(hour, out var c) ? c : 0;

                result.Add(new UserActivity
                {
                    Time = $"{hour:00}:00",
                    Count = count
                });
            }

            return result;
        }

        public async Task SaveDailyStatsAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_connectionString))
                    return;

                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var today = DateTime.Today.ToString("yyyy-MM-dd");

                // Получаем TotalUsers
                var totalUsersCommand = connection.CreateCommand();
                totalUsersCommand.CommandText = "SELECT COUNT(DISTINCT VkUserId) FROM Users";
                var totalUsers = Convert.ToInt32(await totalUsersCommand.ExecuteScalarAsync());

                // Получаем ActiveUsers ИЗ UserActivity таблицы
                var activeUsersCommand = connection.CreateCommand();
                activeUsersCommand.CommandText = "SELECT COUNT(DISTINCT UserId) FROM UserActivity WHERE ActivityDate = @date";
                activeUsersCommand.Parameters.AddWithValue("@date", today);
                var activeUsers = Convert.ToInt32(await activeUsersCommand.ExecuteScalarAsync());

                // Получаем MessagesCount из DailyStats
                var dailyCommand = connection.CreateCommand();
                dailyCommand.CommandText = "SELECT MessagesCount FROM DailyStats WHERE Date = @date";
                dailyCommand.Parameters.AddWithValue("@date", today);

                int messagesCount = 0;

                using (var reader = await dailyCommand.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        messagesCount = reader.GetInt32(0);
                    }
                }

                // Команд сегодня
                var commandsCommand = connection.CreateCommand();
                commandsCommand.CommandText = "SELECT SUM(UsageCount) FROM CommandStats WHERE Date = @date";
                commandsCommand.Parameters.AddWithValue("@date", today);
                var commandsResult = await commandsCommand.ExecuteScalarAsync();
                var commandsCount = commandsResult != DBNull.Value ? Convert.ToInt32(commandsResult) : 0;

                // Сохраняем в DailyStats
                var insertCommand = connection.CreateCommand();
                insertCommand.CommandText = @"
            INSERT OR REPLACE INTO DailyStats 
            (Date, TotalUsers, ActiveUsers, MessagesCount, CommandsCount)
            VALUES (@date, @totalUsers, @activeUsers, @messagesCount, @commandsCount)";

                insertCommand.Parameters.AddWithValue("@date", today);
                insertCommand.Parameters.AddWithValue("@totalUsers", totalUsers);
                insertCommand.Parameters.AddWithValue("@activeUsers", activeUsers);
                insertCommand.Parameters.AddWithValue("@messagesCount", messagesCount);
                insertCommand.Parameters.AddWithValue("@commandsCount", commandsCount);

                await insertCommand.ExecuteNonQueryAsync();

                _logger.LogInformation($"📊 DailyStats сохранены: Total={totalUsers}, Active={activeUsers}, Msgs={messagesCount}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сохранения DailyStats");
            }
        }

        // НОВЫЙ МЕТОД: Обновляет DailyStats при каждом сообщении
        private async Task UpdateDailyStatsInDatabaseAsync(long userId)
        {
            try
            {
                if (string.IsNullOrEmpty(_connectionString))
                    return;

                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var today = DateTime.Today.ToString("yyyy-MM-dd");
                var now = DateTime.Now;

                // 1. ВСЕГДА берем TotalUsers из Users таблицы
                var totalUsersCommand = connection.CreateCommand();
                totalUsersCommand.CommandText = "SELECT COUNT(DISTINCT VkUserId) FROM Users";
                var totalUsers = Convert.ToInt32(await totalUsersCommand.ExecuteScalarAsync());

                // 2. Проверяем DailyStats за сегодня
                var checkCommand = connection.CreateCommand();
                checkCommand.CommandText = "SELECT MessagesCount FROM DailyStats WHERE Date = @date";
                checkCommand.Parameters.AddWithValue("@date", today);

                int messagesCount = 0;

                using (var reader = await checkCommand.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        messagesCount = reader.GetInt32(0);
                    }
                }

                // 3. Увеличиваем счетчик сообщений
                messagesCount++;

                // 4. Проверяем/добавляем активность пользователя в UserActivity
                var userActivityCheckCommand = connection.CreateCommand();
                userActivityCheckCommand.CommandText = @"
            SELECT COUNT(*) FROM UserActivity 
            WHERE UserId = @userId AND ActivityDate = @date";
                userActivityCheckCommand.Parameters.AddWithValue("@userId", userId);
                userActivityCheckCommand.Parameters.AddWithValue("@date", today);

                var isUserAlreadyActive = Convert.ToInt32(await userActivityCheckCommand.ExecuteScalarAsync()) > 0;

                // 5. Если пользователь еще не активен сегодня - добавляем в UserActivity
                if (!isUserAlreadyActive)
                {
                    var insertActivityCommand = connection.CreateCommand();
                    insertActivityCommand.CommandText = @"
                INSERT INTO UserActivity (UserId, ActivityDate, FirstActivity, LastActivity)
                VALUES (@userId, @date, @now, @now)";
                    insertActivityCommand.Parameters.AddWithValue("@userId", userId);
                    insertActivityCommand.Parameters.AddWithValue("@date", today);
                    insertActivityCommand.Parameters.AddWithValue("@now", now);
                    await insertActivityCommand.ExecuteNonQueryAsync();

                    _logger.LogInformation($"✅ Пользователь {userId} добавлен в UserActivity");
                }
                else
                {
                    // Обновляем LastActivity если пользователь уже активен
                    var updateActivityCommand = connection.CreateCommand();
                    updateActivityCommand.CommandText = @"
                UPDATE UserActivity 
                SET LastActivity = @now 
                WHERE UserId = @userId AND ActivityDate = @date";
                    updateActivityCommand.Parameters.AddWithValue("@userId", userId);
                    updateActivityCommand.Parameters.AddWithValue("@date", today);
                    updateActivityCommand.Parameters.AddWithValue("@now", now);
                    await updateActivityCommand.ExecuteNonQueryAsync();
                }

                // 6. Получаем количество активных пользователей ИЗ UserActivity
                var activeUsersCommand = connection.CreateCommand();
                activeUsersCommand.CommandText = "SELECT COUNT(DISTINCT UserId) FROM UserActivity WHERE ActivityDate = @date";
                activeUsersCommand.Parameters.AddWithValue("@date", today);

                int activeUsers = Convert.ToInt32(await activeUsersCommand.ExecuteScalarAsync());

                // 7. Сохраняем в DailyStats
                var saveCommand = connection.CreateCommand();
                saveCommand.CommandText = @"
            INSERT OR REPLACE INTO DailyStats 
            (Date, TotalUsers, ActiveUsers, MessagesCount)
            VALUES (@date, @totalUsers, @activeUsers, @messagesCount)";

                saveCommand.Parameters.AddWithValue("@date", today);
                saveCommand.Parameters.AddWithValue("@totalUsers", totalUsers);
                saveCommand.Parameters.AddWithValue("@activeUsers", activeUsers);
                saveCommand.Parameters.AddWithValue("@messagesCount", messagesCount);

                await saveCommand.ExecuteNonQueryAsync();

                _logger.LogInformation($"📊 DailyStats обновлены: Total={totalUsers}, Active={activeUsers}, Msgs={messagesCount}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обновления DailyStats");
            }
        }

        // ==================== НОВЫЙ МЕТОД: Получает статистику сообщений за неделю ====================
        public async Task<Dictionary<string, object>> GetWeeklyMessagesStatsAsync()
        {
            var result = new Dictionary<string, object>();

            try
            {
                if (string.IsNullOrEmpty(_connectionString))
                    return result;

                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Получаем данные за последние 7 дней
                var command = connection.CreateCommand();
                command.CommandText = @"
            SELECT 
                Date,
                COALESCE(MessagesCount, 0) as MessagesCount
            FROM DailyStats 
            WHERE Date >= date('now', '-6 days')
            ORDER BY Date ASC";

                var labels = new List<string>();
                var messagesData = new List<int>();

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var dateStr = reader.GetString(0);
                        var messagesCount = reader.GetInt32(1);

                        // Форматируем дату для отображения
                        var date = DateTime.Parse(dateStr);
                        labels.Add(date.ToString("dd.MM"));  // "01.03"
                        messagesData.Add(messagesCount);
                    }
                }

                // Если данных меньше 7 дней, заполняем нулями
                var today = DateTime.Today;
                for (int i = labels.Count; i < 7; i++)
                {
                    var date = today.AddDays(-(6 - i));
                    labels.Add(date.ToString("dd.MM"));
                    messagesData.Add(0);
                }

                result["labels"] = labels;
                result["messagesData"] = messagesData;
                result["totalMessages"] = messagesData.Sum();
                result["averageMessages"] = messagesData.Count > 0 ? messagesData.Average() : 0;
                result["maxMessages"] = messagesData.Count > 0 ? messagesData.Max() : 0;

                _logger.LogInformation($"📈 Получена недельная статистика: {messagesData.Sum()} сообщений за 7 дней");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения недельной статистики");
                return result;
            }
        }

    }
}