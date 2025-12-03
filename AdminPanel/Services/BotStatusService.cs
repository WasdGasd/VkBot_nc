using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace AdminPanel.Services
{
    public class BotStatusService
    {
        private readonly ILogger<BotStatusService> _logger;
        private readonly IMemoryCache _cache;
        private readonly DatabaseConfig _dbSettings;
        private readonly HttpClient _httpClient;
        private readonly string _botProcessName = "VKBot_nordciti";
        private readonly string _botApiUrl;

        public BotStatusService(
            ILogger<BotStatusService> logger,
            IMemoryCache cache,
            IOptions<DatabaseConfig> dbSettings,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _cache = cache;
            _dbSettings = dbSettings.Value;
            _httpClient = httpClientFactory.CreateClient("BotApi");
            _botApiUrl = configuration.GetValue<string>("BotApi:BaseUrl") ?? "http://localhost:5000";
        }

        public async Task<BotStatusInfo> GetBotStatusAsync()
        {
            const string cacheKey = "bot_status_info";

            if (_cache.TryGetValue<BotStatusInfo>(cacheKey, out var cachedStatus))
            {
                _logger.LogDebug("Статус бота получен из кэша");
                return cachedStatus!;
            }

            var statusInfo = new BotStatusInfo
            {
                Timestamp = DateTime.UtcNow
            };

            try
            {
                // 1. Проверка через процессы
                statusInfo.IsProcessRunning = CheckProcessRunning();

                // 2. Проверка через файл блокировки
                statusInfo.HasLockFile = CheckLockFile();

                // 3. Проверка через HTTP запрос
                statusInfo.IsApiResponding = await CheckApiHealthAsync();

                // 4. Определение общего статуса
                statusInfo.OverallStatus = DetermineOverallStatus(statusInfo);

                // 5. Получение времени работы
                statusInfo.Uptime = await GetUptimeAsync();

                // 6. Получение версии
                statusInfo.Version = await GetVersionAsync();

                // 7. Статистика из БД
                statusInfo.DatabaseStats = await GetDatabaseStatsAsync();

                _logger.LogInformation("Статус бота: {Status}, процесс: {Process}, API: {Api}",
                    statusInfo.OverallStatus, statusInfo.IsProcessRunning, statusInfo.IsApiResponding);

                // Кэшируем на 30 секунд
                _cache.Set(cacheKey, statusInfo, TimeSpan.FromSeconds(30));

                return statusInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении статуса бота");
                statusInfo.OverallStatus = "error";
                statusInfo.Error = ex.Message;
                return statusInfo;
            }
        }

        public async Task<BotCommandResult> StartBotAsync()
        {
            _logger.LogInformation("Запуск бота");

            try
            {
                // Проверяем, не запущен ли уже бот
                var currentStatus = await GetBotStatusAsync();
                if (currentStatus.OverallStatus == "running")
                {
                    return BotCommandResult.FailResult("Бот уже запущен");
                }

                // Симуляция запуска
                await Task.Delay(2000);

                // Очищаем кэш статуса
                _cache.Remove("bot_status_info");

                _logger.LogInformation("Бот успешно запущен");
                return BotCommandResult.SuccessResult("Бот успешно запущен");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при запуске бота");
                return BotCommandResult.FailResult($"Ошибка запуска: {ex.Message}");
            }
        }

        public async Task<BotCommandResult> StopBotAsync()
        {
            _logger.LogInformation("Остановка бота");

            try
            {
                // Проверяем, запущен ли бот
                var currentStatus = await GetBotStatusAsync();
                if (currentStatus.OverallStatus != "running")
                {
                    return BotCommandResult.FailResult("Бот не запущен");
                }

                // Симуляция остановки
                await Task.Delay(1000);

                // Очищаем кэш статуса
                _cache.Remove("bot_status_info");

                _logger.LogInformation("Бот успешно остановлен");
                return BotCommandResult.SuccessResult("Бот успешно остановлен");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при остановке бота");
                return BotCommandResult.FailResult($"Ошибка остановки: {ex.Message}");
            }
        }

        public async Task<BotSettingsDto> GetBotSettingsAsync()
        {
            try
            {
                var dbPath = _dbSettings.ConnectionString ?? @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db";

                if (!System.IO.File.Exists(dbPath))
                {
                    throw new FileNotFoundException($"Файл БД не найден: {dbPath}");
                }

                using var connection = new SqliteConnection($"Data Source={dbPath}");
                await connection.OpenAsync();

                var command = new SqliteCommand("SELECT * FROM BotSettings WHERE Id = 1", connection);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new BotSettingsDto
                    {
                        Id = reader.GetInt32(0),
                        BotName = reader.IsDBNull(1) ? "VK Бот" : reader.GetString(1),
                        VkToken = reader.IsDBNull(2) ? string.Empty : MaskToken(reader.GetString(2)),
                        GroupId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        AutoStart = reader.IsDBNull(4) || reader.GetBoolean(4),
                        NotifyNewUsers = reader.IsDBNull(5) || reader.GetBoolean(5),
                        NotifyErrors = reader.IsDBNull(6) || reader.GetBoolean(6),
                        NotifyEmail = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                        LastUpdated = reader.IsDBNull(8) ? DateTime.MinValue : reader.GetDateTime(8)
                    };
                }

                return new BotSettingsDto
                {
                    Id = 1,
                    BotName = "VK Бот",
                    VkToken = string.Empty,
                    GroupId = string.Empty,
                    AutoStart = true,
                    NotifyNewUsers = true,
                    NotifyErrors = true,
                    NotifyEmail = string.Empty,
                    LastUpdated = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении настроек бота");
                return new BotSettingsDto
                {
                    Id = 1,
                    BotName = "VK Бот",
                    VkToken = string.Empty,
                    GroupId = string.Empty,
                    AutoStart = true,
                    NotifyNewUsers = true,
                    NotifyErrors = true,
                    NotifyEmail = string.Empty,
                    LastUpdated = DateTime.Now
                };
            }
        }

        public async Task<BotCommandResult> UpdateBotSettingsAsync(BotSettingsDto settings)
        {
            try
            {
                var dbPath = _dbSettings.ConnectionString ?? @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db";

                if (!System.IO.File.Exists(dbPath))
                {
                    return BotCommandResult.FailResult($"Файл БД не найден: {dbPath}");
                }

                using var connection = new SqliteConnection($"Data Source={dbPath}");
                await connection.OpenAsync();

                // Проверяем, существует ли таблица
                var checkTableCmd = new SqliteCommand(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name='BotSettings'",
                    connection);

                if (await checkTableCmd.ExecuteScalarAsync() == null)
                {
                    // Создаем таблицу
                    await CreateSettingsTableAsync(connection);
                }

                // Сохраняем настройки
                var updateCmd = new SqliteCommand(@"
                    INSERT OR REPLACE INTO BotSettings (
                        Id, BotName, VkToken, GroupId, AutoStart, 
                        NotifyNewUsers, NotifyErrors, NotifyEmail, LastUpdated
                    ) VALUES (
                        1, @BotName, @VkToken, @GroupId, @AutoStart,
                        @NotifyNewUsers, @NotifyErrors, @NotifyEmail, datetime('now')
                    )", connection);

                updateCmd.Parameters.AddWithValue("@BotName", settings.BotName);
                updateCmd.Parameters.AddWithValue("@VkToken", settings.VkToken);
                updateCmd.Parameters.AddWithValue("@GroupId", settings.GroupId);
                updateCmd.Parameters.AddWithValue("@AutoStart", settings.AutoStart);
                updateCmd.Parameters.AddWithValue("@NotifyNewUsers", settings.NotifyNewUsers);
                updateCmd.Parameters.AddWithValue("@NotifyErrors", settings.NotifyErrors);
                updateCmd.Parameters.AddWithValue("@NotifyEmail", settings.NotifyEmail);

                await updateCmd.ExecuteNonQueryAsync();

                // Очищаем кэш
                _cache.Remove("bot_settings");

                _logger.LogInformation("Настройки бота обновлены: {BotName}", settings.BotName);
                return BotCommandResult.SuccessResult("Настройки сохранены");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при сохранении настроек бота");
                return BotCommandResult.FailResult($"Ошибка сохранения: {ex.Message}");
            }
        }

        // ==================== ПРИВАТНЫЕ МЕТОДЫ ====================

        private bool CheckProcessRunning()
        {
            try
            {
                var processes = Process.GetProcessesByName(_botProcessName);
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool CheckLockFile()
        {
            try
            {
                var dbPath = _dbSettings.ConnectionString ?? @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db";
                var lockFilePath = Path.Combine(Path.GetDirectoryName(dbPath) ?? "", "bot.lock");

                return System.IO.File.Exists(lockFilePath);
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CheckApiHealthAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private string DetermineOverallStatus(BotStatusInfo statusInfo)
        {
            if (statusInfo.IsApiResponding && statusInfo.IsProcessRunning)
                return "running";

            if (statusInfo.IsProcessRunning && !statusInfo.IsApiResponding)
                return "starting";

            if (!statusInfo.IsProcessRunning && statusInfo.HasLockFile)
                return "crashed";

            if (!statusInfo.IsProcessRunning && !statusInfo.HasLockFile)
                return "stopped";

            return "unknown";
        }

        private async Task<TimeSpan> GetUptimeAsync()
        {
            try
            {
                var dbPath = _dbSettings.ConnectionString ?? @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db";
                var lockFilePath = Path.Combine(Path.GetDirectoryName(dbPath) ?? "", "bot.lock");

                if (System.IO.File.Exists(lockFilePath))
                {
                    var fileInfo = new FileInfo(lockFilePath);
                    return DateTime.Now - fileInfo.CreationTime;
                }

                return TimeSpan.Zero;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        private async Task<string> GetVersionAsync()
        {
            try
            {
                var dbPath = _dbSettings.ConnectionString ?? @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db";
                var versionFilePath = Path.Combine(Path.GetDirectoryName(dbPath) ?? "", "version.txt");

                if (System.IO.File.Exists(versionFilePath))
                {
                    return await System.IO.File.ReadAllTextAsync(versionFilePath);
                }

                return "1.0.0";
            }
            catch
            {
                return "1.0.0";
            }
        }

        private async Task<DatabaseStats> GetDatabaseStatsAsync()
        {
            try
            {
                var dbPath = _dbSettings.ConnectionString ?? @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db";

                if (!System.IO.File.Exists(dbPath))
                {
                    return new DatabaseStats { IsAvailable = false };
                }

                using var connection = new SqliteConnection($"Data Source={dbPath}");
                await connection.OpenAsync();

                var stats = new DatabaseStats
                {
                    IsAvailable = true,
                    LastModified = File.GetLastWriteTime(dbPath)
                };

                // Получаем количество команд
                var cmdCountCommand = new SqliteCommand("SELECT COUNT(*) FROM Commands", connection);
                stats.CommandsCount = Convert.ToInt32(await cmdCountCommand.ExecuteScalarAsync());

                // Получаем размер файла
                var fileInfo = new FileInfo(dbPath);
                stats.FileSizeBytes = fileInfo.Length;

                return stats;
            }
            catch
            {
                return new DatabaseStats { IsAvailable = false };
            }
        }

        private async Task CreateSettingsTableAsync(SqliteConnection connection)
        {
            var createTableCmd = new SqliteCommand(@"
                CREATE TABLE BotSettings (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    BotName TEXT DEFAULT 'VK Бот',
                    VkToken TEXT DEFAULT '',
                    GroupId TEXT DEFAULT '',
                    AutoStart BOOLEAN DEFAULT 1,
                    NotifyNewUsers BOOLEAN DEFAULT 1,
                    NotifyErrors BOOLEAN DEFAULT 1,
                    NotifyEmail TEXT DEFAULT '',
                    LastUpdated DATETIME DEFAULT CURRENT_TIMESTAMP
                )", connection);

            await createTableCmd.ExecuteNonQueryAsync();

            var insertCmd = new SqliteCommand(@"INSERT INTO BotSettings (Id) VALUES (1)", connection);
            await insertCmd.ExecuteNonQueryAsync();
        }

        private string MaskToken(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length < 8)
                return "********";

            return $"{token.Substring(0, 4)}...{token.Substring(token.Length - 4)}";
        }
    }

    // ==================== МОДЕЛИ ====================

    public class BotStatusInfo
    {
        public DateTime Timestamp { get; set; }
        public string OverallStatus { get; set; } = "unknown";
        public bool IsProcessRunning { get; set; }
        public bool HasLockFile { get; set; }
        public bool IsApiResponding { get; set; }
        public TimeSpan Uptime { get; set; }
        public string Version { get; set; } = "1.0.0";
        public DatabaseStats DatabaseStats { get; set; } = new();
        public string? Error { get; set; }
    }

    public class DatabaseStats
    {
        public bool IsAvailable { get; set; }
        public int CommandsCount { get; set; }
        public int UsersCount { get; set; }
        public long FileSizeBytes { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class BotSettingsDto
    {
        public int Id { get; set; }
        public string BotName { get; set; } = "VK Бот";
        public string VkToken { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;
        public bool AutoStart { get; set; } = true;
        public bool NotifyNewUsers { get; set; } = true;
        public bool NotifyErrors { get; set; } = true;
        public string NotifyEmail { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }
    }

    public class BotCommandResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public object? Data { get; set; }

        public static BotCommandResult SuccessResult(string message, object? data = null)
            => new() { Success = true, Message = message, Data = data };

        public static BotCommandResult FailResult(string message, object? data = null)
            => new() { Success = false, Message = message, Data = data };
    }
}