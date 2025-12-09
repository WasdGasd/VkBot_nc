using AdminPanel.Configs;
using AdminPanel.Models;
using AdminPanel.Models.BotApi;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text.Json;

namespace AdminPanel.Services
{
    public class UserService
    {
        private readonly string _connectionString;
        private readonly ILogger<UserService> _logger;
        private readonly BotPathsConfig _botPaths;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public UserService(
            IConfiguration configuration,
            ILogger<UserService> logger,
            IOptions<BotPathsConfig> botPathsConfig,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _botPaths = botPathsConfig.Value;
            _configuration = configuration;
            _httpClient = httpClientFactory.CreateClient("BotApi");

            var dbPath = _botPaths.DatabasePath;
            _connectionString = $"Data Source={dbPath};Pooling=true;Cache=Shared;";

            _logger.LogInformation("UserService инициализирован с реальными данными бота");

            // ГАРАНТИРОВАННО создаем таблицу при запуске
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            try
            {
                _logger.LogInformation("Начинаем инициализацию таблицы Users...");

                // Создаем директорию если её нет
                var dbPath = _botPaths.DatabasePath;
                var directory = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.LogInformation("Создана директория для БД: {Directory}", directory);
                }

                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                // 1. Создаем таблицу Users если её нет
                var createTableSql = @"
                    CREATE TABLE IF NOT EXISTS Users (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        VkUserId INTEGER UNIQUE NOT NULL,
                        FirstName TEXT NOT NULL,
                        LastName TEXT NOT NULL,
                        Username TEXT DEFAULT '',
                        IsActive BOOLEAN DEFAULT 1,
                        IsOnline BOOLEAN DEFAULT 0,
                        LastActivity DATETIME DEFAULT CURRENT_TIMESTAMP,
                        MessageCount INTEGER DEFAULT 0,
                        RegistrationDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        IsBanned BOOLEAN DEFAULT 0,
                        Status TEXT DEFAULT 'user',
                        Email TEXT DEFAULT '',
                        Phone TEXT DEFAULT '',
                        Location TEXT DEFAULT '',
                        PhotoUrl TEXT DEFAULT '',
                        Notes TEXT DEFAULT '',
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                        UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                    )";

                using var createCmd = new SqliteCommand(createTableSql, connection);
                createCmd.ExecuteNonQuery();
                _logger.LogInformation("Таблица Users создана/проверена");

                // 2. Создаем индексы
                var indexes = new[]
                {
                    "CREATE INDEX IF NOT EXISTS idx_users_vkuserid ON Users(VkUserId)",
                    "CREATE INDEX IF NOT EXISTS idx_users_active ON Users(IsActive)",
                    "CREATE INDEX IF NOT EXISTS idx_users_online ON Users(IsOnline)",
                    "CREATE INDEX IF NOT EXISTS idx_users_banned ON Users(IsBanned)",
                    "CREATE INDEX IF NOT EXISTS idx_users_lastactivity ON Users(LastActivity DESC)"
                };

                foreach (var indexSql in indexes)
                {
                    using var indexCmd = new SqliteCommand(indexSql, connection);
                    indexCmd.ExecuteNonQuery();
                }
                _logger.LogInformation("Индексы таблицы Users созданы/проверены");

                // 3. Создаем таблицу Messages для хранения переписки
                var createMessagesTableSql = @"
                    CREATE TABLE IF NOT EXISTS Messages (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        VkUserId INTEGER NOT NULL,
                        MessageText TEXT NOT NULL,
                        IsFromUser BOOLEAN DEFAULT 1,
                        MessageDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (VkUserId) REFERENCES Users(VkUserId) ON DELETE CASCADE
                    )";

                using var messagesCmd = new SqliteCommand(createMessagesTableSql, connection);
                messagesCmd.ExecuteNonQuery();
                _logger.LogInformation("Таблица Messages создана/проверена");

                // Индекс для быстрого поиска сообщений по пользователю
                var messagesIndexSql = "CREATE INDEX IF NOT EXISTS idx_messages_vkuserid ON Messages(VkUserId)";
                using var messagesIndexCmd = new SqliteCommand(messagesIndexSql, connection);
                messagesIndexCmd.ExecuteNonQuery();

                // 4. Добавляем тестовых пользователей если таблица пуста
                var countCmd = new SqliteCommand("SELECT COUNT(*) FROM Users", connection);
                var count = Convert.ToInt32(countCmd.ExecuteScalar());

                if (count == 0)
                {
                    _logger.LogInformation("Таблица Users пуста, добавляем тестовых пользователей...");

                    var testUsers = new[]
                    {
                        @"INSERT INTO Users (VkUserId, FirstName, LastName, Username, IsActive, IsOnline, 
                          LastActivity, MessageCount, RegistrationDate, IsBanned, Status, Email, Phone, Location)
                          VALUES (123456789, 'Иван', 'Иванов', 'ivan_ivanov', 1, 1, 
                          CURRENT_TIMESTAMP, 15, DATETIME('now', '-10 days'), 0, 'user', 'ivan@example.com', '+7 999 123-45-67', 'Москва')",

                        @"INSERT INTO Users (VkUserId, FirstName, LastName, Username, IsActive, IsOnline, 
                          LastActivity, MessageCount, RegistrationDate, IsBanned, Status, Email, Phone, Location)
                          VALUES (987654321, 'Мария', 'Петрова', 'maria_petrova', 1, 0, 
                          DATETIME('now', '-2 hours'), 8, DATETIME('now', '-5 days'), 0, 'vip', 'maria@example.com', '+7 999 987-65-43', 'Санкт-Петербург')",

                        @"INSERT INTO Users (VkUserId, FirstName, LastName, Username, IsActive, IsOnline, 
                          LastActivity, MessageCount, RegistrationDate, IsBanned, Status, Email, Phone, Location)
                          VALUES (555555555, 'Алексей', 'Сидоров', 'alex_sidorov', 1, 1, 
                          CURRENT_TIMESTAMP, 23, DATETIME('now', '-15 days'), 0, 'user', 'alex@example.com', '+7 999 555-55-55', 'Екатеринбург')"
                    };

                    foreach (var sql in testUsers)
                    {
                        using var testCmd = new SqliteCommand(sql, connection);
                        testCmd.ExecuteNonQuery();
                    }

                    // Добавляем тестовые сообщения
                    AddTestMessages(connection);

