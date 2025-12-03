using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using AdminPanel.Configs;

namespace AdminPanel.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;
        private readonly BotPathsConfig _botPaths;
        private readonly ILogger<DatabaseService> _logger;

        public DatabaseService(
            IConfiguration configuration,
            ILogger<DatabaseService> logger,
            IOptions<BotPathsConfig> botPathsConfig)
        {
            _logger = logger;
            _botPaths = botPathsConfig.Value;

            // Формируем строку подключения
            var dbPath = _botPaths.DatabasePath;
            _connectionString = $"Data Source={dbPath};Pooling=true;Cache=Shared;";

            _logger.LogInformation("DatabaseService инициализирован. Путь к БД: {DbPath}", dbPath);
            CheckDatabaseAvailability();
        }

        private void CheckDatabaseAvailability()
        {
            try
            {
                var dbPath = _botPaths.DatabasePath;

                if (!File.Exists(dbPath))
                {
                    _logger.LogWarning("Файл БД не найден: {DbPath}", dbPath);

                    // Создаем директорию если её нет
                    var directory = Path.GetDirectoryName(dbPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                        _logger.LogInformation("Создана директория: {Directory}", directory);
                    }
                }
                else
                {
                    var fileInfo = new FileInfo(dbPath);
                    _logger.LogInformation("БД найдена: {Path}, размер: {Size} KB",
                        dbPath, fileInfo.Length / 1024);

                    // Проверяем доступность
                    TestConnection();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке БД");
            }
        }

        private void TestConnection()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                connection.Close();
                _logger.LogDebug("Подключение к БД успешно");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка подключения к БД");
            }
        }

        public async Task<bool> MigrateDatabaseAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                _logger.LogInformation("Выполнение миграции базы данных...");

                // Проверяем существование таблицы Commands
                var checkTableSql = @"
                    SELECT name FROM sqlite_master 
                    WHERE type='table' AND name='Commands'";

                var checkTableCmd = new SqliteCommand(checkTableSql, connection);
                var tableExists = await checkTableCmd.ExecuteScalarAsync() != null;

                if (!tableExists)
                {
                    _logger.LogInformation("Таблица Commands не существует, создаем...");
                    return await InitializeDatabaseAsync();
                }

                // Проверяем наличие колонки CreatedAt
                var checkColumnSql = @"
                    SELECT COUNT(*) FROM pragma_table_info('Commands') 
                    WHERE name = 'CreatedAt'";

                var checkColumnCmd = new SqliteCommand(checkColumnSql, connection);
                var hasCreatedAt = Convert.ToInt32(await checkColumnCmd.ExecuteScalarAsync()) > 0;

                if (!hasCreatedAt)
                {
                    _logger.LogInformation("Добавляем колонку CreatedAt в таблицу Commands...");

                    // Добавляем колонку CreatedAt
                    var addColumnSql = @"
                        ALTER TABLE Commands 
                        ADD COLUMN CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP";

                    var alterCmd = new SqliteCommand(addColumnSql, connection);
                    await alterCmd.ExecuteNonQueryAsync();

                    _logger.LogInformation("Колонка CreatedAt успешно добавлена");

                    // Обновляем существующие записи
                    var updateSql = @"
                        UPDATE Commands 
                        SET CreatedAt = datetime('now') 
                        WHERE CreatedAt IS NULL";

                    var updateCmd = new SqliteCommand(updateSql, connection);
                    var updatedRows = await updateCmd.ExecuteNonQueryAsync();

                    _logger.LogInformation("Обновлено {Count} записей", updatedRows);
                }
                else
                {
                    _logger.LogInformation("Колонка CreatedAt уже существует");
                }

                // Проверяем наличие колонки CommandType
                var checkCommandTypeSql = @"
                    SELECT COUNT(*) FROM pragma_table_info('Commands') 
                    WHERE name = 'CommandType'";

                var checkCommandTypeCmd = new SqliteCommand(checkCommandTypeSql, connection);
                var hasCommandType = Convert.ToInt32(await checkCommandTypeCmd.ExecuteScalarAsync()) > 0;

                if (!hasCommandType)
                {
                    _logger.LogInformation("Добавляем колонку CommandType в таблицу Commands...");

                    // Добавляем колонку CommandType
                    var addCommandTypeSql = @"
                        ALTER TABLE Commands 
                        ADD COLUMN CommandType TEXT DEFAULT 'text'";

                    var alterCmd2 = new SqliteCommand(addCommandTypeSql, connection);
                    await alterCmd2.ExecuteNonQueryAsync();

                    // Обновляем существующие записи
                    var updateCommandTypeSql = @"
                        UPDATE Commands 
                        SET CommandType = 'text' 
                        WHERE CommandType IS NULL";

                    var updateCmd2 = new SqliteCommand(updateCommandTypeSql, connection);
                    await updateCmd2.ExecuteNonQueryAsync();

                    _logger.LogInformation("Колонка CommandType успешно добавлена");
                }

                // Проверяем наличие колонки KeyboardJson
                var checkKeyboardSql = @"
                    SELECT COUNT(*) FROM pragma_table_info('Commands') 
                    WHERE name = 'KeyboardJson'";

                var checkKeyboardCmd = new SqliteCommand(checkKeyboardSql, connection);
                var hasKeyboardJson = Convert.ToInt32(await checkKeyboardCmd.ExecuteScalarAsync()) > 0;

                if (!hasKeyboardJson)
                {
                    _logger.LogInformation("Добавляем колонку KeyboardJson в таблицу Commands...");

                    // Добавляем колонку KeyboardJson
                    var addKeyboardSql = @"
                        ALTER TABLE Commands 
                        ADD COLUMN KeyboardJson TEXT NULL";

                    var alterCmd3 = new SqliteCommand(addKeyboardSql, connection);
                    await alterCmd3.ExecuteNonQueryAsync();

                    _logger.LogInformation("Колонка KeyboardJson успешно добавлена");
                }

                // Проверяем таблицу BotSettings
                var checkSettingsTableSql = @"
                    SELECT name FROM sqlite_master 
                    WHERE type='table' AND name='BotSettings'";

                var checkSettingsTableCmd = new SqliteCommand(checkSettingsTableSql, connection);
                var settingsTableExists = await checkSettingsTableCmd.ExecuteScalarAsync() != null;

                if (!settingsTableExists)
                {
                    _logger.LogInformation("Создаем таблицу BotSettings...");

                    var createSettingsTable = @"
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
                        )";

                    var createCmd = new SqliteCommand(createSettingsTable, connection);
                    await createCmd.ExecuteNonQueryAsync();

                    // Добавляем запись по умолчанию
                    var insertSettings = @"
                        INSERT INTO BotSettings (Id, BotName) 
                        VALUES (1, 'VK Бот')";

                    var insertCmd = new SqliteCommand(insertSettings, connection);
                    await insertCmd.ExecuteNonQueryAsync();

                    _logger.LogInformation("Таблица BotSettings создана");
                }

                _logger.LogInformation("Миграция БД завершена успешно");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при миграции БД");
                return false;
            }
        }

        public async Task<List<Models.Command>> GetCommandsAsync(
            int page = 1,
            int pageSize = 20,
            string searchTerm = "")
        {
            var commands = new List<Models.Command>();

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Проверяем существование таблицы
                var tableExistsCmd = new SqliteCommand(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name='Commands'",
                    connection);
                var tableExists = await tableExistsCmd.ExecuteScalarAsync() != null;

                if (!tableExists)
                {
                    _logger.LogWarning("Таблица Commands не существует");
                    return commands;
                }

                // Проверяем наличие колонки CreatedAt
                var checkColumnSql = @"
                    SELECT COUNT(*) FROM pragma_table_info('Commands') 
                    WHERE name = 'CreatedAt'";

                var checkColumnCmd = new SqliteCommand(checkColumnSql, connection);
                var hasCreatedAt = Convert.ToInt32(await checkColumnCmd.ExecuteScalarAsync()) > 0;

                var createdAtColumn = hasCreatedAt ? "CreatedAt" : "datetime('now') as CreatedAt";

                // Проверяем наличие колонки CommandType
                var checkCommandTypeSql = @"
                    SELECT COUNT(*) FROM pragma_table_info('Commands') 
                    WHERE name = 'CommandType'";

                var checkCommandTypeCmd = new SqliteCommand(checkCommandTypeSql, connection);
                var hasCommandType = Convert.ToInt32(await checkCommandTypeCmd.ExecuteScalarAsync()) > 0;

                var commandTypeColumn = hasCommandType ? "CommandType" : "'text' as CommandType";

                // Проверяем наличие колонки KeyboardJson
                var checkKeyboardSql = @"
                    SELECT COUNT(*) FROM pragma_table_info('Commands') 
                    WHERE name = 'KeyboardJson'";

                var checkKeyboardCmd = new SqliteCommand(checkKeyboardSql, connection);
                var hasKeyboardJson = Convert.ToInt32(await checkKeyboardCmd.ExecuteScalarAsync()) > 0;

                var keyboardJsonColumn = hasKeyboardJson ? "KeyboardJson" : "NULL as KeyboardJson";

                // Базовый запрос с поиском и пагинацией
                string whereClause = string.IsNullOrEmpty(searchTerm)
                    ? ""
                    : " WHERE (Name LIKE @Search OR Triggers LIKE @Search OR Response LIKE @Search)";

                string sql = $@"
                    SELECT Id, Name, Triggers, Response, {keyboardJsonColumn}, {commandTypeColumn}, {createdAtColumn} 
                    FROM Commands 
                    {whereClause}
                    ORDER BY Id DESC
                    LIMIT @PageSize OFFSET @Offset";

                var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@PageSize", pageSize);
                command.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    command.Parameters.AddWithValue("@Search", $"%{searchTerm}%");
                }

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    commands.Add(new Models.Command
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Triggers = reader.GetString(2),
                        Response = reader.GetString(3),
                        KeyboardJson = reader.IsDBNull(4) ? null : reader.GetString(4),
                        CommandType = reader.GetString(5),
                        CreatedAt = reader.GetDateTime(6)
                    });
                }

                _logger.LogDebug("Загружено {Count} команд (страница {Page})", commands.Count, page);
                return commands;
            }
            catch (SqliteException ex)
            {
                _logger.LogError(ex, "Ошибка SQLite при загрузке команд. Код: {ErrorCode}", ex.SqliteErrorCode);
                throw new InvalidOperationException($"Ошибка базы данных: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке команд");
                throw;
            }
        }

        public async Task<int> GetCommandsCountAsync(string searchTerm = "")
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Проверяем существование таблицы
                var tableExistsCmd = new SqliteCommand(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name='Commands'",
                    connection);
                var tableExists = await tableExistsCmd.ExecuteScalarAsync() != null;

                if (!tableExists)
                {
                    return 0;
                }

                string whereClause = string.IsNullOrEmpty(searchTerm)
                    ? ""
                    : " WHERE (Name LIKE @Search OR Triggers LIKE @Search OR Response LIKE @Search)";

                string sql = $"SELECT COUNT(*) FROM Commands {whereClause}";

                var command = new SqliteCommand(sql, connection);

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    command.Parameters.AddWithValue("@Search", $"%{searchTerm}%");
                }

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при подсчете команд");
                return 0;
            }
        }

        public async Task<Models.Command?> GetCommandByIdAsync(int id)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Проверяем наличие колонки CreatedAt
                var checkColumnSql = @"
                    SELECT COUNT(*) FROM pragma_table_info('Commands') 
                    WHERE name = 'CreatedAt'";

                var checkColumnCmd = new SqliteCommand(checkColumnSql, connection);
                var hasCreatedAt = Convert.ToInt32(await checkColumnCmd.ExecuteScalarAsync()) > 0;

                var createdAtColumn = hasCreatedAt ? "CreatedAt" : "datetime('now') as CreatedAt";

                string sql = $@"
                    SELECT Id, Name, Triggers, Response, 
                           COALESCE(KeyboardJson, '') as KeyboardJson,
                           COALESCE(CommandType, 'text') as CommandType, 
                           {createdAtColumn} 
                    FROM Commands WHERE Id = @Id";

                var command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@Id", id);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new Models.Command
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Triggers = reader.GetString(2),
                        Response = reader.GetString(3),
                        KeyboardJson = string.IsNullOrEmpty(reader.GetString(4)) ? null : reader.GetString(4),
                        CommandType = reader.GetString(5),
                        CreatedAt = reader.GetDateTime(6)
                    };
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении команды ID {Id}", id);
                return null;
            }
        }

        public async Task<int> AddCommandAsync(Models.Command command)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Проверяем уникальность имени
                var checkCmd = new SqliteCommand(
                    "SELECT COUNT(*) FROM Commands WHERE LOWER(Name) = LOWER(@Name)",
                    connection);
                checkCmd.Parameters.AddWithValue("@Name", command.Name.Trim());
                var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                if (exists)
                {
                    throw new InvalidOperationException($"Команда с именем '{command.Name}' уже существует");
                }

                // Проверяем наличие всех колонок
                var checkCreatedAtSql = @"
                    SELECT COUNT(*) FROM pragma_table_info('Commands') 
                    WHERE name = 'CreatedAt'";

                var checkCreatedAtCmd = new SqliteCommand(checkCreatedAtSql, connection);
                var hasCreatedAt = Convert.ToInt32(await checkCreatedAtCmd.ExecuteScalarAsync()) > 0;

                var checkCommandTypeSql = @"
                    SELECT COUNT(*) FROM pragma_table_info('Commands') 
                    WHERE name = 'CommandType'";

                var checkCommandTypeCmd = new SqliteCommand(checkCommandTypeSql, connection);
                var hasCommandType = Convert.ToInt32(await checkCommandTypeCmd.ExecuteScalarAsync()) > 0;

                var checkKeyboardSql = @"
                    SELECT COUNT(*) FROM pragma_table_info('Commands') 
                    WHERE name = 'KeyboardJson'";

                var checkKeyboardCmd = new SqliteCommand(checkKeyboardSql, connection);
                var hasKeyboardJson = Convert.ToInt32(await checkKeyboardCmd.ExecuteScalarAsync()) > 0;

                // Динамически строим SQL в зависимости от наличия колонок
                var columns = new List<string> { "Name", "Triggers", "Response" };
                var values = new List<string> { "@Name", "@Triggers", "@Response" };
                var parameters = new Dictionary<string, object>
                {
                    ["@Name"] = command.Name.Trim(),
                    ["@Triggers"] = command.Triggers?.Trim() ?? "",
                    ["@Response"] = command.Response.Trim()
                };

                if (hasKeyboardJson)
                {
                    columns.Add("KeyboardJson");
                    values.Add("@KeyboardJson");
                    parameters["@KeyboardJson"] = string.IsNullOrWhiteSpace(command.KeyboardJson) ? DBNull.Value : command.KeyboardJson;
                }

                if (hasCommandType)
                {
                    columns.Add("CommandType");
                    values.Add("@CommandType");
                    parameters["@CommandType"] = string.IsNullOrWhiteSpace(command.CommandType) ? "text" : command.CommandType;
                }

                if (hasCreatedAt)
                {
                    columns.Add("CreatedAt");
                    values.Add("datetime('now')");
                }

                var sql = $@"
                    INSERT INTO Commands ({string.Join(", ", columns)})
                    VALUES ({string.Join(", ", values)});
                    SELECT last_insert_rowid();";

                var cmd = new SqliteCommand(sql, connection);
                foreach (var param in parameters)
                {
                    cmd.Parameters.AddWithValue(param.Key, param.Value);
                }

                var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                _logger.LogInformation("Добавлена команда: '{Name}' (ID: {Id})", command.Name, id);
                return id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении команды: {Name}", command.Name);
                throw;
            }
        }

        public async Task<bool> UpdateCommandAsync(Models.Command command)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Проверяем существование команды
                var checkCmd = new SqliteCommand(
                    "SELECT COUNT(*) FROM Commands WHERE Id = @Id",
                    connection);
                checkCmd.Parameters.AddWithValue("@Id", command.Id);
                var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

                if (!exists)
                {
                    throw new KeyNotFoundException($"Команда с ID {command.Id} не найдена");
                }

                // Проверяем уникальность имени (кроме текущей команды)
                var checkNameCmd = new SqliteCommand(
                    "SELECT COUNT(*) FROM Commands WHERE LOWER(Name) = LOWER(@Name) AND Id != @Id",
                    connection);
                checkNameCmd.Parameters.AddWithValue("@Name", command.Name.Trim());
                checkNameCmd.Parameters.AddWithValue("@Id", command.Id);
                var nameExists = Convert.ToInt32(await checkNameCmd.ExecuteScalarAsync()) > 0;

                if (nameExists)
                {
                    throw new InvalidOperationException($"Команда с именем '{command.Name}' уже существует");
                }

                // Проверяем наличие колонок
                var checkKeyboardSql = @"
                    SELECT COUNT(*) FROM pragma_table_info('Commands') 
                    WHERE name = 'KeyboardJson'";

                var checkKeyboardCmd = new SqliteCommand(checkKeyboardSql, connection);
                var hasKeyboardJson = Convert.ToInt32(await checkKeyboardCmd.ExecuteScalarAsync()) > 0;

                var checkCommandTypeSql = @"
                    SELECT COUNT(*) FROM pragma_table_info('Commands') 
                    WHERE name = 'CommandType'";

                var checkCommandTypeCmd = new SqliteCommand(checkCommandTypeSql, connection);
                var hasCommandType = Convert.ToInt32(await checkCommandTypeCmd.ExecuteScalarAsync()) > 0;

                // Динамически строим SQL
                var setClauses = new List<string>
                {
                    "Name = @Name",
                    "Triggers = @Triggers",
                    "Response = @Response"
                };

                var parameters = new Dictionary<string, object>
                {
                    ["@Id"] = command.Id,
                    ["@Name"] = command.Name.Trim(),
                    ["@Triggers"] = command.Triggers?.Trim() ?? "",
                    ["@Response"] = command.Response.Trim()
                };

                if (hasKeyboardJson)
                {
                    setClauses.Add("KeyboardJson = @KeyboardJson");
                    parameters["@KeyboardJson"] = string.IsNullOrWhiteSpace(command.KeyboardJson) ? DBNull.Value : command.KeyboardJson;
                }

                if (hasCommandType)
                {
                    setClauses.Add("CommandType = @CommandType");
                    parameters["@CommandType"] = string.IsNullOrWhiteSpace(command.CommandType) ? "text" : command.CommandType;
                }

                var sql = $@"
                    UPDATE Commands 
                    SET {string.Join(", ", setClauses)}
                    WHERE Id = @Id";

                var cmd = new SqliteCommand(sql, connection);
                foreach (var param in parameters)
                {
                    cmd.Parameters.AddWithValue(param.Key, param.Value);
                }

                var affectedRows = await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Обновлена команда ID {Id}: {AffectedRows} строк",
                    command.Id, affectedRows);
                return affectedRows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении команды ID {Id}", command.Id);
                throw;
            }
        }

        public async Task<bool> DeleteCommandAsync(int id)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Получаем имя команды для логов
                var getNameCmd = new SqliteCommand(
                    "SELECT Name FROM Commands WHERE Id = @Id",
                    connection);
                getNameCmd.Parameters.AddWithValue("@Id", id);
                var name = await getNameCmd.ExecuteScalarAsync() as string;

                var sql = "DELETE FROM Commands WHERE Id = @Id";
                var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@Id", id);

                var affectedRows = await cmd.ExecuteNonQueryAsync();

                if (affectedRows > 0)
                {
                    _logger.LogInformation("Удалена команда ID {Id}: '{Name}'", id, name ?? "неизвестно");
                }
                else
                {
                    _logger.LogWarning("Команда ID {Id} не найдена для удаления", id);
                }

                return affectedRows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении команды ID {Id}", id);
                throw;
            }
        }

        public async Task<int> ClearAllCommandsAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Проверяем существование таблицы
                var tableExistsCmd = new SqliteCommand(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name='Commands'",
                    connection);
                var tableExists = await tableExistsCmd.ExecuteScalarAsync() != null;

                if (!tableExists)
                {
                    _logger.LogWarning("Таблица Commands не существует");
                    return 0;
                }

                // Получаем количество команд перед удалением
                var countCmd = new SqliteCommand("SELECT COUNT(*) FROM Commands", connection);
                var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

                var sql = "DELETE FROM Commands";
                var cmd = new SqliteCommand(sql, connection);

                var affectedRows = await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Удалено всех команд: {Count} из {Total}", affectedRows, count);

                // Сбрасываем автоинкремент
                if (affectedRows > 0)
                {
                    var resetCmd = new SqliteCommand(
                        "DELETE FROM sqlite_sequence WHERE name='Commands'",
                        connection);
                    await resetCmd.ExecuteNonQueryAsync();
                }

                return affectedRows;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при очистке команд");
                throw;
            }
        }

        public async Task<BotSettingsDto> GetBotSettingsAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Проверяем существование таблицы настроек
                var tableExistsCmd = new SqliteCommand(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name='BotSettings'",
                    connection);
                var tableExists = await tableExistsCmd.ExecuteScalarAsync() != null;

                if (!tableExists)
                {
                    _logger.LogInformation("Таблица BotSettings не существует, создаем...");
                    await InitializeDatabaseAsync();
                }

                var getCmd = new SqliteCommand("SELECT * FROM BotSettings WHERE Id = 1", connection);

                using var reader = await getCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new BotSettingsDto
                    {
                        Id = reader.GetInt32(0),
                        BotName = reader.IsDBNull(1) ? "VK Бот" : reader.GetString(1),
                        VkToken = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        GroupId = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        AutoStart = !reader.IsDBNull(4) && reader.GetBoolean(4),
                        NotifyNewUsers = !reader.IsDBNull(5) && reader.GetBoolean(5),
                        NotifyErrors = !reader.IsDBNull(6) && reader.GetBoolean(6),
                        NotifyEmail = reader.IsDBNull(7) ? "" : reader.GetString(7),
                        LastUpdated = reader.IsDBNull(8) ? DateTime.Now : reader.GetDateTime(8)
                    };
                }

                // Если запись не найдена, создаем дефолтную
                _logger.LogInformation("Запись настроек не найдена, создаем дефолтную");
                return await CreateDefaultSettingsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении настроек бота");
                return new BotSettingsDto
                {
                    Id = 1,
                    BotName = "VK Бот",
                    VkToken = "",
                    GroupId = "",
                    AutoStart = true,
                    NotifyNewUsers = true,
                    NotifyErrors = true,
                    NotifyEmail = "",
                    LastUpdated = DateTime.Now
                };
            }
        }

        public async Task<bool> SaveBotSettingsAsync(BotSettingsDto settings)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Проверяем существование таблицы настроек
                var tableExistsCmd = new SqliteCommand(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name='BotSettings'",
                    connection);
                var tableExists = await tableExistsCmd.ExecuteScalarAsync() != null;

                if (!tableExists)
                {
                    _logger.LogInformation("Таблица BotSettings не существует, создаем...");
                    await InitializeDatabaseAsync();
                }

                var sql = @"
                    INSERT OR REPLACE INTO BotSettings (
                        Id, BotName, VkToken, GroupId, AutoStart, 
                        NotifyNewUsers, NotifyErrors, NotifyEmail, LastUpdated
                    ) VALUES (
                        1, @BotName, @VkToken, @GroupId, @AutoStart,
                        @NotifyNewUsers, @NotifyErrors, @NotifyEmail, datetime('now')
                    )";

                var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@BotName", settings.BotName ?? "VK Бот");
                cmd.Parameters.AddWithValue("@VkToken", settings.VkToken ?? "");
                cmd.Parameters.AddWithValue("@GroupId", settings.GroupId ?? "");
                cmd.Parameters.AddWithValue("@AutoStart", settings.AutoStart);
                cmd.Parameters.AddWithValue("@NotifyNewUsers", settings.NotifyNewUsers);
                cmd.Parameters.AddWithValue("@NotifyErrors", settings.NotifyErrors);
                cmd.Parameters.AddWithValue("@NotifyEmail", settings.NotifyEmail ?? "");

                var affectedRows = await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Сохранены настройки бота: '{BotName}' (строк: {Rows})",
                    settings.BotName, affectedRows);
                return affectedRows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при сохранении настроек бота");
                throw;
            }
        }

        public async Task<bool> InitializeDatabaseAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // Создаем таблицу команд с полной структурой
                var createCommandsTable = @"
                    CREATE TABLE IF NOT EXISTS Commands (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        Triggers TEXT DEFAULT '',
                        Response TEXT NOT NULL,
                        KeyboardJson TEXT NULL,
                        CommandType TEXT DEFAULT 'text',
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                    )";

                var cmd1 = new SqliteCommand(createCommandsTable, connection);
                await cmd1.ExecuteNonQueryAsync();
                _logger.LogInformation("Таблица Commands создана/проверена");

                // Создаем таблицу настроек
                var createSettingsTable = @"
                    CREATE TABLE IF NOT EXISTS BotSettings (
                        Id INTEGER PRIMARY KEY CHECK (Id = 1),
                        BotName TEXT DEFAULT 'VK Бот',
                        VkToken TEXT DEFAULT '',
                        GroupId TEXT DEFAULT '',
                        AutoStart BOOLEAN DEFAULT 1,
                        NotifyNewUsers BOOLEAN DEFAULT 1,
                        NotifyErrors BOOLEAN DEFAULT 1,
                        NotifyEmail TEXT DEFAULT '',
                        LastUpdated DATETIME DEFAULT CURRENT_TIMESTAMP
                    )";

                var cmd2 = new SqliteCommand(createSettingsTable, connection);
                await cmd2.ExecuteNonQueryAsync();
                _logger.LogInformation("Таблица BotSettings создана/проверена");

                // Добавляем запись настроек по умолчанию
                var insertSettings = @"
                    INSERT OR IGNORE INTO BotSettings (Id, BotName) VALUES (1, 'VK Бот')";

                var cmd3 = new SqliteCommand(insertSettings, connection);
                await cmd3.ExecuteNonQueryAsync();

                _logger.LogInformation("База данных успешно инициализирована");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при инициализации БД");
                return false;
            }
        }

        public async Task<DatabaseInfo> GetDatabaseInfoAsync()
        {
            var info = new DatabaseInfo();
            var dbPath = _botPaths.DatabasePath;

            try
            {
                info.Exists = File.Exists(dbPath);
                info.ConnectionString = _connectionString;

                if (info.Exists)
                {
                    var fileInfo = new FileInfo(dbPath);
                    info.FileSizeKB = fileInfo.Length / 1024;
                    info.LastModified = fileInfo.LastWriteTime;
                    info.Created = fileInfo.CreationTime;

                    // Пытаемся подключиться и получить статистику
                    try
                    {
                        using var connection = new SqliteConnection(_connectionString);
                        await connection.OpenAsync();

                        // Проверяем таблицу команд
                        var tableExistsCmd = new SqliteCommand(
                            "SELECT name FROM sqlite_master WHERE type='table' AND name='Commands'",
                            connection);
                        info.CommandsTableExists = await tableExistsCmd.ExecuteScalarAsync() != null;

                        if (info.CommandsTableExists)
                        {
                            var countCmd = new SqliteCommand("SELECT COUNT(*) FROM Commands", connection);
                            info.CommandsCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                        }

                        // Проверяем таблицу настроек
                        var settingsTableExistsCmd = new SqliteCommand(
                            "SELECT name FROM sqlite_master WHERE type='table' AND name='BotSettings'",
                            connection);
                        info.SettingsTableExists = await settingsTableExistsCmd.ExecuteScalarAsync() != null;

                        info.ConnectionTested = true;
                        _logger.LogDebug("Информация о БД получена: {CommandsCount} команд, размер: {Size}KB",
                            info.CommandsCount, info.FileSizeKB);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при тестировании подключения к БД");
                        info.ConnectionError = ex.Message;
                    }
                }
                else
                {
                    _logger.LogWarning("Файл БД не существует: {Path}", dbPath);
                }

                return info;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении информации о БД");
                info.Error = ex.Message;
                return info;
            }
        }

        private async Task<BotSettingsDto> CreateDefaultSettingsAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                await InitializeDatabaseAsync();

                var defaultSettings = new BotSettingsDto
                {
                    Id = 1,
                    BotName = "VK Бот",
                    VkToken = "",
                    GroupId = "",
                    AutoStart = true,
                    NotifyNewUsers = true,
                    NotifyErrors = true,
                    NotifyEmail = "",
                    LastUpdated = DateTime.Now
                };

                await SaveBotSettingsAsync(defaultSettings);
                return defaultSettings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании настроек по умолчанию");
                throw;
            }
        }

        public async Task<bool> RecreateDatabaseAsync()
        {
            try
            {
                var dbPath = _botPaths.DatabasePath;

                // Удаляем существующий файл БД
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                    _logger.LogInformation("Файл БД удален: {Path}", dbPath);
                }

                // Создаем новую БД
                await InitializeDatabaseAsync();

                _logger.LogInformation("База данных успешно пересоздана");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при пересоздании БД");
                return false;
            }
        }
    }

    public class DatabaseInfo
    {
        public bool Exists { get; set; }
        public long FileSizeKB { get; set; }
        public DateTime LastModified { get; set; }
        public DateTime Created { get; set; }
        public int CommandsCount { get; set; }
        public bool CommandsTableExists { get; set; }
        public bool SettingsTableExists { get; set; }
        public bool ConnectionTested { get; set; }
        public string ConnectionString { get; set; } = "";
        public string? ConnectionError { get; set; }
        public string? Error { get; set; }
    }
}