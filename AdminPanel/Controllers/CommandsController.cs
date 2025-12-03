using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using AdminPanel.Models;

namespace AdminPanel.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CommandsController : ControllerBase
    {
        private readonly string _dbPath;
        private readonly ILogger<CommandsController> _logger;
        private readonly IConfiguration _configuration;

        public CommandsController(
            IConfiguration configuration,
            ILogger<CommandsController> logger,
            IOptions<DatabaseConfig> dbSettings)
        {
            _configuration = configuration;
            _logger = logger;
            // Путь к БД бота в другом проекте
            _dbPath = dbSettings.Value.ConnectionString ??
                @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db";
        }

        // GET: api/commands
        [HttpGet]
        public async Task<IActionResult> GetCommands([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            _logger.LogInformation("Получение команд, страница {Page}, размер {PageSize}", page, pageSize);

            try
            {
                if (!System.IO.File.Exists(_dbPath))
                {
                    _logger.LogWarning("Файл БД не найден: {DbPath}", _dbPath);
                    return NotFound(new ApiResponse(false, "Файл базы данных не найден", new
                    {
                        dbPath = _dbPath,
                        dbExists = false
                    }));
                }

                var commands = new List<Command>();
                int totalCount = 0;

                await using (var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=true"))
                {
                    await connection.OpenAsync();

                    // Получаем общее количество
                    var countCommand = new SqliteCommand("SELECT COUNT(*) FROM Commands", connection);
                    totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

                    // Получаем данные с пагинацией
                    var queryCommand = new SqliteCommand(
                        "SELECT * FROM Commands ORDER BY Id LIMIT @Limit OFFSET @Offset",
                        connection);

                    queryCommand.Parameters.AddWithValue("@Limit", pageSize);
                    queryCommand.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);

                    await using (var reader = await queryCommand.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var command = new Command
                            {
                                Id = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                                Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                Triggers = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                Response = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                KeyboardJson = reader.IsDBNull(4) ? null : reader.GetString(4),
                                CommandType = reader.IsDBNull(5) ? "text" : reader.GetString(5),
                                CreatedAt = reader.IsDBNull(6) ? DateTime.MinValue : reader.GetDateTime(6)
                            };
                            commands.Add(command);
                        }
                    }
                }

                _logger.LogInformation("Загружено {Count} команд", commands.Count);

                return Ok(new ApiResponse(true, "Команды успешно загружены", new
                {
                    commands,
                    pagination = new
                    {
                        page,
                        pageSize,
                        totalCount,
                        totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                    },
                    dbPath = _dbPath,
                    dbExists = true
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении команд");
                return StatusCode(500, new ApiResponse(false, "Ошибка при получении команд", ex.Message));
            }
        }

        // GET: api/commands/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetCommand(int id)
        {
            _logger.LogInformation("Получение команды с ID {Id}", id);

            try
            {
                if (!System.IO.File.Exists(_dbPath))
                {
                    return NotFound(new ApiResponse(false, $"Файл БД не найден: {_dbPath}"));
                }

                await using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    await connection.OpenAsync();
                    var command = new SqliteCommand(
                        "SELECT * FROM Commands WHERE Id = @Id",
                        connection);

                    command.Parameters.AddWithValue("@Id", id);

                    await using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var cmd = new Command
                            {
                                Id = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                                Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                Triggers = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                Response = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                KeyboardJson = reader.IsDBNull(4) ? null : reader.GetString(4),
                                CommandType = reader.IsDBNull(5) ? "text" : reader.GetString(5),
                                CreatedAt = reader.IsDBNull(6) ? DateTime.MinValue : reader.GetDateTime(6)
                            };

                            return Ok(new ApiResponse(true, "Команда найдена", cmd));
                        }
                    }
                }

                return NotFound(new ApiResponse(false, $"Команда с ID {id} не найдена"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении команды {Id}", id);
                return StatusCode(500, new ApiResponse(false, "Ошибка при получении команды", ex.Message));
            }
        }

        // POST: api/commands
        [HttpPost]
        public async Task<IActionResult> CreateCommand([FromBody] CreateCommandRequest request)
        {
            _logger.LogInformation("Создание новой команды: {Name}", request.Name);

            try
            {
                // Валидация
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return BadRequest(new ApiResponse(false, "Название команды обязательно"));
                }

                if (string.IsNullOrWhiteSpace(request.Response))
                {
                    return BadRequest(new ApiResponse(false, "Текст ответа обязателен"));
                }

                if (!System.IO.File.Exists(_dbPath))
                {
                    return StatusCode(500, new ApiResponse(false, $"Файл БД не найден: {_dbPath}"));
                }

                await using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    await connection.OpenAsync();

                    // Проверяем уникальность имени
                    var checkCommand = new SqliteCommand(
                        "SELECT COUNT(*) FROM Commands WHERE LOWER(Name) = LOWER(@Name)",
                        connection);
                    checkCommand.Parameters.AddWithValue("@Name", request.Name.Trim());

                    var count = Convert.ToInt64(await checkCommand.ExecuteScalarAsync());
                    if (count > 0)
                    {
                        return BadRequest(new ApiResponse(false, "Команда с таким именем уже существует"));
                    }

                    // Создаем команду
                    var insertCommand = new SqliteCommand(@"
                        INSERT INTO Commands (Name, Triggers, Response, KeyboardJson, CommandType, CreatedAt)
                        VALUES (@Name, @Triggers, @Response, @KeyboardJson, @CommandType, datetime('now'))",
                        connection);

                    insertCommand.Parameters.AddWithValue("@Name", request.Name.Trim());
                    insertCommand.Parameters.AddWithValue("@Triggers", request.Triggers?.Trim() ?? string.Empty);
                    insertCommand.Parameters.AddWithValue("@Response", request.Response.Trim());
                    insertCommand.Parameters.AddWithValue("@KeyboardJson",
                        string.IsNullOrWhiteSpace(request.KeyboardJson) ? DBNull.Value : request.KeyboardJson);
                    insertCommand.Parameters.AddWithValue("@CommandType",
                        string.IsNullOrWhiteSpace(request.CommandType) ? "text" : request.CommandType.Trim());

                    var rowsAffected = await insertCommand.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        var getLastIdCommand = new SqliteCommand("SELECT last_insert_rowid()", connection);
                        var newId = Convert.ToInt32(await getLastIdCommand.ExecuteScalarAsync());

                        _logger.LogInformation("Создана команда {Name} с ID {Id}", request.Name, newId);

                        return Ok(new ApiResponse(true, "Команда успешно создана", new { id = newId }));
                    }

                    return StatusCode(500, new ApiResponse(false, "Не удалось создать команду"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании команды");
                return StatusCode(500, new ApiResponse(false, "Ошибка при создании команды", ex.Message));
            }
        }

        // PUT: api/commands/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCommand(int id, [FromBody] UpdateCommandRequest request)
        {
            _logger.LogInformation("Обновление команды ID {Id}", id);

            try
            {
                if (!System.IO.File.Exists(_dbPath))
                {
                    return StatusCode(500, new ApiResponse(false, $"Файл БД не найден: {_dbPath}"));
                }

                await using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    await connection.OpenAsync();

                    // Проверяем существование команды
                    var checkCommand = new SqliteCommand(
                        "SELECT COUNT(*) FROM Commands WHERE Id = @Id",
                        connection);
                    checkCommand.Parameters.AddWithValue("@Id", id);

                    var count = Convert.ToInt64(await checkCommand.ExecuteScalarAsync());
                    if (count == 0)
                    {
                        return NotFound(new ApiResponse(false, $"Команда с ID {id} не найдена"));
                    }

                    // Проверяем уникальность имени (если оно меняется)
                    if (!string.IsNullOrWhiteSpace(request.Name))
                    {
                        var checkNameCommand = new SqliteCommand(
                            "SELECT COUNT(*) FROM Commands WHERE LOWER(Name) = LOWER(@Name) AND Id != @Id",
                            connection);
                        checkNameCommand.Parameters.AddWithValue("@Name", request.Name.Trim());
                        checkNameCommand.Parameters.AddWithValue("@Id", id);

                        var nameCount = Convert.ToInt64(await checkNameCommand.ExecuteScalarAsync());
                        if (nameCount > 0)
                        {
                            return BadRequest(new ApiResponse(false, "Команда с таким именем уже существует"));
                        }
                    }

                    // Обновляем команду
                    var updateCommand = new SqliteCommand(@"
                        UPDATE Commands 
                        SET Name = COALESCE(@Name, Name),
                            Triggers = COALESCE(@Triggers, Triggers),
                            Response = COALESCE(@Response, Response),
                            KeyboardJson = @KeyboardJson,
                            CommandType = COALESCE(@CommandType, CommandType)
                        WHERE Id = @Id",
                        connection);

                    updateCommand.Parameters.AddWithValue("@Id", id);
                    updateCommand.Parameters.AddWithValue("@Name",
                        string.IsNullOrWhiteSpace(request.Name) ? DBNull.Value : request.Name.Trim());
                    updateCommand.Parameters.AddWithValue("@Triggers",
                        string.IsNullOrWhiteSpace(request.Triggers) ? DBNull.Value : request.Triggers.Trim());
                    updateCommand.Parameters.AddWithValue("@Response",
                        string.IsNullOrWhiteSpace(request.Response) ? DBNull.Value : request.Response.Trim());
                    updateCommand.Parameters.AddWithValue("@KeyboardJson",
                        string.IsNullOrWhiteSpace(request.KeyboardJson) ? DBNull.Value : request.KeyboardJson);
                    updateCommand.Parameters.AddWithValue("@CommandType",
                        string.IsNullOrWhiteSpace(request.CommandType) ? DBNull.Value : request.CommandType.Trim());

                    var rowsAffected = await updateCommand.ExecuteNonQueryAsync();

                    _logger.LogInformation("Обновлена команда ID {Id}, затронуто строк: {Rows}", id, rowsAffected);

                    return Ok(new ApiResponse(true, "Команда успешно обновлена", new { rowsAffected }));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении команды ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, "Ошибка при обновлении команды", ex.Message));
            }
        }

        // DELETE: api/commands/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCommand(int id)
        {
            _logger.LogInformation("Удаление команды ID {Id}", id);

            try
            {
                if (!System.IO.File.Exists(_dbPath))
                {
                    return StatusCode(500, new ApiResponse(false, $"Файл БД не найден: {_dbPath}"));
                }

                await using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    await connection.OpenAsync();

                    // Получаем имя команды для логов
                    var getCommand = new SqliteCommand("SELECT Name FROM Commands WHERE Id = @Id", connection);
                    getCommand.Parameters.AddWithValue("@Id", id);
                    var name = await getCommand.ExecuteScalarAsync() as string;

                    var deleteCommand = new SqliteCommand("DELETE FROM Commands WHERE Id = @Id", connection);
                    deleteCommand.Parameters.AddWithValue("@Id", id);

                    var rowsAffected = await deleteCommand.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        _logger.LogInformation("Удалена команда ID {Id}, имя: {Name}", id, name);
                        return Ok(new ApiResponse(true, $"Команда '{name}' успешно удалена"));
                    }

                    return NotFound(new ApiResponse(false, $"Команда с ID {id} не найдена"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении команды ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, "Ошибка при удалении команды", ex.Message));
            }
        }

        // DELETE: api/commands/clear
        [HttpDelete("clear")]
        public async Task<IActionResult> ClearCommands()
        {
            _logger.LogInformation("Очистка всех команд");

            try
            {
                if (!System.IO.File.Exists(_dbPath))
                {
                    return StatusCode(500, new ApiResponse(false, $"Файл БД не найден: {_dbPath}"));
                }

                await using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    await connection.OpenAsync();

                    var command = new SqliteCommand("DELETE FROM Commands", connection);
                    var rowsAffected = await command.ExecuteNonQueryAsync();

                    // Сбрасываем автоинкремент
                    var resetCommand = new SqliteCommand("DELETE FROM sqlite_sequence WHERE name='Commands'", connection);
                    await resetCommand.ExecuteNonQueryAsync();

                    _logger.LogInformation("Очищено команд: {Count}", rowsAffected);

                    return Ok(new ApiResponse(true, $"Удалено {rowsAffected} команд", new { rowsAffected }));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при очистке команд");
                return StatusCode(500, new ApiResponse(false, "Ошибка при очистке таблицы", ex.Message));
            }
        }

        // GET: api/commands/settings
        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            _logger.LogInformation("Получение настроек бота");

            try
            {
                if (!System.IO.File.Exists(_dbPath))
                {
                    return Ok(new ApiResponse(false, "Файл БД не найден"));
                }

                await using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    await connection.OpenAsync();

                    // Создаем таблицу настроек если не существует
                    await CreateSettingsTableIfNotExists(connection);

                    // Получаем настройки
                    var getCmd = new SqliteCommand("SELECT * FROM BotSettings WHERE Id = 1", connection);

                    await using (var reader = await getCmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var settings = new BotSettingsDto
                            {
                                Id = reader.GetInt32(0),
                                BotName = reader.IsDBNull(1) ? "VK Бот" : reader.GetString(1),
                                VkToken = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                GroupId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                AutoStart = reader.IsDBNull(4) || reader.GetBoolean(4),
                                NotifyNewUsers = reader.IsDBNull(5) || reader.GetBoolean(5),
                                NotifyErrors = reader.IsDBNull(6) || reader.GetBoolean(6),
                                NotifyEmail = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                                LastUpdated = reader.IsDBNull(8) ? DateTime.Now : reader.GetDateTime(8)
                            };

                            _logger.LogInformation("Настройки загружены: {BotName}", settings.BotName);

                            return Ok(new ApiResponse(true, "Настройки загружены", settings));
                        }
                    }

                    return Ok(new ApiResponse(false, "Настройки не найдены"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении настроек");
                return StatusCode(500, new ApiResponse(false, "Ошибка при получении настроек", ex.Message));
            }
        }

        // POST: api/commands/settings
        [HttpPost("settings")]
        public async Task<IActionResult> SaveSettings([FromBody] BotSettingsRequest request)
        {
            _logger.LogInformation("Сохранение настроек бота");

            try
            {
                // Валидация
                if (string.IsNullOrWhiteSpace(request.BotName))
                {
                    return BadRequest(new ApiResponse(false, "Название бота обязательно"));
                }

                if (!string.IsNullOrEmpty(request.VkToken) && request.VkToken.Length < 20)
                {
                    return BadRequest(new ApiResponse(false, "Токен VK API должен быть не менее 20 символов"));
                }

                if (!System.IO.File.Exists(_dbPath))
                {
                    return StatusCode(500, new ApiResponse(false, "Файл БД не найден"));
                }

                await using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    await connection.OpenAsync();

                    // Создаем таблицу настроек если не существует
                    await CreateSettingsTableIfNotExists(connection);

                    // Сохраняем настройки
                    var updateCmd = new SqliteCommand(@"
                        INSERT OR REPLACE INTO BotSettings (
                            Id, BotName, VkToken, GroupId, AutoStart, 
                            NotifyNewUsers, NotifyErrors, NotifyEmail, LastUpdated
                        ) VALUES (
                            1, @BotName, @VkToken, @GroupId, @AutoStart,
                            @NotifyNewUsers, @NotifyErrors, @NotifyEmail, datetime('now')
                        )", connection);

                    updateCmd.Parameters.AddWithValue("@BotName", request.BotName ?? "VK Бот");
                    updateCmd.Parameters.AddWithValue("@VkToken", request.VkToken ?? string.Empty);
                    updateCmd.Parameters.AddWithValue("@GroupId", request.GroupId ?? string.Empty);
                    updateCmd.Parameters.AddWithValue("@AutoStart", request.AutoStart);
                    updateCmd.Parameters.AddWithValue("@NotifyNewUsers", request.NotifyNewUsers);
                    updateCmd.Parameters.AddWithValue("@NotifyErrors", request.NotifyErrors);
                    updateCmd.Parameters.AddWithValue("@NotifyEmail", request.NotifyEmail ?? string.Empty);

                    await updateCmd.ExecuteNonQueryAsync();

                    _logger.LogInformation("Настройки сохранены: {BotName}", request.BotName);

                    // Получаем обновленные настройки
                    var getCmd = new SqliteCommand("SELECT * FROM BotSettings WHERE Id = 1", connection);

                    await using (var reader = await getCmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var settings = new BotSettingsDto
                            {
                                BotName = reader.IsDBNull(1) ? "VK Бот" : reader.GetString(1),
                                VkToken = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                GroupId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                AutoStart = reader.IsDBNull(4) || reader.GetBoolean(4),
                                NotifyNewUsers = reader.IsDBNull(5) || reader.GetBoolean(5),
                                NotifyErrors = reader.IsDBNull(6) || reader.GetBoolean(6),
                                NotifyEmail = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                                LastUpdated = reader.IsDBNull(8) ? DateTime.Now : reader.GetDateTime(8)
                            };

                            return Ok(new ApiResponse(true, "Настройки сохранены", settings));
                        }
                    }

                    return StatusCode(500, new ApiResponse(false, "Не удалось сохранить настройки"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при сохранении настроек");
                return StatusCode(500, new ApiResponse(false, "Ошибка при сохранении настроек", ex.Message));
            }
        }

        // POST: api/commands/start-bot
        [HttpPost("start-bot")]
        public async Task<IActionResult> StartBot()
        {
            _logger.LogInformation("Запуск бота");

            try
            {
                // Симуляция запуска
                await Task.Delay(1000);

                var isRunning = await CheckIfBotIsRunningAsync();

                return Ok(new ApiResponse(true, "Команда на запуск бота отправлена", new
                {
                    isRunning,
                    timestamp = DateTime.Now
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при запуске бота");
                return StatusCode(500, new ApiResponse(false, "Ошибка при запуске бота", ex.Message));
            }
        }

        // POST: api/commands/stop-bot
        [HttpPost("stop-bot")]
        public async Task<IActionResult> StopBot()
        {
            _logger.LogInformation("Остановка бота");

            try
            {
                // Симуляция остановки
                await Task.Delay(1000);

                return Ok(new ApiResponse(true, "Команда на остановку бота отправлена", new
                {
                    timestamp = DateTime.Now
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при остановке бота");
                return StatusCode(500, new ApiResponse(false, "Ошибка при остановке бота", ex.Message));
            }
        }

        // GET: api/commands/bot-status
        [HttpGet("bot-status")]
        public async Task<IActionResult> GetBotStatus()
        {
            _logger.LogInformation("Получение статуса бота");

            try
            {
                var isRunning = await CheckIfBotIsRunningAsync();

                return Ok(new ApiResponse(true, "Статус бота получен", new
                {
                    isRunning,
                    status = isRunning ? "running" : "stopped",
                    timestamp = DateTime.Now
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении статуса бота");
                return Ok(new ApiResponse(false, "Ошибка при получении статуса", new
                {
                    isRunning = false,
                    status = "error",
                    error = ex.Message
                }));
            }
        }

        // GET: api/commands/bot-info
        [HttpGet("bot-info")]
        public async Task<IActionResult> GetBotInfo()
        {
            _logger.LogInformation("Получение информации о боте");

            try
            {
                if (!System.IO.File.Exists(_dbPath))
                {
                    return Ok(new ApiResponse(false, "База данных не найдена"));
                }

                await using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    await connection.OpenAsync();

                    // Проверяем существует ли таблица настроек
                    var checkTableCmd = new SqliteCommand(
                        "SELECT name FROM sqlite_master WHERE type='table' AND name='BotSettings'",
                        connection);

                    var tableExists = await checkTableCmd.ExecuteScalarAsync() != null;

                    if (!tableExists)
                    {
                        return Ok(new ApiResponse(false, "Таблица настроек не найдена"));
                    }

                    // Получаем настройки
                    var getCmd = new SqliteCommand("SELECT * FROM BotSettings WHERE Id = 1", connection);

                    await using (var reader = await getCmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var isRunning = await CheckIfBotIsRunningAsync();

                            return Ok(new ApiResponse(true, "Информация о боте получена", new
                            {
                                botName = reader.IsDBNull(1) ? "VK Бот" : reader.GetString(1),
                                groupId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                autoStart = reader.IsDBNull(4) || reader.GetBoolean(4),
                                lastUpdated = reader.IsDBNull(8) ? DateTime.Now : reader.GetDateTime(8),
                                version = GetBotVersion(),
                                isRunning,
                                uptime = isRunning ? await GetBotUptimeAsync() : TimeSpan.Zero,
                                timestamp = DateTime.Now
                            }));
                        }
                    }

                    return Ok(new ApiResponse(false, "Настройки не найдены"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении информации о боте");
                return StatusCode(500, new ApiResponse(false, "Ошибка при получении информации о боте", ex.Message));
            }
        }

        // GET: api/commands/test
        [HttpGet("test")]
        public async Task<IActionResult> Test()
        {
            _logger.LogInformation("Тестирование API");

            try
            {
                var dbExists = System.IO.File.Exists(_dbPath);
                var fileInfo = dbExists ? new System.IO.FileInfo(_dbPath) : null;

                int commandsCount = 0;
                if (dbExists)
                {
                    try
                    {
                        await using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                        {
                            await connection.OpenAsync();
                            var command = new SqliteCommand("SELECT COUNT(*) FROM Commands", connection);
                            commandsCount = Convert.ToInt32(await command.ExecuteScalarAsync());
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка подсчета команд");
                        commandsCount = -1;
                    }
                }

                return Ok(new ApiResponse(true, "API админ-панели работает", new
                {
                    dbPath = _dbPath,
                    exists = dbExists,
                    dbSize = fileInfo?.Length,
                    lastModified = fileInfo?.LastWriteTime,
                    commandsCount,
                    timestamp = DateTime.Now
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в тестовом методе");
                return Ok(new ApiResponse(false, "API работает, но есть ошибки", ex.Message));
            }
        }

        // GET: api/commands/structure
        [HttpGet("structure")]
        public async Task<IActionResult> GetTableStructure()
        {
            _logger.LogInformation("Получение структуры таблицы");

            try
            {
                if (!System.IO.File.Exists(_dbPath))
                {
                    return Ok(new ApiResponse(false, $"Файл БД не найден: {_dbPath}"));
                }

                await using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    await connection.OpenAsync();
                    var command = new SqliteCommand("PRAGMA table_info(Commands)", connection);

                    var columns = new List<TableColumnInfo>();
                    await using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            columns.Add(new TableColumnInfo
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                Type = reader.GetString(2),
                                NotNull = reader.GetInt32(3) == 1,
                                DefaultValue = reader.IsDBNull(4) ? null : reader.GetString(4),
                                IsPrimaryKey = reader.GetInt32(5) == 1
                            });
                        }
                    }

                    _logger.LogInformation("Структура таблицы загружена: {Count} колонок", columns.Count);

                    return Ok(new ApiResponse(true, "Структура таблицы получена", new
                    {
                        table = "Commands",
                        columns,
                        count = columns.Count
                    }));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении структуры таблицы");
                return StatusCode(500, new ApiResponse(false, "Ошибка при получении структуры таблицы", ex.Message));
            }
        }

        // GET: api/commands/check-table
        [HttpGet("check-table")]
        public async Task<IActionResult> CheckTableExists()
        {
            _logger.LogInformation("Проверка существования таблицы");

            try
            {
                if (!System.IO.File.Exists(_dbPath))
                {
                    _logger.LogWarning("Файл БД не найден: {DbPath}", _dbPath);
                    return Ok(new ApiResponse(false, "Файл БД не найден", new
                    {
                        dbPath = _dbPath,
                        dbExists = false,
                        tableExists = false
                    }));
                }

                await using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    await connection.OpenAsync();

                    // Проверяем существует ли таблица Commands
                    var checkTableCommand = new SqliteCommand(
                        "SELECT name FROM sqlite_master WHERE type='table' AND name='Commands'",
                        connection);

                    var tableExists = await checkTableCommand.ExecuteScalarAsync() != null;
                    _logger.LogInformation("Таблица Commands существует: {Exists}", tableExists);

                    if (tableExists)
                    {
                        // Проверяем структуру таблицы
                        var structureCommand = new SqliteCommand("PRAGMA table_info(Commands)", connection);
                        var columns = new List<string>();

                        await using (var reader = await structureCommand.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                columns.Add($"{reader.GetString(1)} ({reader.GetString(2)})");
                            }
                        }

                        return Ok(new ApiResponse(true, "Таблица найдена", new
                        {
                            tableExists = true,
                            columns,
                            columnCount = columns.Count,
                            dbExists = true
                        }));
                    }

                    return Ok(new ApiResponse(false, "Таблица не найдена", new
                    {
                        tableExists = false,
                        message = "Таблица Commands не существует",
                        dbExists = true
                    }));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке таблицы");
                return StatusCode(500, new ApiResponse(false, "Ошибка проверки таблицы", ex.Message));
            }
        }

        // POST: api/commands/init
        [HttpPost("init")]
        public async Task<IActionResult> InitializeTable()
        {
            _logger.LogInformation("Инициализация таблицы");

            try
            {
                await using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    await connection.OpenAsync();

                    // Создаем таблицу команд
                    var command = new SqliteCommand(@"
                        CREATE TABLE IF NOT EXISTS Commands (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Name TEXT NOT NULL,
                            Triggers TEXT DEFAULT '',
                            Response TEXT NOT NULL,
                            KeyboardJson TEXT NULL,
                            CommandType TEXT DEFAULT 'text',
                            CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                        )", connection);

                    await command.ExecuteNonQueryAsync();

                    // Создаем таблицу настроек
                    await CreateSettingsTableIfNotExists(connection);

                    _logger.LogInformation("Таблицы созданы/проверены");

                    return Ok(new ApiResponse(true, "Таблицы созданы/проверены", new
                    {
                        dbPath = _dbPath
                    }));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при инициализации таблицы");
                return StatusCode(500, new ApiResponse(false, "Ошибка при инициализации таблицы", ex.Message));
            }
        }

        // ==================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ====================

        private async Task CreateSettingsTableIfNotExists(SqliteConnection connection)
        {
            var checkTableCmd = new SqliteCommand(
                "SELECT name FROM sqlite_master WHERE type='table' AND name='BotSettings'",
                connection);

            var tableExists = await checkTableCmd.ExecuteScalarAsync() != null;

            if (!tableExists)
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

                _logger.LogInformation("Таблица BotSettings создана");
            }
        }

        private async Task<bool> CheckIfBotIsRunningAsync()
        {
            try
            {
                // 1. Проверка через процесс
                var processes = System.Diagnostics.Process.GetProcessesByName("VKBot_nordciti");
                if (processes.Length > 0) return true;

                // 2. Проверка через файл блокировки
                var lockFilePath = Path.Combine(Path.GetDirectoryName(_dbPath) ?? "", "bot.lock");
                if (System.IO.File.Exists(lockFilePath))
                {
                    var fileInfo = new System.IO.FileInfo(lockFilePath);
                    if (fileInfo.CreationTime > DateTime.Now.AddMinutes(-5))
                    {
                        return true;
                    }
                }

                // 3. Проверка через HTTP
                try
                {
                    using var httpClient = new HttpClient();
                    httpClient.Timeout = TimeSpan.FromSeconds(3);
                    var response = await httpClient.GetAsync("http://localhost:5000/health");
                    return response.IsSuccessStatusCode;
                }
                catch
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task<TimeSpan> GetBotUptimeAsync()
        {
            try
            {
                var lockFilePath = Path.Combine(Path.GetDirectoryName(_dbPath) ?? "", "bot.lock");
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

        private string GetBotVersion()
        {
            var versionFilePath = Path.Combine(Path.GetDirectoryName(_dbPath) ?? "", "version.txt");
            if (System.IO.File.Exists(versionFilePath))
            {
                return System.IO.File.ReadAllText(versionFilePath).Trim();
            }

            return "1.0.0";
        }
    }

    // ==================== МОДЕЛИ ЗАПРОСОВ ====================

    public class CreateCommandRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Triggers { get; set; }
        public string Response { get; set; } = string.Empty;
        public string? KeyboardJson { get; set; }
        public string CommandType { get; set; } = "text";
    }

    public class UpdateCommandRequest
    {
        public string? Name { get; set; }
        public string? Triggers { get; set; }
        public string? Response { get; set; }
        public string? KeyboardJson { get; set; }
        public string? CommandType { get; set; }
    }

    public class BotSettingsRequest
    {
        public string BotName { get; set; } = string.Empty;
        public string VkToken { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;
        public bool AutoStart { get; set; } = true;
        public bool NotifyNewUsers { get; set; } = true;
        public bool NotifyErrors { get; set; } = true;
        public string NotifyEmail { get; set; } = string.Empty;
    }

    public class BotSettingsDto
    {
        public int Id { get; set; }
        public string BotName { get; set; } = string.Empty;
        public string VkToken { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;
        public bool AutoStart { get; set; } = true;
        public bool NotifyNewUsers { get; set; } = true;
        public bool NotifyErrors { get; set; } = true;
        public string NotifyEmail { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }
    }

    public class TableColumnInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool NotNull { get; set; }
        public string? DefaultValue { get; set; }
        public bool IsPrimaryKey { get; set; }
    }
}