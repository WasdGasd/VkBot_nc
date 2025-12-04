using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using AdminPanel.Configs;
using AdminPanel.Models;

namespace AdminPanel.Services
{
    public class UserService
    {
        private readonly string _connectionString;
        private readonly ILogger<UserService> _logger;
        private readonly BotPathsConfig _botPaths;

        public UserService(
            IConfiguration configuration,
            ILogger<UserService> logger,
            IOptions<BotPathsConfig> botPathsConfig)
        {
            _logger = logger;
            _botPaths = botPathsConfig.Value;

            var dbPath = _botPaths.DatabasePath;
            _connectionString = $"Data Source={dbPath};Pooling=true;Cache=Shared;";

            _logger.LogInformation("UserService инициализирован. Путь к БД: {DbPath}", dbPath);

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
                        FirstName TEXT,
                        LastName TEXT,
                        Username TEXT,
                        IsActive BOOLEAN DEFAULT 1,
                        IsOnline BOOLEAN DEFAULT 0,
                        LastActivity DATETIME DEFAULT CURRENT_TIMESTAMP,
                        MessageCount INTEGER DEFAULT 0,
                        RegistrationDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        IsBanned BOOLEAN DEFAULT 0,
                        Status TEXT DEFAULT 'user',
                        Email TEXT,
                        Phone TEXT,
                        Location TEXT,
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

                // 4. Добавляем тестового пользователя если таблица пуста
                var countCmd = new SqliteCommand("SELECT COUNT(*) FROM Users", connection);
                var count = Convert.ToInt32(countCmd.ExecuteScalar());

                if (count == 0)
                {
                    _logger.LogInformation("Таблица Users пуста, добавляем тестового пользователя...");

                    var testUserSql = @"
                        INSERT INTO Users (VkUserId, FirstName, LastName, Username, IsActive, IsOnline, 
                                          LastActivity, MessageCount, RegistrationDate, IsBanned, Status, Email)
                        VALUES (123456789, 'Тестовый', 'Пользователь', 'test_user', 1, 0, 
                                CURRENT_TIMESTAMP, 10, CURRENT_TIMESTAMP, 0, 'user', 'test@example.com')";

                    using var testCmd = new SqliteCommand(testUserSql, connection);
                    testCmd.ExecuteNonQuery();
                    _logger.LogInformation("Тестовый пользователь добавлен");
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

        // ==================== ОСНОВНЫЕ МЕТОДЫ ДЛЯ ПОЛЬЗОВАТЕЛЕЙ ====================

        public async Task<UserListResponse> GetUsersAsync(
            int page = 1,
            int pageSize = 20,
            string search = "",
            string status = "all",
            string sortBy = "newest")
        {
            _logger.LogInformation("Получение пользователей: страница {Page}, поиск: '{Search}'", page, search);

            var response = new UserListResponse
            {
                Page = page,
                PageSize = pageSize
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
                        FirstName = reader.IsDBNull(2) ? null : reader.GetString(2),
                        LastName = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Username = reader.IsDBNull(4) ? null : reader.GetString(4),
                        IsActive = !reader.IsDBNull(5) && reader.GetBoolean(5),
                        IsOnline = !reader.IsDBNull(6) && reader.GetBoolean(6),
                        LastActivity = reader.GetDateTime(7),
                        MessageCount = reader.GetInt32(8),
                        RegistrationDate = reader.GetDateTime(9),
                        IsBanned = !reader.IsDBNull(10) && reader.GetBoolean(10),
                        Status = reader.IsDBNull(11) ? null : reader.GetString(11),
                        Email = reader.IsDBNull(12) ? null : reader.GetString(12),
                        Phone = reader.IsDBNull(13) ? null : reader.GetString(13),
                        Location = reader.IsDBNull(14) ? null : reader.GetString(14)
                    };

                    response.Users.Add(user);
                }

                _logger.LogInformation("Загружено {Count} пользователей из {Total}", response.Users.Count, response.TotalCount);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователей");
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
                _logger.LogError(ex, "Ошибка при получении статистики пользователей");
            }
        }

