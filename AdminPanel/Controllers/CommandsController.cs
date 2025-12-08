using Microsoft.AspNetCore.Mvc;
using AdminPanel.Services;
using AdminPanel.Models;
using BotSettingsDto = AdminPanel.Services.BotSettingsDto;

namespace AdminPanel.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CommandsController : ControllerBase
    {
        private readonly DatabaseService _dbService;
        private readonly BotStatusService _botStatusService;
        private readonly ILogger<CommandsController> _logger;

        public CommandsController(
            DatabaseService dbService,
            BotStatusService botStatusService,
            ILogger<CommandsController> logger)
        {
            _dbService = dbService;
            _botStatusService = botStatusService;
            _logger = logger;
        }

        // GET: api/commands
        [HttpGet]
        public async Task<IActionResult> GetCommands(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string search = "")
        {
            _logger.LogInformation("GET /api/commands?page={Page}&pageSize={PageSize}&search={Search}",
                page, pageSize, search);

            try
            {
                var commands = await _dbService.GetCommandsAsync(page, pageSize, search);
                var totalCount = await _dbService.GetCommandsCountAsync(search);

                var response = new
                {
                    success = true,
                    message = "Команды успешно загружены",
                    data = new
                    {
                        commands,
                        pagination = new
                        {
                            page,
                            pageSize,
                            totalCount,
                            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                        },
                        searchTerm = search
                    },
                    timestamp = DateTime.UtcNow
                };

                return Ok(response);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("не найдена") ||
                                                      ex.Message.Contains("не существует"))
            {
                _logger.LogWarning(ex, "Таблица команд не найдена");

                return Ok(new
                {
                    success = false,
                    message = "Таблица команд не найдена. Нажмите 'Создать таблицу' для инициализации базы данных.",
                    data = new
                    {
                        commands = new List<Command>(),
                        pagination = new
                        {
                            page,
                            pageSize,
                            totalCount = 0,
                            totalPages = 0
                        }
                    },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении команд");

                return StatusCode(500, new
                {
                    success = false,
                    message = $"Ошибка загрузки команд: {ex.Message}",
                    data = new { error = ex.ToString() },
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // GET: api/commands/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetCommand(int id)
        {
            _logger.LogInformation("GET /api/commands/{Id}", id);

            try
            {
                var command = await _dbService.GetCommandByIdAsync(id);

                if (command == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = $"Команда с ID {id} не найдена",
                        timestamp = DateTime.UtcNow
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "Команда найдена",
                    data = command,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении команды ID {Id}", id);

                return StatusCode(500, new
                {
                    success = false,
                    message = $"Ошибка загрузки команды: {ex.Message}",
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // POST: api/commands
        [HttpPost]
        public async Task<IActionResult> CreateCommand([FromBody] CreateCommandRequest request)
        {
            _logger.LogInformation("POST /api/commands - создание команды: {Name}", request.Name);

            try
            {
                // Валидация
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Название команды обязательно",
                        timestamp = DateTime.UtcNow
                    });
                }

                if (string.IsNullOrWhiteSpace(request.Response))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Текст ответа обязателен",
                        timestamp = DateTime.UtcNow
                    });
                }

                var command = new Command
                {
                    Name = request.Name,
                    Triggers = request.Triggers ?? "",
                    Response = request.Response,
                    KeyboardJson = request.KeyboardJson,
                    CommandType = request.CommandType ?? "text"
                };

                var id = await _dbService.AddCommandAsync(command);

                _logger.LogInformation("Создана команда: {Name} (ID: {Id})", request.Name, id);

                return Ok(new
                {
                    success = true,
                    message = "Команда успешно создана",
                    data = new { id },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("уже существует"))
            {
                _logger.LogInformation("Попытка создать дубликат команды: {CommandName}", request.Name);

                return Conflict(new
                {
                    success = false,
                    message = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании команды");

                return StatusCode(500, new
                {
                    success = false,
                    message = $"Ошибка создания команды: {ex.Message}",
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // PUT: api/commands/{id}
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateCommand(int id, [FromBody] UpdateCommandRequest request)
        {
            _logger.LogInformation("PUT /api/commands/{Id} - обновление команды", id);

            try
            {
                var existingCommand = await _dbService.GetCommandByIdAsync(id);
                if (existingCommand == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = $"Команда с ID {id} не найдена",
                        timestamp = DateTime.UtcNow
                    });
                }

                // Обновляем только переданные поля
                if (!string.IsNullOrWhiteSpace(request.Name))
                    existingCommand.Name = request.Name;

                if (request.Triggers != null)
                    existingCommand.Triggers = request.Triggers;

                if (!string.IsNullOrWhiteSpace(request.Response))
                    existingCommand.Response = request.Response;

                if (request.KeyboardJson != null)
                    existingCommand.KeyboardJson = request.KeyboardJson;

                if (!string.IsNullOrWhiteSpace(request.CommandType))
                    existingCommand.CommandType = request.CommandType;

                var success = await _dbService.UpdateCommandAsync(existingCommand);

                if (!success)
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = "Не удалось обновить команду",
                        timestamp = DateTime.UtcNow
                    });
                }

                _logger.LogInformation("Обновлена команда ID {Id}: {Name}", id, existingCommand.Name);

                return Ok(new
                {
                    success = true,
                    message = "Команда успешно обновлена",
                    data = new { id },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("уже существует"))
            {
                _logger.LogWarning(ex, "Попытка переименовать команду в существующее имя");

                return Conflict(new
                {
                    success = false,
                    message = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Команда не найдена при обновлении");

                return NotFound(new
                {
                    success = false,
                    message = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении команды ID {Id}", id);

                return StatusCode(500, new
                {
                    success = false,
                    message = $"Ошибка обновления команды: {ex.Message}",
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // DELETE: api/commands/{id}
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteCommand(int id)
        {
            _logger.LogInformation("DELETE /api/commands/{Id}", id);

            try
            {
                var success = await _dbService.DeleteCommandAsync(id);

                if (!success)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = $"Команда с ID {id} не найдена",
                        timestamp = DateTime.UtcNow
                    });
                }

                _logger.LogInformation("Удалена команда ID {Id}", id);

                return Ok(new
                {
                    success = true,
                    message = "Команда успешно удалена",
                    data = new { id },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении команды ID {Id}", id);

                return StatusCode(500, new
                {
                    success = false,
                    message = $"Ошибка удаления команды: {ex.Message}",
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // DELETE: api/commands/clear
        [HttpDelete("clear")]
        public async Task<IActionResult> ClearCommands()
        {
            _logger.LogInformation("DELETE /api/commands/clear - очистка всех команд");

            try
            {
                var count = await _dbService.ClearAllCommandsAsync();

                _logger.LogInformation("Очищено команд: {Count}", count);

                return Ok(new
                {
                    success = true,
                    message = $"Удалено {count} команд",
                    data = new { rowsAffected = count },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при очистке команд");

                return StatusCode(500, new
                {
                    success = false,
                    message = $"Ошибка очистки команд: {ex.Message}",
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // GET: api/commands/settings
        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            _logger.LogInformation("GET /api/commands/settings");

            try
            {
                var settings = await _dbService.GetBotSettingsAsync();

                return Ok(new
                {
                    success = true,
                    message = "Настройки загружены",
                    data = settings,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении настроек");

                return Ok(new
                {
                    success = false,
                    message = $"Ошибка загрузки настроек: {ex.Message}",
                    data = new AdminPanel.Models.BotSettingsDto
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
                    },
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // POST: api/commands/settings
        [HttpPost("settings")]
        public async Task<IActionResult> SaveSettings([FromBody] BotSettingsDto request)
        {
            _logger.LogInformation("POST /api/commands/settings - сохранение настроек");

            try
            {
                // Валидация
                if (string.IsNullOrWhiteSpace(request.BotName))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Название бота обязательно",
                        timestamp = DateTime.UtcNow
                    });
                }

                if (!string.IsNullOrEmpty(request.VkToken) && request.VkToken.Length < 20)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Токен VK API должен быть не менее 20 символов",
                        timestamp = DateTime.UtcNow
                    });
                }

                var success = await _dbService.SaveBotSettingsAsync(request);

                if (!success)
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = "Не удалось сохранить настройки",
                        timestamp = DateTime.UtcNow
                    });
                }

                _logger.LogInformation("Сохранены настройки бота: {BotName}", request.BotName);

                // Получаем обновленные настройки
                var updatedSettings = await _dbService.GetBotSettingsAsync();

                return Ok(new
                {
                    success = true,
                    message = "Настройки сохранены",
                    data = updatedSettings,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при сохранении настроек");

                return StatusCode(500, new
                {
                    success = false,
                    message = $"Ошибка сохранения настроек: {ex.Message}",
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // POST: api/commands/start-bot
        [HttpPost("start-bot")]
        public async Task<IActionResult> StartBot()
        {
            _logger.LogInformation("POST /api/commands/start-bot - запуск бота");

            try
            {
                var result = await _botStatusService.StartBotAsync();

                if (!result.Success)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = result.Message,
                        data = result.Data,
                        timestamp = DateTime.UtcNow
                    });
                }

                _logger.LogInformation("Команда на запуск бота выполнена: {Message}", result.Message);

                // Ждем немного и возвращаем обновленный статус
                await Task.Delay(2000);
                var botStatus = await _botStatusService.GetBotStatusAsync();

                return Ok(new
                {
                    success = true,
                    message = result.Message,
                    data = new
                    {
                        result.Data,
                        botStatus
                    },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при запуске бота");

                return StatusCode(500, new
                {
                    success = false,
                    message = $"Ошибка запуска бота: {ex.Message}",
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // POST: api/commands/stop-bot
        [HttpPost("stop-bot")]
        public async Task<IActionResult> StopBot()
        {
            _logger.LogInformation("POST /api/commands/stop-bot - остановка бота");

            try
            {
                var result = await _botStatusService.StopBotAsync();

                if (!result.Success)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = result.Message,
                        data = result.Data,
                        timestamp = DateTime.UtcNow
                    });
                }

                _logger.LogInformation("Команда на остановку бота выполнена: {Message}", result.Message);

                // Ждем немного и возвращаем обновленный статус
                await Task.Delay(1000);
                var botStatus = await _botStatusService.GetBotStatusAsync();

                return Ok(new
                {
                    success = true,
                    message = result.Message,
                    data = new
                    {
                        result.Data,
                        botStatus
                    },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при остановке бота");

                return StatusCode(500, new
                {
                    success = false,
                    message = $"Ошибка остановки бота: {ex.Message}",
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // GET: api/commands/bot-info
        [HttpGet("bot-info")]
        public async Task<IActionResult> GetBotInfo()
        {
            _logger.LogInformation("GET /api/commands/bot-info - информация о боте");

            try
            {
                var botStatus = await _botStatusService.GetBotStatusAsync();
                var dbInfo = await _dbService.GetDatabaseInfoAsync();
                var settings = await _dbService.GetBotSettingsAsync();

                var response = new
                {
                    botName = settings.BotName,
                    groupId = settings.GroupId,
                    autoStart = settings.AutoStart,
                    lastUpdated = settings.LastUpdated,
                    version = botStatus.Version,
                    isRunning = botStatus.ProcessInfo.IsRunning,
                    uptime = botStatus.Uptime.TotalSeconds,
                    processId = botStatus.ProcessInfo.ProcessId,
                    apiStatus = botStatus.ApiStatus.IsResponding,
                    overallStatus = botStatus.OverallStatus,
                    databaseInfo = dbInfo,
                    timestamp = DateTime.UtcNow
                };

                return Ok(new
                {
                    success = true,
                    message = "Информация о боте получена",
                    data = response,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении информации о боте");

                return Ok(new
                {
                    success = false,
                    message = $"Ошибка получения информации: {ex.Message}",
                    data = new
                    {
                        botName = "VK Бот",
                        isRunning = false,
                        uptime = 0,
                        overallStatus = "error",
                        timestamp = DateTime.UtcNow
                    },
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // GET: api/commands/db-info
        [HttpGet("db-info")]
        public async Task<IActionResult> GetDatabaseInfo()
        {
            _logger.LogInformation("GET /api/commands/db-info - информация о БД");

            try
            {
                var dbInfo = await _dbService.GetDatabaseInfoAsync();

                return Ok(new
                {
                    success = true,
                    message = "Информация о БД получена",
                    data = dbInfo,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении информации о БД");

                return Ok(new
                {
                    success = false,
                    message = $"Ошибка получения информации: {ex.Message}",
                    data = new DatabaseInfo
                    {
                        Exists = false,
                        Error = ex.Message
                    },
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // POST: api/commands/migrate
        [HttpPost("migrate")]
        public async Task<IActionResult> MigrateDatabase()
        {
            _logger.LogInformation("POST /api/commands/migrate - миграция БД");

            try
            {
                var success = await _dbService.MigrateDatabaseAsync();

                if (!success)
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = "Не удалось выполнить миграцию БД",
                        timestamp = DateTime.UtcNow
                    });
                }

                _logger.LogInformation("Миграция БД выполнена успешно");

                return Ok(new
                {
                    success = true,
                    message = "Миграция БД выполнена успешно",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при миграции БД");

                return StatusCode(500, new
                {
                    success = false,
                    message = $"Ошибка миграции: {ex.Message}",
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // POST: api/commands/recreate-db
        [HttpPost("recreate-db")]
        public async Task<IActionResult> RecreateDatabase()
        {
            _logger.LogInformation("POST /api/commands/recreate-db - пересоздание БД");

            try
            {
                var success = await _dbService.RecreateDatabaseAsync();

                if (!success)
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = "Не удалось пересоздать БД",
                        timestamp = DateTime.UtcNow
                    });
                }

                _logger.LogInformation("БД успешно пересоздана");

                return Ok(new
                {
                    success = true,
                    message = "База данных успешно пересоздана",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при пересоздании БД");

                return StatusCode(500, new
                {
                    success = false,
                    message = $"Ошибка пересоздания БД: {ex.Message}",
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // GET: api/commands/test
        [HttpGet("test")]
        public async Task<IActionResult> Test()
        {
            _logger.LogInformation("GET /api/commands/test - тест API");

            try
            {
                var dbInfo = await _dbService.GetDatabaseInfoAsync();
                var botStatus = await _botStatusService.GetBotStatusAsync();

                return Ok(new
                {
                    success = true,
                    message = "API админ-панели работает",
                    data = new
                    {
                        api = "AdminPanel API",
                        version = "1.0.0",
                        timestamp = DateTime.UtcNow,
                        database = dbInfo,
                        botStatus = new
                        {
                            isRunning = botStatus.ProcessInfo.IsRunning,
                            overallStatus = botStatus.OverallStatus
                        },
                        environment = Environment.MachineName,
                        os = Environment.OSVersion.VersionString
                    },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в тестовом методе");

                return Ok(new
                {
                    success = false,
                    message = $"API работает, но есть ошибки: {ex.Message}",
                    data = new { error = ex.ToString() },
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // POST: api/commands/init
        [HttpPost("init")]
        public async Task<IActionResult> InitializeDatabase()
        {
            _logger.LogInformation("POST /api/commands/init - инициализация БД");

            try
            {
                var success = await _dbService.InitializeDatabaseAsync();

                if (!success)
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = "Не удалось инициализировать базу данных",
                        timestamp = DateTime.UtcNow
                    });
                }

                _logger.LogInformation("База данных успешно инициализирована");

                // Получаем обновленную информацию
                var dbInfo = await _dbService.GetDatabaseInfoAsync();

                return Ok(new
                {
                    success = true,
                    message = "База данных инициализирована",
                    data = dbInfo,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при инициализации БД");

                return StatusCode(500, new
                {
                    success = false,
                    message = $"Ошибка инициализации: {ex.Message}",
                    timestamp = DateTime.UtcNow
                });
            }
        }
    }

    // Модели запросов
    public class CreateCommandRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Triggers { get; set; }
        public string Response { get; set; } = string.Empty;
        public string? KeyboardJson { get; set; }
        public string? CommandType { get; set; }
    }

    public class UpdateCommandRequest
    {
        public string? Name { get; set; }
        public string? Triggers { get; set; }
        public string? Response { get; set; }
        public string? KeyboardJson { get; set; }
        public string? CommandType { get; set; }
    }
}