                    _logger.LogInformation("3 тестовых пользователя добавлены");
                }
                else
                {
                    _logger.LogInformation("В таблице Users уже есть {Count} пользователей", count);
                }

                _logger.LogInformation("Инициализация таблицы Users завершена успешно");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "КРИТИЧЕСКАЯ ОШИБКА при инициализации таблицы Users");
                throw;
            }
        }

        private void AddTestMessages(SqliteConnection connection)
        {
            try
            {
                // Добавляем тестовые сообщения для пользователей
                var testMessages = new[]
                {
                    @"INSERT INTO Messages (VkUserId, MessageText, IsFromUser, MessageDate) 
                      VALUES (123456789, 'Привет! Как мне забронировать билет?', 1, DATETIME('now', '-3 hours'))",

                    @"INSERT INTO Messages (VkUserId, MessageText, IsFromUser, MessageDate) 
                      VALUES (123456789, 'Здравствуйте! Вы можете забронировать билет через наше мобильное приложение или на сайте.', 0, DATETIME('now', '-3 hours', '+5 minutes'))",

                    @"INSERT INTO Messages (VkUserId, MessageText, IsFromUser, MessageDate) 
                      VALUES (987654321, 'Какие есть скидки для студентов?', 1, DATETIME('now', '-1 day'))",

                    @"INSERT INTO Messages (VkUserId, MessageText, IsFromUser, MessageDate) 
                      VALUES (987654321, 'Для студентов действует скидка 20% при предъявлении студенческого билета.', 0, DATETIME('now', '-1 day', '+10 minutes'))",

                    @"INSERT INTO Messages (VkUserId, MessageText, IsFromUser, MessageDate) 
                      VALUES (555555555, 'До скольки вы работаете сегодня?', 1, DATETIME('now', '-30 минут'))",

                    @"INSERT INTO Messages (VkUserId, MessageText, IsFromUser, MessageDate) 
                      VALUES (555555555, 'Мы работаем с 10:00 до 22:00. Последний сеанс в 21:00.', 0, DATETIME('now', '-25 минут'))"
                };

                foreach (var sql in testMessages)
                {
                    using var cmd = new SqliteCommand(sql, connection);
                    cmd.ExecuteNonQuery();
                }

                _logger.LogInformation("Добавлены тестовые сообщения");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении тестовых сообщений");
            }
        }

        // ==================== РЕАЛЬНЫЕ МЕТОДЫ ДЛЯ ПОЛЬЗОВАТЕЛЕЙ ====================

        public async Task<UserListResponse> GetUsersAsync(
            int page = 1,
            int pageSize = 20,
            string search = "",
            string status = "all",
            string sortBy = "newest")
        {
            _logger.LogInformation("Получение реальных пользователей: страница {Page}, поиск: '{Search}'", page, search);

            try
            {
                // Пробуем получить данные из реального API бота
                var realUsers = await GetRealUsersFromBotAsync();

                // Если не удалось получить реальные данные, используем локальную базу
                if (realUsers == null || !realUsers.Any())
                {
                    _logger.LogWarning("Не удалось получить реальных пользователей, используем локальные данные");
                    return await GetUsersFromLocalDatabaseAsync(page, pageSize, search ?? "", status, sortBy);
                }

                // Фильтрация и сортировка реальных данных
                var filteredUsers = FilterRealUsers(realUsers, search, status);
                var sortedUsers = SortRealUsers(filteredUsers, sortBy);

                // Пагинация
                var totalCount = filteredUsers.Count;
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                var paginatedUsers = sortedUsers
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // Конвертация в формат User
                var users = ConvertToUsers(paginatedUsers);

                return new UserListResponse
                {
                    Users = users,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = totalPages,
                    ActiveCount = filteredUsers.Count(u => u.IsActive && !u.IsBanned),
                    OnlineCount = filteredUsers.Count(u => u.IsOnline),
                    NewTodayCount = filteredUsers.Count(u => u.RegisteredAt.Date == DateTime.Today)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователей");
                // В случае ошибки возвращаем данные из локальной базы
                return await GetUsersFromLocalDatabaseAsync(page, pageSize, search ?? "", status, sortBy);
            }
        }

        private async Task<List<BotApiUserResponse>> GetRealUsersFromBotAsync()
        {
            try
            {
                var botApiUrl = _configuration["BotApi:BaseUrl"] ?? "http://localhost:5000";
                _logger.LogInformation("Попытка подключения к API бота: {BotApiUrl}/api/adminapi/users?all=true", botApiUrl);

                var response = await _httpClient.GetAsync($"{botApiUrl}/api/adminapi/users?all=true");

                _logger.LogInformation("Ответ от API бота: StatusCode={StatusCode}", response.StatusCode);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("API бота не доступен: {StatusCode}, ReasonPhrase={ReasonPhrase}",
                        response.StatusCode, response.ReasonPhrase);

                    // Пробуем получить содержимое ошибки
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Содержимое ошибки: {ErrorContent}", errorContent);

                    return new List<BotApiUserResponse>();
                }

                var content = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Полученный контент от API бота: {Content}", content);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var result = JsonSerializer.Deserialize<BotApiUserListResponse>(content, options);

                if (result == null)
                {
                    _logger.LogWarning("Не удалось десериализовать ответ от API бота");
                    return new List<BotApiUserResponse>();
                }

                _logger.LogInformation("Успешно получено {Count} пользователей из API бота", result.Users?.Count ?? 0);
                return result?.Users ?? new List<BotApiUserResponse>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении данных из API бота: {Message}", ex.Message);
                return new List<BotApiUserResponse>();
            }
        }

        private List<BotApiUserResponse> FilterRealUsers(List<BotApiUserResponse> users, string search, string status)
        {
            if (string.IsNullOrEmpty(search) && status == "all")
                return users;

            var filtered = users.AsEnumerable();

            if (!string.IsNullOrEmpty(search))
            {
                var searchLower = search.ToLower();
                filtered = filtered.Where(u =>
                    (u.FirstName?.ToLower() ?? "").Contains(searchLower) ||
                    (u.LastName?.ToLower() ?? "").Contains(searchLower) ||
                    (u.Username?.ToLower() ?? "").Contains(searchLower) ||
                    u.VkId.ToString().Contains(search)
                );
            }

            if (status != "all")
            {
                filtered = status switch
                {
                    "active" => filtered.Where(u => u.IsActive && !u.IsBanned),
                    "inactive" => filtered.Where(u => !u.IsActive && !u.IsBanned),
                    "banned" => filtered.Where(u => u.IsBanned),
                    "online" => filtered.Where(u => u.IsOnline),
                    _ => filtered
                };
            }

            return filtered.ToList();
        }

        private List<BotApiUserResponse> SortRealUsers(List<BotApiUserResponse> users, string sortBy)
        {
            return sortBy switch
            {
                "oldest" => users.OrderBy(u => u.RegisteredAt).ToList(),
                "name" => users.OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ToList(),
                "activity" => users.OrderByDescending(u => u.LastSeen).ToList(),
                _ => users.OrderByDescending(u => u.RegisteredAt).ToList() // newest
            };
        }

        // ==================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ КОНВЕРТАЦИИ ====================

        private List<User> ConvertToUsers(List<BotApiUserResponse> botUsers)
        {
            return botUsers.Select(botUser => new User
            {
                Id = (int)botUser.VkId, // Явное приведение long к int
                VkUserId = botUser.VkId,
                FirstName = botUser.FirstName ?? "Неизвестно",
                LastName = botUser.LastName ?? "",
                Username = botUser.Username ?? "",
                IsActive = botUser.IsActive,
                IsOnline = botUser.IsOnline,
                LastActivity = botUser.LastSeen,
                MessageCount = botUser.MessagesCount,
                RegistrationDate = botUser.RegisteredAt,
                IsBanned = botUser.IsBanned,
                Status = botUser.Status ?? "user"
            }).ToList();
        }

        private User ConvertToUser(BotApiUserDetailResponse realUser)
        {
            // Логируем структуру для отладки
            try
            {
                var json = JsonSerializer.Serialize(realUser);
                _logger.LogDebug("Структура BotApiUserDetailResponse: {Json}", json);
            }
            catch { }

            try
            {
                // Пытаемся получить данные из вложенного объекта User или напрямую
                object? source = realUser;

                // Проверяем наличие свойства User
                var userProperty = realUser.GetType().GetProperty("User");
                if (userProperty != null)
                {
                    var userObj = userProperty.GetValue(realUser);
                    if (userObj != null)
                    {
                        source = userObj;
                    }
                }

                return new User
                {
                    Id = GetIntValue(source, "VkId", "Id", "UserId"),
                    VkUserId = GetLongValue(source, "VkId", "Id", "UserId"),
                    FirstName = GetStringValue(source, "FirstName", "Name", "First") ?? "Неизвестно",
                    LastName = GetStringValue(source, "LastName", "Surname", "Last") ?? "",
                    Username = GetStringValue(source, "Username", "Nickname", "Login") ?? "",
                    IsActive = GetBoolValue(source, "IsActive", "Active"),
                    IsOnline = GetBoolValue(source, "IsOnline", "Online"),
                    LastActivity = GetDateTimeValue(source, "LastSeen", "LastActivity", "LastOnline"),
                    MessageCount = GetIntValue(source, "MessagesCount", "MessageCount", "TotalMessages"),
                    RegistrationDate = GetDateTimeValue(source, "RegisteredAt", "RegistrationDate", "CreatedAt"),
                    IsBanned = GetBoolValue(source, "IsBanned", "Banned"),
                    Status = GetStringValue(source, "Status", "Role", "Type") ?? "user"
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось конвертировать BotApiUserDetailResponse");
                return CreateDefaultUser();
            }
        }

        private int GetIntValue(object? obj, params string[] propertyNames)
        {
            if (obj == null) return 0;

            foreach (var propertyName in propertyNames)
            {
                try
                {
                    var property = obj.GetType().GetProperty(propertyName);
                    if (property != null)
                    {
                        var value = property.GetValue(obj);
                        if (value != null)
                        {
                            if (value is int intValue) return intValue;
                            if (value is long longValue) return (int)longValue;
                            if (int.TryParse(value.ToString(), out int parsed)) return parsed;
                        }
                    }
                }
                catch { }
            }
            return 0;
        }

        private long GetLongValue(object? obj, params string[] propertyNames)
        {
            if (obj == null) return 0;

            foreach (var propertyName in propertyNames)
            {
                try
                {
                    var property = obj.GetType().GetProperty(propertyName);
                    if (property != null)
                    {
                        var value = property.GetValue(obj);
                        if (value != null)
                        {
                            if (value is long longValue) return longValue;
                            if (value is int intValue) return intValue;
                            if (long.TryParse(value.ToString(), out long parsed)) return parsed;
                        }
                    }
                }
                catch { }
            }
            return 0;
        }

        private string? GetStringValue(object? obj, params string[] propertyNames)
        {
            if (obj == null) return null;

            foreach (var propertyName in propertyNames)
            {
                try
                {
                    var property = obj.GetType().GetProperty(propertyName);
                    if (property != null)
                    {
                        var value = property.GetValue(obj);
                        if (value != null)
                        {
                            return value.ToString();
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        private bool GetBoolValue(object? obj, params string[] propertyNames)
        {
            if (obj == null) return false;

            foreach (var propertyName in propertyNames)
            {
                try
                {
                    var property = obj.GetType().GetProperty(propertyName);
                    if (property != null)
                    {
                        var value = property.GetValue(obj);
                        if (value != null)
                        {
                            if (value is bool boolValue) return boolValue;
                            if (bool.TryParse(value.ToString(), out bool parsed)) return parsed;
                            if (value is int intValue) return intValue != 0;
                        }
                    }
                }
                catch { }
            }
            return false;
        }

        private DateTime GetDateTimeValue(object? obj, params string[] propertyNames)
        {
            if (obj == null) return DateTime.Now;

            foreach (var propertyName in propertyNames)
            {
                try
                {
                    var property = obj.GetType().GetProperty(propertyName);
                    if (property != null)
                    {
                        var value = property.GetValue(obj);
                        if (value != null)
                        {
                            if (value is DateTime dateTimeValue) return dateTimeValue;
                            if (DateTime.TryParse(value.ToString(), out DateTime parsed)) return parsed;
                        }
                    }
                }
                catch { }
            }
            return DateTime.Now;
        }

        private User CreateDefaultUser()
        {
            return new User
            {
                Id = 0,
                VkUserId = 0,
                FirstName = "Неизвестно",
                LastName = "",
                Username = "",
                IsActive = false,
                IsOnline = false,
                LastActivity = DateTime.Now,
                MessageCount = 0,
                RegistrationDate = DateTime.Now,
                IsBanned = false,
                Status = "user"
            };
        }

        // Метод для работы с локальной базой (резервный вариант)
        private async Task<UserListResponse> GetUsersFromLocalDatabaseAsync(
            int page = 1,
            int pageSize = 20,
            string search = "",
            string status = "all",
            string sortBy = "newest")
        {
            var response = new UserListResponse
            {
                Page = page,
                PageSize = pageSize,
                Users = new List<User>()
            };

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Базовые условия WHERE
                var conditions = new List<string>();
                var parameters = new Dictionary<string, object>();

                if (!string.IsNullOrEmpty(search))
                {
                    conditions.Add(@"
                        (LOWER(FirstName) LIKE @Search OR 
                         LOWER(LastName) LIKE @Search OR 
                         LOWER(Username) LIKE @Search OR 
                         CAST(VkUserId AS TEXT) LIKE @Search)");
                    parameters["@Search"] = $"%{search.ToLower()}%";
                }

                if (status != "all")
                {
                    switch (status)
                    {
                        case "active":
                            conditions.Add("IsActive = 1 AND IsBanned = 0");
                            break;
                        case "inactive":
                            conditions.Add("IsActive = 0 AND IsBanned = 0");
                            break;
                        case "banned":
                            conditions.Add("IsBanned = 1");
                            break;
                        case "online":
                            conditions.Add("IsOnline = 1");
                            break;
                    }
                }

                var whereClause = conditions.Any()
                    ? "WHERE " + string.Join(" AND ", conditions)
                    : "";

                // Получаем общее количество
                var countSql = $"SELECT COUNT(*) FROM Users {whereClause}";
                using var countCmd = new SqliteCommand(countSql, connection);

                foreach (var param in parameters)
                {
                    countCmd.Parameters.AddWithValue(param.Key, param.Value);
                }

                response.TotalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                response.TotalPages = (int)Math.Ceiling(response.TotalCount / (double)pageSize);

                // Получаем статистику
                await GetUserStatsAsync(connection, response);

                // Сортировка
                string orderBy = sortBy switch
                {
                    "oldest" => "RegistrationDate ASC",
                    "name" => "FirstName ASC, LastName ASC",
                    "activity" => "LastActivity DESC",
                    _ => "RegistrationDate DESC" // newest
                };

                // Получаем пользователей с пагинацией
                var sql = $@"
                    SELECT 
                        Id, VkUserId, FirstName, LastName, Username,
                        IsActive, IsOnline, LastActivity, MessageCount,
                        RegistrationDate, IsBanned, Status, Email, Phone, Location
                    FROM Users 
                    {whereClause}
                    ORDER BY {orderBy}
                    LIMIT @PageSize OFFSET @Offset";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@PageSize", pageSize);
                cmd.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);

                foreach (var param in parameters)
                {
                    cmd.Parameters.AddWithValue(param.Key, param.Value);
                }

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var user = new User
                    {
                        Id = reader.GetInt32(0),
                        VkUserId = reader.GetInt64(1),
                        FirstName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        LastName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        Username = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        IsActive = !reader.IsDBNull(5) && reader.GetBoolean(5),
                        IsOnline = !reader.IsDBNull(6) && reader.GetBoolean(6),
                        LastActivity = reader.GetDateTime(7),
                        MessageCount = reader.GetInt32(8),
                        RegistrationDate = reader.GetDateTime(9),
                        IsBanned = !reader.IsDBNull(10) && reader.GetBoolean(10),
                        Status = reader.IsDBNull(11) ? "user" : reader.GetString(11),
                        Email = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                        Phone = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                        Location = reader.IsDBNull(14) ? string.Empty : reader.GetString(14)
                    };

                    response.Users.Add(user);
                }

                _logger.LogInformation("Загружено {Count} пользователей из локальной базы", response.Users.Count);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователей из локальной базы");
                throw;
            }
        }

        private async Task GetUserStatsAsync(SqliteConnection connection, UserListResponse response)
        {
            try
            {
                // Активные пользователи (активны и не забанены)
                var activeSql = "SELECT COUNT(*) FROM Users WHERE IsActive = 1 AND IsBanned = 0";
                using var activeCmd = new SqliteCommand(activeSql, connection);
                response.ActiveCount = Convert.ToInt32(await activeCmd.ExecuteScalarAsync());

                // Онлайн пользователи
                var onlineSql = "SELECT COUNT(*) FROM Users WHERE IsOnline = 1";
                using var onlineCmd = new SqliteCommand(onlineSql, connection);
                response.OnlineCount = Convert.ToInt32(await onlineCmd.ExecuteScalarAsync());

                // Новые сегодня
                var todaySql = @"
                    SELECT COUNT(*) FROM Users 
                    WHERE DATE(RegistrationDate) = DATE('now')";
                using var todayCmd = new SqliteCommand(todaySql, connection);
                response.NewTodayCount = Convert.ToInt32(await todayCmd.ExecuteScalarAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении статистики пользователей из локальной базы");
            }
        }

        // ==================== ПОЛУЧЕНИЕ КОНКРЕТНОГО ПОЛЬЗОВАТЕЛЯ ====================

        public async Task<User?> GetUserAsync(long vkUserId)
        {
            try
            {
                // Пробуем получить реального пользователя
                var realUser = await GetRealUserFromBotAsync(vkUserId);
                if (realUser != null)
                {
                    return ConvertToUser(realUser);
                }

                // Если не нашли, ищем в локальной базе
                return await GetUserFromLocalDatabaseAsync(vkUserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователя {VkUserId}", vkUserId);
                return await GetUserFromLocalDatabaseAsync(vkUserId);
            }
        }

        private async Task<BotApiUserDetailResponse?> GetRealUserFromBotAsync(long vkUserId)
        {
            try
            {
                var botApiUrl = _configuration["BotApi:BaseUrl"] ?? "http://localhost:5000";
                var response = await _httpClient.GetAsync($"{botApiUrl}/api/adminapi/users/{vkUserId}");

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                return JsonSerializer.Deserialize<BotApiUserDetailResponse>(content, options);
            }
            catch
            {
                return null;
            }
        }

        private async Task<User?> GetUserFromLocalDatabaseAsync(long vkUserId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT 
                        Id, VkUserId, FirstName, LastName, Username,
                        IsActive, IsOnline, LastActivity, MessageCount,
                        RegistrationDate, IsBanned, Status, Email, Phone, Location
                    FROM Users 
                    WHERE VkUserId = @VkUserId";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@VkUserId", vkUserId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new User
                    {
                        Id = reader.GetInt32(0),
                        VkUserId = reader.GetInt64(1),
                        FirstName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        LastName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        Username = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        IsActive = !reader.IsDBNull(5) && reader.GetBoolean(5),
                        IsOnline = !reader.IsDBNull(6) && reader.GetBoolean(6),
                        LastActivity = reader.GetDateTime(7),
                        MessageCount = reader.GetInt32(8),
                        RegistrationDate = reader.GetDateTime(9),
                        IsBanned = !reader.IsDBNull(10) && reader.GetBoolean(10),
                        Status = reader.IsDBNull(11) ? "user" : reader.GetString(11),
                        Email = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                        Phone = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                        Location = reader.IsDBNull(14) ? string.Empty : reader.GetString(14)
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователя из локальной базы {VkUserId}", vkUserId);
                return null;
            }
        }

        public async Task<User?> GetUserByIdAsync(int id)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT 
                        Id, VkUserId, FirstName, LastName, Username,
                        IsActive, IsOnline, LastActivity, MessageCount,
                        RegistrationDate, IsBanned, Status, Email, Phone, Location
                    FROM Users 
                    WHERE Id = @Id";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Id", id);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new User
                    {
                        Id = reader.GetInt32(0),
                        VkUserId = reader.GetInt64(1),
                        FirstName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        LastName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        Username = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        IsActive = !reader.IsDBNull(5) && reader.GetBoolean(5),
                        IsOnline = !reader.IsDBNull(6) && reader.GetBoolean(6),
                        LastActivity = reader.GetDateTime(7),
                        MessageCount = reader.GetInt32(8),
                        RegistrationDate = reader.GetDateTime(9),
                        IsBanned = !reader.IsDBNull(10) && reader.GetBoolean(10),
                        Status = reader.IsDBNull(11) ? "user" : reader.GetString(11),
                        Email = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                        Phone = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                        Location = reader.IsDBNull(14) ? string.Empty : reader.GetString(14)
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователя ID {Id}", id);
                throw;
            }
        }

        public async Task<User> AddOrUpdateUserAsync(User user)
        {
            // Обновляем в локальной базе для истории
            return await AddOrUpdateUserAsyncInternal(user);
        }

        private async Task<User> AddOrUpdateUserAsyncInternal(User user)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Проверяем существование пользователя
                var existingUser = await GetUserAsync(user.VkUserId);

                if (existingUser == null)
                {
                    // Добавляем нового пользователя
                    var insertSql = @"
                INSERT INTO Users (
                    VkUserId, FirstName, LastName, Username,
                    IsActive, IsOnline, LastActivity, MessageCount,
                    RegistrationDate, IsBanned, Status, Email, Phone, Location
                ) VALUES (
                    @VkUserId, @FirstName, @LastName, @Username,
                    @IsActive, @IsOnline, @LastActivity, @MessageCount,
                    @RegistrationDate, @IsBanned, @Status, @Email, @Phone, @Location
                );
                SELECT last_insert_rowid();";

                    using var insertCmd = new SqliteCommand(insertSql, connection);
                    AddUserParameters(insertCmd, user);

                    var id = Convert.ToInt32(await insertCmd.ExecuteScalarAsync());
                    user.Id = id;

                    _logger.LogInformation("Добавлен новый пользователь: {FirstName} {LastName} (ID: {Id})",
                        user.FirstName, user.LastName, id);
                }
                else
                {
                    // Обновляем существующего пользователя
                    user.Id = existingUser.Id;

                    var updateSql = @"
                UPDATE Users SET
                    FirstName = @FirstName,
                    LastName = @LastName,
                    Username = @Username,
                    IsActive = @IsActive,
                    IsOnline = @IsOnline,
                    LastActivity = @LastActivity,
                    MessageCount = @MessageCount,
                    IsBanned = @IsBanned,
                    Status = @Status,
                    Email = @Email,
                    Phone = @Phone,
                    Location = @Location,
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE VkUserId = @VkUserId";

                    using var updateCmd = new SqliteCommand(updateSql, connection);
                    AddUserParameters(updateCmd, user);

                    await updateCmd.ExecuteNonQueryAsync();

                    _logger.LogInformation("Обновлен пользователь: {FirstName} {LastName} (ID: {Id})",
                        user.FirstName, user.LastName, user.Id);
                }

                return user;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении/обновлении пользователя");
                throw;
            }
        }

        private void AddUserParameters(SqliteCommand cmd, User user)
        {
            cmd.Parameters.AddWithValue("@VkUserId", user.VkUserId);
            cmd.Parameters.AddWithValue("@FirstName", user.FirstName ?? string.Empty);
            cmd.Parameters.AddWithValue("@LastName", user.LastName ?? string.Empty);
            cmd.Parameters.AddWithValue("@Username", user.Username ?? string.Empty);
            cmd.Parameters.AddWithValue("@IsActive", user.IsActive);
            cmd.Parameters.AddWithValue("@IsOnline", user.IsOnline);
            cmd.Parameters.AddWithValue("@LastActivity", user.LastActivity);
            cmd.Parameters.AddWithValue("@MessageCount", user.MessageCount);
            cmd.Parameters.AddWithValue("@RegistrationDate", user.RegistrationDate);
            cmd.Parameters.AddWithValue("@IsBanned", user.IsBanned);
            cmd.Parameters.AddWithValue("@Status", user.Status ?? "user");
            cmd.Parameters.AddWithValue("@Email", user.Email ?? string.Empty);
            cmd.Parameters.AddWithValue("@Phone", user.Phone ?? string.Empty);
            cmd.Parameters.AddWithValue("@Location", user.Location ?? string.Empty);
        }

        public async Task<bool> UpdateUserStatusAsync(int id, bool isActive, bool isBanned = false)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    UPDATE Users SET
                        IsActive = @IsActive,
                        IsBanned = @IsBanned,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE Id = @Id";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@IsActive", isActive);
                cmd.Parameters.AddWithValue("@IsBanned", isBanned);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();

                _logger.LogInformation("Обновлен статус пользователя ID {Id}: Active={Active}, Banned={Banned}",
                    id, isActive, isBanned);

                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении статуса пользователя ID {Id}", id);
                throw;
            }
        }

        public async Task<bool> UpdateUserActivityAsync(long vkUserId, bool isOnline, DateTime? lastActivity = null)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    UPDATE Users SET
                        IsOnline = @IsOnline,
                        LastActivity = COALESCE(@LastActivity, CURRENT_TIMESTAMP),
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE VkUserId = @VkUserId";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@VkUserId", vkUserId);
                cmd.Parameters.AddWithValue("@IsOnline", isOnline);
                cmd.Parameters.AddWithValue("@LastActivity",
                    lastActivity.HasValue ? (object)lastActivity.Value : DBNull.Value);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    _logger.LogDebug("Обновлена активность пользователя VK ID {VkUserId}: Online={Online}",
                        vkUserId, isOnline);
                }

                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении активности пользователя VK ID {VkUserId}", vkUserId);
                throw;
            }
        }

        public async Task<bool> IncrementMessageCountAsync(long vkUserId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    UPDATE Users SET
                        MessageCount = MessageCount + 1,
                        LastActivity = CURRENT_TIMESTAMP,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE VkUserId = @VkUserId";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@VkUserId", vkUserId);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при увеличении счетчика сообщений пользователя VK ID {VkUserId}", vkUserId);
                throw;
            }
        }

        public async Task<bool> DeleteUserAsync(int id)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Сначала получаем информацию о пользователя для логов
                var user = await GetUserByIdAsync(id);

                var sql = "DELETE FROM Users WHERE Id = @Id";
                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Id", id);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();

                if (rowsAffected > 0 && user != null)
                {
                    _logger.LogInformation("Удален пользователь ID {Id}: {FirstName} {LastName} (VK ID: {VkUserId})",
                        id, user.FirstName, user.LastName, user.VkUserId);
                }

                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении пользователя ID {Id}", id);
                throw;
            }
        }

        public async Task<List<User>> SearchUsersAsync(string query)
        {
            var users = new List<User>();

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT 
                        Id, VkUserId, FirstName, LastName, Username,
                        IsActive, IsOnline, LastActivity, MessageCount,
                        RegistrationDate, IsBanned, Status, Email, Phone, Location
                    FROM Users 
                    WHERE LOWER(FirstName) LIKE @Query OR 
                          LOWER(LastName) LIKE @Query OR 
                          LOWER(Username) LIKE @Query OR 
                          CAST(VkUserId AS TEXT) LIKE @Query
                    ORDER BY LastActivity DESC
                    LIMIT 50";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Query", $"%{query.ToLower()}%");

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var user = new User
                    {
                        Id = reader.GetInt32(0),
                        VkUserId = reader.GetInt64(1),
                        FirstName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        LastName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        Username = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        IsActive = !reader.IsDBNull(5) && reader.GetBoolean(5),
                        IsOnline = !reader.IsDBNull(6) && reader.GetBoolean(6),
                        LastActivity = reader.GetDateTime(7),
                        MessageCount = reader.GetInt32(8),
                        RegistrationDate = reader.GetDateTime(9),
                        IsBanned = !reader.IsDBNull(10) && reader.GetBoolean(10),
                        Status = reader.IsDBNull(11) ? "user" : reader.GetString(11),
                        Email = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                        Phone = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                        Location = reader.IsDBNull(14) ? string.Empty : reader.GetString(14)
                    };

                    users.Add(user);
                }

                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при поиске пользователей по запросу: {Query}", query);
                throw;
            }
        }

        // ==================== МЕТОДЫ ДЛЯ РАБОТЫ С СООБЩЕНИЯМИ ====================

        /// <summary>
        /// Получить историю переписки с пользователем
        /// </summary>
        public async Task<List<Message>> GetUserMessagesAsync(long vkUserId, int limit = 50)
        {
            try
            {
                // Пробуем получить реальные сообщения
                var realMessages = await GetRealMessagesFromBotAsync(vkUserId, limit);
                if (realMessages != null && realMessages.Any())
                {
                    return ConvertToMessages(realMessages, vkUserId);
                }

                // Если не удалось, используем локальные данные
                return await GetMessagesFromLocalDatabaseAsync(vkUserId, limit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении сообщений пользователя {VkUserId}", vkUserId);
                return await GetMessagesFromLocalDatabaseAsync(vkUserId, limit);
            }
        }

        private async Task<List<BotApiMessage>?> GetRealMessagesFromBotAsync(long vkUserId, int limit)
        {
            try
            {
                var botApiUrl = _configuration["BotApi:BaseUrl"] ?? "http://localhost:5000";
                var response = await _httpClient.GetAsync($"{botApiUrl}/api/adminapi/users/{vkUserId}/messages?limit={limit}");

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                // Пробуем десериализовать разными способами
                try
                {
                    // Вариант 1: Прямой список сообщений
                    var messages = JsonSerializer.Deserialize<List<BotApiMessage>>(content, options);
                    if (messages != null && messages.Any())
                        return messages;

                    // Вариант 2: Обернутый ответ { messages: [...] }
                    var wrappedResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(content, options);
                    if (wrappedResponse != null && wrappedResponse.ContainsKey("messages"))
                    {
                        var messagesJson = JsonSerializer.Serialize(wrappedResponse["messages"]);
                        return JsonSerializer.Deserialize<List<BotApiMessage>>(messagesJson, options);
                    }

                    // Вариант 3: Ответ в формате BotApiMessageListResponse
                    var listResponse = JsonSerializer.Deserialize<BotApiMessageListResponse>(content, options);
                    return listResponse?.Messages;
                }
                catch
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private List<Message> ConvertToMessages(List<BotApiMessage> realMessages, long vkUserId)
        {
            return realMessages.Select(real => new Message
            {
                Id = real.Id,
                VkUserId = vkUserId,
                MessageText = real.Text,
                IsFromUser = real.IsFromUser,
                MessageDate = real.SentAt,
                CreatedAt = real.SentAt
            }).ToList();
        }

        private async Task<List<Message>> GetMessagesFromLocalDatabaseAsync(long vkUserId, int limit)
        {
            var messages = new List<Message>();

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT Id, VkUserId, MessageText, IsFromUser, MessageDate, CreatedAt
                    FROM Messages 
                    WHERE VkUserId = @VkUserId
                    ORDER BY MessageDate DESC
                    LIMIT @Limit";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@VkUserId", vkUserId);
                cmd.Parameters.AddWithValue("@Limit", limit);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    messages.Add(new Message
                    {
                        Id = reader.GetInt32(0),
                        VkUserId = reader.GetInt64(1),
                        MessageText = reader.GetString(2),
                        IsFromUser = reader.GetBoolean(3),
                        MessageDate = reader.GetDateTime(4),
                        CreatedAt = reader.GetDateTime(5)
                    });
                }

                _logger.LogDebug("Загружено {Count} сообщений из локальной базы для пользователя {VkUserId}",
                    messages.Count, vkUserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении сообщений из локальной базы для пользователя {VkUserId}", vkUserId);
            }

            return messages;
        }

        /// <summary>
        /// Добавить сообщение в историю переписки
        /// </summary>
        public async Task AddMessageAsync(long vkUserId, string messageText, bool isFromUser = true)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    INSERT INTO Messages (VkUserId, MessageText, IsFromUser, MessageDate)
                    VALUES (@VkUserId, @MessageText, @IsFromUser, CURRENT_TIMESTAMP)";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@VkUserId", vkUserId);
                cmd.Parameters.AddWithValue("@MessageText", messageText);
                cmd.Parameters.AddWithValue("@IsFromUser", isFromUser);

                await cmd.ExecuteNonQueryAsync();

                _logger.LogDebug("Добавлено сообщение от пользователя {VkUserId}: {MessageText}",
                    vkUserId, messageText.Length > 50 ? messageText.Substring(0, 50) + "..." : messageText);

                // Обновляем счетчик сообщений пользователя
                await IncrementMessageCountAsync(vkUserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении сообщения пользователя {VkUserId}", vkUserId);
                throw;
            }
        }

        /// <summary>
        /// Получить пользователей, с которыми есть переписка (последние N дней)
        /// </summary>
        public async Task<List<UserWithMessages>> GetUsersWithMessagesAsync(int days = 30)
        {
            var usersWithMessages = new List<UserWithMessages>();

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Сначала получаем пользователей с сообщениями
                var sql = @"
            SELECT DISTINCT
                u.Id, u.VkUserId, u.FirstName, u.LastName, u.Username,
                u.IsActive, u.IsOnline, u.LastActivity, u.MessageCount,
                u.RegistrationDate, u.IsBanned, u.Status, u.Email, u.Phone, u.Location
            FROM Users u
            INNER JOIN Messages m ON u.VkUserId = m.VkUserId
            WHERE m.MessageDate >= datetime('now', @DaysAgo)
            ORDER BY u.LastActivity DESC";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@DaysAgo", $"-{days} days");

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var user = new UserWithMessages
                    {
                        Id = reader.GetInt32(0),
                        VkUserId = reader.GetInt64(1),
                        FirstName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        LastName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        Username = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        IsActive = !reader.IsDBNull(5) && reader.GetBoolean(5),
                        IsOnline = !reader.IsDBNull(6) && reader.GetBoolean(6),
                        LastActivity = reader.GetDateTime(7),
                        MessageCount = reader.GetInt32(8),
                        RegistrationDate = reader.GetDateTime(9),
                        IsBanned = !reader.IsDBNull(10) && reader.GetBoolean(10),
                        Status = reader.IsDBNull(11) ? "user" : reader.GetString(11),
                        Email = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                        Phone = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                        Location = reader.IsDBNull(14) ? string.Empty : reader.GetString(14)
                    };

                    // Получаем сообщения этого пользователя
                    var messages = await GetUserMessagesAsync(user.VkUserId, 5);

                    user.MessagesCount = messages.Count;
                    user.LastMessageDate = messages.Any() ? messages.Max(m => m.MessageDate) : DateTime.MinValue;
                    user.RecentMessages = messages;
                    user.HasRecentMessages = messages.Any(m => m.MessageDate >= DateTime.Now.AddDays(-days));

                    usersWithMessages.Add(user);
                }

                _logger.LogInformation("Найдено {Count} пользователей с перепиской за последние {Days} дней",
                    usersWithMessages.Count, days);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователей с перепиской");
            }

            return usersWithMessages;
        }

        /// <summary>
        /// Получить статистику по перепискам
        /// </summary>
        public async Task<MessagesStats> GetMessagesStatsAsync(int days = 30)
        {
            var stats = new MessagesStats();

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Общее количество сообщений
                var totalSql = @"
                    SELECT 
                        COUNT(*) as TotalMessages,
                        COUNT(DISTINCT VkUserId) as UniqueUsers,
                        SUM(CASE WHEN IsFromUser = 1 THEN 1 ELSE 0 END) as FromUserCount,
                        SUM(CASE WHEN IsFromUser = 0 THEN 1 ELSE 0 END) as FromBotCount
                    FROM Messages 
                    WHERE MessageDate >= datetime('now', @DaysAgo)";

                using var totalCmd = new SqliteCommand(totalSql, connection);
                totalCmd.Parameters.AddWithValue("@DaysAgo", $"-{days} days");

                using var totalReader = await totalCmd.ExecuteReaderAsync();
                if (await totalReader.ReadAsync())
                {
                    stats.TotalMessages = totalReader.GetInt32(0);
                    stats.UniqueUsers = totalReader.GetInt32(1);
                    stats.FromUserCount = totalReader.GetInt32(2);
                    stats.FromBotCount = totalReader.GetInt32(3);
                }

                // Сообщения по дням (последние 7 дней)
                var dailySql = @"
                    SELECT 
                        DATE(MessageDate) as MessageDay,
                        COUNT(*) as MessageCount
                    FROM Messages 
                    WHERE MessageDate >= datetime('now', '-7 days')
                    GROUP BY DATE(MessageDate)
                    ORDER BY MessageDay DESC";

                using var dailyCmd = new SqliteCommand(dailySql, connection);
                using var dailyReader = await dailyCmd.ExecuteReaderAsync();

                while (await dailyReader.ReadAsync())
                {
                    stats.DailyStats.Add(new DailyMessageStats
                    {
                        Date = DateTime.Parse(dailyReader.GetString(0)),
                        Count = dailyReader.GetInt32(1)
                    });
                }

                // Самые активные пользователи
                var topUsersSql = @"
                    SELECT 
                        u.VkUserId,
                        u.FirstName,
                        u.LastName,
                        u.Username,
                        COUNT(m.Id) as MessageCount,
                        MAX(m.MessageDate) as LastMessageDate
                    FROM Messages m
                    INNER JOIN Users u ON m.VkUserId = u.VkUserId
                    WHERE m.MessageDate >= datetime('now', @DaysAgo)
                    GROUP BY u.VkUserId
                    ORDER BY MessageCount DESC
                    LIMIT 10";

                using var topUsersCmd = new SqliteCommand(topUsersSql, connection);
                topUsersCmd.Parameters.AddWithValue("@DaysAgo", $"-{days} days");
                using var topUsersReader = await topUsersCmd.ExecuteReaderAsync();

                while (await topUsersReader.ReadAsync())
                {
                    stats.TopUsers.Add(new TopUserStats
                    {
                        VkUserId = topUsersReader.GetInt64(0),
                        FirstName = topUsersReader.IsDBNull(1) ? string.Empty : topUsersReader.GetString(1),
                        LastName = topUsersReader.IsDBNull(2) ? string.Empty : topUsersReader.GetString(2),
                        Username = topUsersReader.IsDBNull(3) ? string.Empty : topUsersReader.GetString(3),
                        MessageCount = topUsersReader.GetInt32(4),
                        LastMessageDate = topUsersReader.GetDateTime(5)
                    });
                }

                _logger.LogDebug("Статистика сообщений: {Total} сообщений от {Users} пользователей",
                    stats.TotalMessages, stats.UniqueUsers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении статистики сообщений");
            }

            return stats;
        }

        /// <summary>
        /// Импортировать сообщения из бота
        /// </summary>
        public async Task ImportBotMessagesAsync(List<BotMessageImport> botMessages)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Группируем сообщения по пользователям для batch вставки
                var groupedMessages = botMessages.GroupBy(m => m.VkUserId);
                var importedCount = 0;

                foreach (var group in groupedMessages)
                {
                    var vkUserId = group.Key;

                    // Создаем пользователя если его нет
                    var existingUser = await GetUserAsync(vkUserId);
                    if (existingUser == null && group.Any())
                    {
                        var firstMessage = group.First();
                        var user = new User
                        {
                            VkUserId = vkUserId,
                            FirstName = firstMessage.FirstName ?? "Неизвестный",
                            LastName = firstMessage.LastName ?? "Пользователь",
                            Username = firstMessage.Username ?? string.Empty,
                            IsActive = true,
                            IsOnline = false,
                            LastActivity = group.Max(m => m.MessageDate),
                            MessageCount = group.Count(),
                            RegistrationDate = group.Min(m => m.MessageDate),
                            IsBanned = false,
                            Status = "user"
                        };

                        await AddOrUpdateUserAsync(user);
                    }

                    // Добавляем сообщения
                    using var transaction = connection.BeginTransaction();
                    try
                    {
                        foreach (var message in group)
                        {
                            var sql = @"
                                INSERT OR IGNORE INTO Messages (VkUserId, MessageText, IsFromUser, MessageDate)
                                VALUES (@VkUserId, @MessageText, @IsFromUser, @MessageDate)";

                            using var cmd = new SqliteCommand(sql, connection, transaction);
                            cmd.Parameters.AddWithValue("@VkUserId", vkUserId);
                            cmd.Parameters.AddWithValue("@MessageText", message.Text);
                            cmd.Parameters.AddWithValue("@IsFromUser", message.IsFromUser);
                            cmd.Parameters.AddWithValue("@MessageDate", message.MessageDate);

                            await cmd.ExecuteNonQueryAsync();
                            importedCount++;
                        }

                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }

                _logger.LogInformation("Импортировано {Count} сообщений от {Users} пользователей",
                    importedCount, groupedMessages.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при импорте сообщений бота");
                throw;
            }
        }

        /// <summary>
        /// Получить пользователей, которые писали боту сегодня
        /// </summary>
        public async Task<List<User>> GetUsersMessagedTodayAsync()
        {
            var users = new List<User>();

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT DISTINCT
                        u.Id, u.VkUserId, u.FirstName, u.LastName, u.Username,
                        u.IsActive, u.IsOnline, u.LastActivity, u.MessageCount,
                        u.RegistrationDate, u.IsBanned, u.Status, u.Email, u.Phone, u.Location
                    FROM Users u
                    INNER JOIN Messages m ON u.VkUserId = m.VkUserId
                    WHERE DATE(m.MessageDate) = DATE('now')
                    ORDER BY u.LastActivity DESC";

                using var cmd = new SqliteCommand(sql, connection);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var user = new User
                    {
                        Id = reader.GetInt32(0),
                        VkUserId = reader.GetInt64(1),
                        FirstName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        LastName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        Username = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        IsActive = !reader.IsDBNull(5) && reader.GetBoolean(5),
                        IsOnline = !reader.IsDBNull(6) && reader.GetBoolean(6),
                        LastActivity = reader.GetDateTime(7),
                        MessageCount = reader.GetInt32(8),
                        RegistrationDate = reader.GetDateTime(9),
                        IsBanned = !reader.IsDBNull(10) && reader.GetBoolean(10),
                        Status = reader.IsDBNull(11) ? "user" : reader.GetString(11),
                        Email = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                        Phone = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                        Location = reader.IsDBNull(14) ? string.Empty : reader.GetString(14)
                    };

                    users.Add(user);
                }

                _logger.LogDebug("Найдено {Count} пользователей, которые писали сегодня", users.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователей, писавших сегодня");
            }

            return users;
        }

        /// <summary>
        /// Очистить историю сообщений старше N дней
        /// </summary>
        public async Task<int> CleanupOldMessagesAsync(int daysToKeep = 90)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    DELETE FROM Messages 
                    WHERE MessageDate < datetime('now', @DaysAgo)";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@DaysAgo", $"-{daysToKeep} days");

                var deletedCount = await cmd.ExecuteNonQueryAsync();

                _logger.LogInformation("Удалено {Count} старых сообщений (старше {Days} дней)",
                    deletedCount, daysToKeep);

                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при очистке старых сообщений");
                return 0;
            }
        }

        /// <summary>
        /// Отправить сообщение пользователю через бота (имитация)
        /// </summary>
        public async Task<bool> SendMessageToUserAsync(long vkUserId, string message)
        {
            try
            {
                // Здесь должен быть реальный вызов API бота
                // Пока просто сохраняем сообщение как от бота
                await AddMessageAsync(vkUserId, message, false);

                _logger.LogInformation("Отправлено сообщение пользователю {VkUserId}: {Message}",
                    vkUserId, message.Length > 50 ? message.Substring(0, 50) + "..." : message);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отправке сообщения пользователю {VkUserId}", vkUserId);
                return false;
            }
        }

        /// <summary>
        /// Получить сводку по пользователю (статистика активности)
        /// </summary>
        public async Task<UserSummary> GetUserSummaryAsync(long vkUserId)
        {
            var summary = new UserSummary { VkUserId = vkUserId };

            try
            {
                var user = await GetUserAsync(vkUserId);
                if (user == null)
                    return summary;

                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Получаем статистику сообщений за последние 7 дней
                var weeklySql = @"
                    SELECT 
                        COUNT(*) as TotalMessages,
                        SUM(CASE WHEN IsFromUser = 1 THEN 1 ELSE 0 END) as UserMessages,
                        SUM(CASE WHEN IsFromUser = 0 THEN 1 ELSE 0 END) as BotMessages,
                        DATE(MessageDate) as MessageDate
                    FROM Messages 
                    WHERE VkUserId = @VkUserId 
                    AND MessageDate >= datetime('now', '-7 days')
                    GROUP BY DATE(MessageDate)
                    ORDER BY MessageDate DESC";

                using var weeklyCmd = new SqliteCommand(weeklySql, connection);
                weeklyCmd.Parameters.AddWithValue("@VkUserId", vkUserId);

                using var weeklyReader = await weeklyCmd.ExecuteReaderAsync();
                while (await weeklyReader.ReadAsync())
                {
                    var daily = new DailyActivity
                    {
                        Date = DateTime.Parse(weeklyReader.GetString(3)),
                        TotalMessages = weeklyReader.GetInt32(0),
                        UserMessages = weeklyReader.GetInt32(1),
                        BotMessages = weeklyReader.GetInt32(2)
                    };
                    summary.WeeklyActivity.Add(daily);
                }

                // Среднее время ответа
                var responseTimeSql = @"
                    SELECT 
                        AVG(JULIANDAY(m2.MessageDate) - JULIANDAY(m1.MessageDate)) * 24 * 60 * 60 as AvgResponseTimeSeconds
                    FROM Messages m1
                    LEFT JOIN Messages m2 ON m1.VkUserId = m2.VkUserId 
                        AND m2.MessageDate > m1.MessageDate 
                        AND m2.MessageDate < datetime(m1.MessageDate, '+30 minutes')
                    WHERE m1.VkUserId = @VkUserId 
                    AND m1.IsFromUser = 1 
                    AND m2.IsFromUser = 0";

                using var responseCmd = new SqliteCommand(responseTimeSql, connection);
                responseCmd.Parameters.AddWithValue("@VkUserId", vkUserId);

                var avgResponseSeconds = await responseCmd.ExecuteScalarAsync();
                if (avgResponseSeconds != DBNull.Value)
                {
                    summary.AverageResponseTimeSeconds = Convert.ToDouble(avgResponseSeconds);
                }

                summary.UserInfo = user;
                summary.IsLoaded = true;

                _logger.LogDebug("Получена сводка по пользователю {VkUserId}", vkUserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении сводки по пользователю {VkUserId}", vkUserId);
            }

            return summary;
        }
    }

    // ==================== ДОПОЛНИТЕЛЬНЫЕ МОДЕЛИ ====================

    public class UserSummary
    {
        public long VkUserId { get; set; }
        public User? UserInfo { get; set; }
        public List<DailyActivity> WeeklyActivity { get; set; } = new();
        public double AverageResponseTimeSeconds { get; set; }
        public bool IsLoaded { get; set; }
    }

    public class DailyActivity
    {
        public DateTime Date { get; set; }
        public int TotalMessages { get; set; }
        public int UserMessages { get; set; }
        public int BotMessages { get; set; }
    }
}