        public async Task<User?> GetUserAsync(long vkUserId)
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
                        FirstName = reader.IsDBNull(2) ? null : reader.GetString(2),
                        LastName = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Username = reader.IsDBNull(4) ? null : reader.GetString(4),
                        IsActive = !reader.IsDBNull(5) && reader.GetBoolean(5),
                        IsOnline = !reader.IsDBNull(6) && reader.GetBoolean(6),
                        LastActivity = reader.GetDateTime(7),
                        MessageCount = reader.GetInt32(8),
                        RegistrationDate = reader.GetDateTime(9),
                        IsBanned = !reader.IsDBNull(10) && reader.GetBoolean(10),
                        Status = reader.IsDBNull(11) ? null : reader.GetString(11),
                        Email = reader.IsDBNull(12) ? null : reader.GetString(12),
                        Phone = reader.IsDBNull(13) ? null : reader.GetString(13),
                        Location = reader.IsDBNull(14) ? null : reader.GetString(14)
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователя {VkUserId}", vkUserId);
                throw;
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
                        FirstName = reader.IsDBNull(2) ? null : reader.GetString(2),
                        LastName = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Username = reader.IsDBNull(4) ? null : reader.GetString(4),
                        IsActive = !reader.IsDBNull(5) && reader.GetBoolean(5),
                        IsOnline = !reader.IsDBNull(6) && reader.GetBoolean(6),
                        LastActivity = reader.GetDateTime(7),
                        MessageCount = reader.GetInt32(8),
                        RegistrationDate = reader.GetDateTime(9),
                        IsBanned = !reader.IsDBNull(10) && reader.GetBoolean(10),
                        Status = reader.IsDBNull(11) ? null : reader.GetString(11),
                        Email = reader.IsDBNull(12) ? null : reader.GetString(12),
                        Phone = reader.IsDBNull(13) ? null : reader.GetString(13),
                        Location = reader.IsDBNull(14) ? null : reader.GetString(14)
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
            cmd.Parameters.AddWithValue("@FirstName", (object?)user.FirstName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LastName", (object?)user.LastName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Username", (object?)user.Username ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", user.IsActive);
            cmd.Parameters.AddWithValue("@IsOnline", user.IsOnline);
            cmd.Parameters.AddWithValue("@LastActivity", user.LastActivity);
            cmd.Parameters.AddWithValue("@MessageCount", user.MessageCount);
            cmd.Parameters.AddWithValue("@RegistrationDate", user.RegistrationDate);
            cmd.Parameters.AddWithValue("@IsBanned", user.IsBanned);
            cmd.Parameters.AddWithValue("@Status", (object?)user.Status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Email", (object?)user.Email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Phone", (object?)user.Phone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Location", (object?)user.Location ?? DBNull.Value);
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

                // Сначала получаем информацию о пользователе для логов
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
                        FirstName = reader.IsDBNull(2) ? null : reader.GetString(2),
                        LastName = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Username = reader.IsDBNull(4) ? null : reader.GetString(4),
                        IsActive = !reader.IsDBNull(5) && reader.GetBoolean(5),
                        IsOnline = !reader.IsDBNull(6) && reader.GetBoolean(6),
                        LastActivity = reader.GetDateTime(7),
                        MessageCount = reader.GetInt32(8),
                        RegistrationDate = reader.GetDateTime(9),
                        IsBanned = !reader.IsDBNull(10) && reader.GetBoolean(10),
                        Status = reader.IsDBNull(11) ? null : reader.GetString(11),
                        Email = reader.IsDBNull(12) ? null : reader.GetString(12),
                        Phone = reader.IsDBNull(13) ? null : reader.GetString(13),
                        Location = reader.IsDBNull(14) ? null : reader.GetString(14)
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении сообщения пользователя {VkUserId}", vkUserId);
                throw;
            }
        }

        /// <summary>
        /// Получить историю переписки с пользователем
        /// </summary>
        public async Task<List<Message>> GetUserMessagesAsync(long vkUserId, int limit = 50)
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

                _logger.LogDebug("Загружено {Count} сообщений пользователя {VkUserId}", messages.Count, vkUserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении сообщений пользователя {VkUserId}", vkUserId);
            }

            return messages;
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

                var sql = @"
                    SELECT 
                        u.Id, u.VkUserId, u.FirstName, u.LastName, u.Username,
                        u.IsActive, u.IsOnline, u.LastActivity, u.MessageCount,
                        u.RegistrationDate, u.IsBanned, u.Status, u.Email, u.Phone, u.Location,
                        COUNT(m.Id) as MessageCount,
                        MAX(m.MessageDate) as LastMessageDate,
                        GROUP_CONCAT(
                            CASE 
                                WHEN LENGTH(m.MessageText) > 50 
                                THEN SUBSTR(m.MessageText, 1, 50) || '...'
                                ELSE m.MessageText
                            END, ' || '
                        ) as LastMessagesPreview
                    FROM Users u
                    LEFT JOIN Messages m ON u.VkUserId = m.VkUserId 
                        AND m.MessageDate >= datetime('now', @DaysAgo)
                    GROUP BY u.Id, u.VkUserId
                    HAVING COUNT(m.Id) > 0
                    ORDER BY MAX(m.MessageDate) DESC, u.LastActivity DESC";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@DaysAgo", $"-{days} days");

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var user = new User
                    {
                        Id = reader.GetInt32(0),
                        VkUserId = reader.GetInt64(1),
                        FirstName = reader.IsDBNull(2) ? null : reader.GetString(2),
                        LastName = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Username = reader.IsDBNull(4) ? null : reader.GetString(4),
                        IsActive = !reader.IsDBNull(5) && reader.GetBoolean(5),
                        IsOnline = !reader.IsDBNull(6) && reader.GetBoolean(6),
                        LastActivity = reader.GetDateTime(7),
                        MessageCount = reader.GetInt32(8),
                        RegistrationDate = reader.GetDateTime(9),
                        IsBanned = !reader.IsDBNull(10) && reader.GetBoolean(10),
                        Status = reader.IsDBNull(11) ? null : reader.GetString(11),
                        Email = reader.IsDBNull(12) ? null : reader.GetString(12),
                        Phone = reader.IsDBNull(13) ? null : reader.GetString(13),
                        Location = reader.IsDBNull(14) ? null : reader.GetString(14)
                    };

                    var messagesCount = reader.GetInt32(15);
                    var lastMessageDate = reader.IsDBNull(16) ? DateTime.MinValue : reader.GetDateTime(16);
                    var messagesPreview = reader.IsDBNull(17) ? null : reader.GetString(17);

                    usersWithMessages.Add(new UserWithMessages
                    {
                        User = user,
                        MessagesCount = messagesCount,
                        LastMessageDate = lastMessageDate,
                        MessagesPreview = messagesPreview?.Split(" || ", StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>(),
                        HasRecentMessages = messagesCount > 0
                    });
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
                        FirstName = topUsersReader.IsDBNull(1) ? null : topUsersReader.GetString(1),
                        LastName = topUsersReader.IsDBNull(2) ? null : topUsersReader.GetString(2),
                        Username = topUsersReader.IsDBNull(3) ? null : topUsersReader.GetString(3),
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
        public async Task ImportBotMessagesAsync(List<BotMessage> botMessages)
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
                            FirstName = firstMessage.FirstName,
                            LastName = firstMessage.LastName,
                            Username = firstMessage.Username,
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
                        FirstName = reader.IsDBNull(2) ? null : reader.GetString(2),
                        LastName = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Username = reader.IsDBNull(4) ? null : reader.GetString(4),
                        IsActive = !reader.IsDBNull(5) && reader.GetBoolean(5),
                        IsOnline = !reader.IsDBNull(6) && reader.GetBoolean(6),
                        LastActivity = reader.GetDateTime(7),
                        MessageCount = reader.GetInt32(8),
                        RegistrationDate = reader.GetDateTime(9),
                        IsBanned = !reader.IsDBNull(10) && reader.GetBoolean(10),
                        Status = reader.IsDBNull(11) ? null : reader.GetString(11),
                        Email = reader.IsDBNull(12) ? null : reader.GetString(12),
                        Phone = reader.IsDBNull(13) ? null : reader.GetString(13),
                        Location = reader.IsDBNull(14) ? null : reader.GetString(14)
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
    }
}