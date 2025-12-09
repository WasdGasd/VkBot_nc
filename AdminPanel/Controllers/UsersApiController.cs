using Microsoft.AspNetCore.Mvc;
using AdminPanel.Services;
using AdminPanel.Models;
using AdminPanel.Models.BotApi;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace AdminPanel.Controllers
{
    [ApiController]
    [Route("api/v1/users")]
    public class UsersApiController : ControllerBase
    {
        private readonly UserService _userService;
        private readonly ILogger<UsersApiController> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        public UsersApiController(
            UserService userService,
            ILogger<UsersApiController> logger,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _userService = userService;
            _logger = logger;
            _configuration = configuration;
            _httpClient = httpClientFactory.CreateClient("BotApi");
        }

        // ==================== ДИАГНОСТИКА ====================

        [HttpGet("diagnostic")]
        public async Task<IActionResult> Diagnostic()
        {
            _logger.LogInformation("GET /api/v1/users/diagnostic");

            try
            {
                var dbPath = _configuration["ConnectionStrings:DefaultConnection"]
                    ?? @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db";
                var fileExists = System.IO.File.Exists(dbPath);
                var fileSize = fileExists ? new FileInfo(dbPath).Length : 0;

                string? tableExists = null;
                int? userCount = null;
                List<string>? allTables = null;
                string? connectionError = null;

                if (fileExists)
                {
                    try
                    {
                        using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=true;Cache=Shared;");
                        await connection.OpenAsync();

                        // Получаем все таблицы
                        var tablesCmd = new SqliteCommand(
                            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name",
                            connection);
                        using var reader = await tablesCmd.ExecuteReaderAsync();
                        allTables = new List<string>();
                        while (await reader.ReadAsync())
                        {
                            allTables.Add(reader.GetString(0));
                        }

                        // Проверяем таблицу Users
                        var checkTableCmd = new SqliteCommand(
                            "SELECT name FROM sqlite_master WHERE type='table' AND name='Users'",
                            connection);
                        tableExists = await checkTableCmd.ExecuteScalarAsync() as string;

                        if (tableExists != null)
                        {
                            var countCmd = new SqliteCommand("SELECT COUNT(*) FROM Users", connection);
                            userCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                        }
                    }
                    catch (Exception ex)
                    {
                        connectionError = ex.Message;
                        tableExists = $"Ошибка подключения: {ex.Message}";
                    }
                }

                // Пробуем получить пользователей через сервис
                UserListResponse? usersResponse = null;
                bool canGetUsers = false;
                string? serviceError = null;

                try
                {
                    usersResponse = await _userService.GetUsersAsync(1, 5);
                    canGetUsers = true;
                }
                catch (Exception ex)
                {
                    serviceError = ex.Message;
                }

                return Ok(new
                {
                    success = true,
                    message = "Диагностическая информация",
                    data = new
                    {
                        database = new
                        {
                            path = dbPath,
                            exists = fileExists,
                            size_kb = fileSize / 1024,
                            tables = allTables,
                            users_table_exists = tableExists != null && !tableExists.Contains("Ошибка"),
                            users_count = userCount,
                            connection_error = connectionError
                        },
                        service = new
                        {
                            can_get_users = canGetUsers,
                            service_error = serviceError,
                            total_users = usersResponse?.TotalCount ?? 0,
                            loaded_users = usersResponse?.Users?.Count ?? 0,
                            sample_user = usersResponse?.Users?.FirstOrDefault()?.FirstName
                        },
                        environment = new
                        {
                            machine_name = Environment.MachineName,
                            os_version = Environment.OSVersion.VersionString,
                            user = Environment.UserName,
                            current_directory = Environment.CurrentDirectory
                        },
                        timestamp = DateTime.UtcNow
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при диагностике");
                return StatusCode(500, new ApiResponse(false, $"Диагностическая ошибка: {ex.Message}", new
                {
                    error_details = ex.ToString()
                }));
            }
        }

        // ==================== ОСНОВНЫЕ МЕТОДЫ ДЛЯ ПОЛЬЗОВАТЕЛЕЙ ====================

        [HttpGet]
        public async Task<IActionResult> GetUsers(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string search = "",
            [FromQuery] string status = "all",
            [FromQuery] string sort = "newest")
        {
            _logger.LogInformation("GET /api/v1/users?page={Page}&pageSize={PageSize}&search={Search}&status={Status}&sort={Sort}",
                page, pageSize, search ?? "", status, sort);

            try
            {
                // Сначала пробуем получить данные напрямую из API бота
                var realUsersResponse = await GetUsersFromBotApi();

                if (realUsersResponse != null && realUsersResponse.Users.Any())
                {
                    _logger.LogInformation("Получено {Count} реальных пользователей из API бота",
                        realUsersResponse.Users.Count);

                    // Фильтрация и сортировка
                    var filteredUsers = FilterUsers(realUsersResponse.Users, search, status);
                    var sortedUsers = SortUsers(filteredUsers, sort);

                    var totalCount = filteredUsers.Count;
                    var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                    var paginatedUsers = sortedUsers
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();

                    var result = new
                    {
                        Users = paginatedUsers.Select(u => new User
                        {
                            Id = (int)u.VkId, // Используем VkId как Id для фронтенда
                            VkUserId = u.VkId,
                            FirstName = u.FirstName ?? "Неизвестно",
                            LastName = u.LastName ?? "",
                            Username = u.Username ?? "",
                            IsActive = u.IsActive,
                            IsOnline = u.IsOnline,
                            LastActivity = u.LastSeen,
                            MessageCount = u.MessagesCount,
                            RegistrationDate = u.RegisteredAt,
                            IsBanned = u.IsBanned,
                            Status = u.Status ?? "user"
                        }).ToList(),
                        TotalCount = totalCount,
                        Page = page,
                        PageSize = pageSize,
                        TotalPages = totalPages,
                        ActiveCount = realUsersResponse.Users.Count(u => u.IsActive && !u.IsBanned),
                        OnlineCount = realUsersResponse.Users.Count(u => u.IsOnline),
                        NewTodayCount = realUsersResponse.Users.Count(u => u.RegisteredAt.Date == DateTime.Today)
                    };

                    return Ok(new ApiResponse(true, "Реальные пользователи получены из бота", result));
                }

                _logger.LogWarning("Не удалось получить пользователей из API бота, используем локальные данные");

                // Если не удалось получить из API, используем UserService
                var serviceResult = await _userService.GetUsersAsync(page, pageSize, search ?? "", status, sort);
                return Ok(new ApiResponse(true, "Пользователи получены из локальной базы", serviceResult));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователей");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpGet("test-bot-api")]
        public async Task<IActionResult> TestBotApiConnection()
        {
            try
            {
                var botApiUrl = _configuration["BotApi:BaseUrl"] ?? "http://localhost:5000";

                _logger.LogInformation("Тестирование подключения к API бота: {Url}", botApiUrl);

                // 1. Проверяем доступность базового URL
                var healthResponse = await _httpClient.GetAsync($"{botApiUrl}/health");
                var healthStatus = healthResponse.IsSuccessStatusCode ? "доступен" : "недоступен";

                // 2. Проверяем endpoint пользователей
                var usersResponse = await _httpClient.GetAsync($"{botApiUrl}/api/adminapi/users?all=true");
                var usersStatus = usersResponse.IsSuccessStatusCode ? "доступен" : "недоступен";

                string usersContent = "";
                if (usersResponse.IsSuccessStatusCode)
                {
                    usersContent = await usersResponse.Content.ReadAsStringAsync();
                    var usersData = JsonSerializer.Deserialize<BotApiUserListResponse>(usersContent,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    return Ok(new ApiResponse(true, "API бота доступен", new
                    {
                        botApiUrl,
                        healthEndpoint = healthStatus,
                        usersEndpoint = usersStatus,
                        usersCount = usersData?.Users?.Count ?? 0,
                        sampleData = usersData?.Users?.Take(3),
                        rawResponse = usersContent.Length > 500 ? usersContent.Substring(0, 500) + "..." : usersContent
                    }));
                }
                else
                {
                    return Ok(new ApiResponse(false, "API бота недоступен", new
                    {
                        botApiUrl,
                        healthEndpoint = healthStatus,
                        usersEndpoint = usersStatus,
                        errorCode = (int)usersResponse.StatusCode,
                        errorReason = usersResponse.ReasonPhrase
                    }));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка тестирования API бота");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // UsersApiController.cs - в методе GetUser обновите код:

        // В UsersApiController.cs исправьте метод GetUser:

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetUser(int id)
        {
            _logger.LogInformation("GET /api/v1/users/{Id}", id);

            try
            {
                // Сначала получаем пользователя
                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                // Пробуем получить реальные данные из бота
                try
                {
                    var botApiUrl = _configuration["BotApi:BaseUrl"] ?? "http://localhost:5000";
                    var response = await _httpClient.GetAsync($"{botApiUrl}/api/adminapi/users/{user.VkUserId}");

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var realUser = JsonSerializer.Deserialize<BotApiUserDetailResponse>(content, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (realUser != null && realUser.User != null)
                        {
                            return Ok(new ApiResponse(true, "Пользователь найден (реальные данные)", new
                            {
                                user = new User
                                {
                                    Id = user.Id,
                                    VkUserId = realUser.User.VkId,
                                    FirstName = realUser.User.FirstName ?? "Неизвестно",
                                    LastName = realUser.User.LastName ?? "",
                                    Username = realUser.User.Username ?? "",
                                    IsActive = realUser.User.IsActive,
                                    IsOnline = realUser.User.IsOnline,
                                    LastActivity = realUser.User.LastSeen,
                                    MessageCount = realUser.User.MessagesCount,
                                    RegistrationDate = realUser.User.RegisteredAt,
                                    IsBanned = realUser.User.IsBanned,
                                    Status = realUser.User.Status ?? "user"
                                },
                                recentMessages = realUser.Messages.Select(m => new Message
                                {
                                    Id = m.Id,
                                    VkUserId = m.VkId,
                                    MessageText = m.Text,
                                    IsFromUser = m.IsFromUser,
                                    MessageDate = m.SentAt
                                }),
                                statistics = realUser.Stats
                            }));
                        }
                    }
                }
                catch (Exception apiEx)
                {
                    _logger.LogWarning(apiEx, "Не удалось получить реальные данные пользователя");
                }

                // Если не удалось получить реальные данные, используем локальные
                var messages = await _userService.GetUserMessagesAsync(user.VkUserId, 10);

                return Ok(new ApiResponse(true, "Пользователь найден (локальные данные)", new
                {
                    user,
                    recentMessages = messages,
                    statistics = new
                    {
                        totalMessages = user.MessageCount,
                        isOnline = user.IsOnline,
                        lastActivity = user.LastActivity
                    }
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователя ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] User user)
        {
            _logger.LogInformation("POST /api/v1/users - создание пользователя VK ID: {VkUserId}", user.VkUserId);

            try
            {
                // Валидация
                if (user.VkUserId <= 0)
                {
                    return BadRequest(new ApiResponse(false, "VK ID обязателен и должен быть больше 0"));
                }

                if (string.IsNullOrWhiteSpace(user.FirstName))
                {
                    return BadRequest(new ApiResponse(false, "Имя пользователя обязательно"));
                }

                var createdUser = await _userService.AddOrUpdateUserAsync(user);

                return Ok(new ApiResponse(true, "Пользователь создан/обновлен", createdUser));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании пользователя");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] User user)
        {
            _logger.LogInformation("PUT /api/v1/users/{Id} - обновление пользователя", id);

            try
            {
                var existingUser = await _userService.GetUserByIdAsync(id);
                if (existingUser == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                // Обновляем только разрешенные поля
                if (!string.IsNullOrWhiteSpace(user.FirstName))
                    existingUser.FirstName = user.FirstName;

                if (!string.IsNullOrWhiteSpace(user.LastName))
                    existingUser.LastName = user.LastName;

                if (!string.IsNullOrWhiteSpace(user.Username))
                    existingUser.Username = user.Username;

                if (!string.IsNullOrWhiteSpace(user.Email))
                    existingUser.Email = user.Email;

                if (!string.IsNullOrWhiteSpace(user.Phone))
                    existingUser.Phone = user.Phone;

                if (!string.IsNullOrWhiteSpace(user.Location))
                    existingUser.Location = user.Location;

                if (!string.IsNullOrWhiteSpace(user.Status))
                    existingUser.Status = user.Status;

                existingUser.IsActive = user.IsActive;
                existingUser.IsBanned = user.IsBanned;
                existingUser.IsOnline = user.IsOnline;

                var updatedUser = await _userService.AddOrUpdateUserAsync(existingUser);

                return Ok(new ApiResponse(true, "Пользователь обновлен", updatedUser));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении пользователя ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpGet("simple")]
        public async Task<IActionResult> GetUsersSimple()
        {
            try
            {
                // Прямой запрос к API бота
                var botApiUrl = _configuration["BotApi:BaseUrl"] ?? "http://localhost:5000";
                var response = await _httpClient.GetAsync($"{botApiUrl}/api/adminapi/users?all=true");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("API бота недоступен: {StatusCode}", response.StatusCode);

                    // Возвращаем локальные данные
                    var localResult = await _userService.GetUsersAsync(1, 20);
                    return Ok(new ApiResponse(false, "API бота недоступен, локальные данные", localResult));
                }

                var content = await response.Content.ReadAsStringAsync();
                var realUsers = JsonSerializer.Deserialize<BotApiUserListResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (realUsers == null || !realUsers.Users.Any())
                {
                    _logger.LogWarning("API бота вернул пустой список");
                    var localResult = await _userService.GetUsersAsync(1, 20);
                    return Ok(new ApiResponse(false, "API бота вернул пустые данные", localResult));
                }

                // Преобразуем в формат фронтенда
                var users = realUsers.Users.Select(u => new User
                {
                    Id = (int)u.VkId,
                    VkUserId = u.VkId,
                    FirstName = u.FirstName ?? "Неизвестно",
                    LastName = u.LastName ?? "",
                    Username = u.Username ?? "",
                    IsActive = u.IsActive,
                    IsOnline = u.IsOnline,
                    LastActivity = u.LastSeen,
                    MessageCount = u.MessagesCount,
                    RegistrationDate = u.RegisteredAt,
                    IsBanned = u.IsBanned,
                    Status = u.Status ?? "user"
                }).ToList();

                return Ok(new ApiResponse(true, "Реальные данные из API бота", new
                {
                    Users = users,
                    TotalCount = realUsers.TotalCount,
                    ActiveCount = realUsers.ActiveCount,
                    OnlineCount = realUsers.OnlineCount,
                    NewTodayCount = realUsers.NewTodayCount
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователей");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // ==================== МЕТОДЫ ДЛЯ СТАТУСА И АКТИВНОСТИ ====================

        [HttpPatch("{id:int}/status")]
        public async Task<IActionResult> UpdateUserStatus(int id, [FromBody] UpdateUserStatusRequest request)
        {
            _logger.LogInformation("PATCH /api/v1/users/{Id}/status - обновление статуса", id);

            try
            {
                var success = await _userService.UpdateUserStatusAsync(id, request.IsActive, request.IsBanned);

                if (!success)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                return Ok(new ApiResponse(true, "Статус пользователя обновлен"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении статуса пользователя ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpPatch("vk/{vkUserId:long}/activity")]
        public async Task<IActionResult> UpdateUserActivity(long vkUserId, [FromBody] UpdateUserActivityRequest request)
        {
            _logger.LogInformation("PATCH /api/v1/users/vk/{VkUserId}/activity", vkUserId);

            try
            {
                var success = await _userService.UpdateUserActivityAsync(vkUserId, request.IsOnline, request.LastActivity);

                if (!success)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с VK ID {vkUserId} не найден"));
                }

                return Ok(new ApiResponse(true, "Активность пользователя обновлена"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении активности пользователя VK ID {VkUserId}", vkUserId);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpPost("vk/{vkUserId:long}/message")]
        public async Task<IActionResult> IncrementMessageCount(long vkUserId)
        {
            _logger.LogInformation("POST /api/v1/users/vk/{VkUserId}/message - увеличение счетчика сообщений", vkUserId);

            try
            {
                var success = await _userService.IncrementMessageCountAsync(vkUserId);

                if (!success)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с VK ID {vkUserId} не найден"));
                }

                return Ok(new ApiResponse(true, "Счетчик сообщений увеличен"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при увеличении счетчика сообщений пользователя VK ID {VkUserId}", vkUserId);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            _logger.LogInformation("DELETE /api/v1/users/{Id}", id);

            try
            {
                var success = await _userService.DeleteUserAsync(id);

                if (!success)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                return Ok(new ApiResponse(true, "Пользователь удален"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении пользователя ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // ==================== МЕТОДЫ ДЛЯ ПОИСКА И СТАТИСТИКИ ====================

        [HttpGet("search")]
        public async Task<IActionResult> SearchUsers([FromQuery] string query)
        {
            _logger.LogInformation("GET /api/v1/users/search?query={Query}", query);

            try
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    return BadRequest(new ApiResponse(false, "Поисковый запрос не может быть пустым"));
                }

                var users = await _userService.SearchUsersAsync(query);

                return Ok(new ApiResponse(true, "Результаты поиска", users));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при поиске пользователей по запросу: {Query}", query);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetUserStats()
        {
            _logger.LogInformation("GET /api/v1/users/stats");

            try
            {
                var result = await _userService.GetUsersAsync(1, 1);

                var stats = new
                {
                    TotalUsers = result.TotalCount,
                    ActiveUsers = result.ActiveCount,
                    OnlineUsers = result.OnlineCount,
                    NewToday = result.NewTodayCount,
                    timestamp = DateTime.UtcNow
                };

                return Ok(new ApiResponse(true, "Статистика пользователей получена", stats));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении статистики пользователей");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // ==================== МЕТОДЫ ДЛЯ РАБОТЫ С ПЕРЕПИСКОЙ ====================

        [HttpGet("with-messages")]
        public async Task<IActionResult> GetUsersWithMessages(
            [FromQuery] int days = 30,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            _logger.LogInformation("GET /api/v1/users/with-messages?days={Days}&page={Page}&pageSize={PageSize}",
                days, page, pageSize);

            try
            {
                var allUsers = await _userService.GetUsersWithMessagesAsync(days);

                // Пагинация
                var totalCount = allUsers.Count;
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                var paginatedUsers = allUsers
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return Ok(new ApiResponse(true, "Пользователи с перепиской получены", new
                {
                    users = paginatedUsers,
                    pagination = new
                    {
                        page,
                        pageSize,
                        totalCount,
                        totalPages
                    },
                    stats = new
                    {
                        days,
                        usersWithMessages = totalCount,
                        timestamp = DateTime.UtcNow
                    }
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователей с перепиской");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpGet("{id:int}/messages")]
        public async Task<IActionResult> GetUserMessages(
            int id,
            [FromQuery] int limit = 50,
            [FromQuery] string? dateFrom = null,
            [FromQuery] string? dateTo = null)
        {
            _logger.LogInformation("GET /api/v1/users/{Id}/messages?limit={Limit}", id, limit);

            try
            {
                // Получаем пользователя
                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                // Получаем сообщения
                var messages = await _userService.GetUserMessagesAsync(user.VkUserId, limit);

                // Фильтрация по дате (если указаны параметры)
                if (!string.IsNullOrEmpty(dateFrom) && DateTime.TryParse(dateFrom, out var fromDate))
                {
                    messages = messages.Where(m => m.MessageDate >= fromDate).ToList();
                }

                if (!string.IsNullOrEmpty(dateTo) && DateTime.TryParse(dateTo, out var toDate))
                {
                    messages = messages.Where(m => m.MessageDate <= toDate).ToList();
                }

                return Ok(new ApiResponse(true, "Сообщения пользователя получены", new
                {
                    user = new
                    {
                        id = user.Id,
                        vkUserId = user.VkUserId,
                        name = $"{user.FirstName} {user.LastName}",
                        username = user.Username,
                        messageCount = user.MessageCount
                    },
                    messages = messages.OrderByDescending(m => m.MessageDate),
                    stats = new
                    {
                        totalMessages = messages.Count,
                        userMessages = messages.Count(m => m.IsFromUser),
                        botMessages = messages.Count(m => !m.IsFromUser),
                        firstMessageDate = messages.Any() ? messages.Min(m => m.MessageDate).ToString("yyyy-MM-dd HH:mm:ss") : null,
                        lastMessageDate = messages.Any() ? messages.Max(m => m.MessageDate).ToString("yyyy-MM-dd HH:mm:ss") : null
                    },
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении сообщений пользователя ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpPost("{id:int}/messages")]
        public async Task<IActionResult> AddMessage(
            int id,
            [FromBody] AddMessageRequest request)
        {
            _logger.LogInformation("POST /api/v1/users/{Id}/messages", id);

            try
            {
                // Получаем пользователя
                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                // Валидация
                if (string.IsNullOrWhiteSpace(request.MessageText))
                {
                    return BadRequest(new ApiResponse(false, "Текст сообщения не может быть пустым"));
                }

                // Добавляем сообщение
                await _userService.AddMessageAsync(
                    user.VkUserId,
                    request.MessageText,
                    request.IsFromUser ?? true);

                // Обновляем счетчик сообщений
                await _userService.IncrementMessageCountAsync(user.VkUserId);

                return Ok(new ApiResponse(true, "Сообщение добавлено", new
                {
                    userId = id,
                    vkUserId = user.VkUserId,
                    messageText = request.MessageText.Length > 100
                        ? request.MessageText.Substring(0, 100) + "..."
                        : request.MessageText,
                    isFromUser = request.IsFromUser,
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении сообщения пользователю ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpPost("{id:int}/send-message")]
        public async Task<IActionResult> SendMessageToUser(
            int id,
            [FromBody] SendMessageRequest request)
        {
            _logger.LogInformation("POST /api/v1/users/{Id}/send-message", id);

            try
            {
                // Получаем пользователя
                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                // Валидация
                if (string.IsNullOrWhiteSpace(request.Message))
                {
                    return BadRequest(new ApiResponse(false, "Текст сообщения не может быть пустым"));
                }

                // Отправляем сообщение через бота (имитация)
                var success = await _userService.SendMessageToUserAsync(user.VkUserId, request.Message);

                if (!success)
                {
                    return StatusCode(500, new ApiResponse(false, "Не удалось отправить сообщение"));
                }

                return Ok(new ApiResponse(true, "Сообщение отправлено пользователю", new
                {
                    userId = id,
                    vkUserId = user.VkUserId,
                    userName = $"{user.FirstName} {user.LastName}",
                    messagePreview = request.Message.Length > 50 ? request.Message.Substring(0, 50) + "..." : request.Message,
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отправке сообщения пользователю ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpGet("messages/stats")]
        public async Task<IActionResult> GetMessagesStats(
            [FromQuery] int days = 30,
            [FromQuery] bool includeDaily = true,
            [FromQuery] bool includeTopUsers = true)
        {
            _logger.LogInformation("GET /api/v1/users/messages/stats?days={Days}", days);

            try
            {
                var stats = await _userService.GetMessagesStatsAsync(days);

                return Ok(new ApiResponse(true, "Статистика сообщений получена", new
                {
                    period = new
                    {
                        days,
                        fromDate = DateTime.Now.AddDays(-days).ToString("yyyy-MM-dd"),
                        toDate = DateTime.Now.ToString("yyyy-MM-dd")
                    },
                    summary = new
                    {
                        totalMessages = stats.TotalMessages,
                        uniqueUsers = stats.UniqueUsers,
                        fromUserCount = stats.FromUserCount,
                        fromBotCount = stats.FromBotCount,
                        avgMessagesPerUser = stats.UniqueUsers > 0
                            ? Math.Round((double)stats.TotalMessages / stats.UniqueUsers, 2)
                            : 0
                    },
                    dailyStats = includeDaily ? stats.DailyStats.OrderByDescending(d => d.Date).Take(7) : null,
                    topUsers = includeTopUsers ? stats.TopUsers.Take(10) : null,
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении статистики сообщений");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpGet("recent-chats")]
        public async Task<IActionResult> GetRecentChats(
            [FromQuery] int limit = 20,
            [FromQuery] int hours = 24)
        {
            _logger.LogInformation("GET /api/v1/users/recent-chats?limit={Limit}&hours={Hours}", limit, hours);

            try
            {
                // Получаем всех пользователей с перепиской
                var allUsers = await _userService.GetUsersWithMessagesAsync(days: Math.Max(1, hours / 24));

                // Фильтруем по последней активности (последние N часов)
                var recentUsers = allUsers
                    .Where(u => u.HasRecentMessages && u.LastMessageDate >= DateTime.Now.AddHours(-hours))
                    .OrderByDescending(u => u.LastMessageDate)
                    .Take(limit)
                    .ToList();

                return Ok(new ApiResponse(true, "Недавние чаты получены", new
                {
                    chats = recentUsers,
                    stats = new
                    {
                        totalChats = recentUsers.Count,
                        hours,
                        lastUpdate = DateTime.UtcNow
                    }
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении недавних чатов");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpGet("today")]
        public async Task<IActionResult> GetUsersMessagedToday()
        {
            _logger.LogInformation("GET /api/v1/users/today - пользователи, которые писали сегодня");

            try
            {
                var users = await _userService.GetUsersMessagedTodayAsync();

                return Ok(new ApiResponse(true, "Пользователи, писавшие сегодня", new
                {
                    users,
                    count = users.Count,
                    date = DateTime.Today.ToString("yyyy-MM-dd"),
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователей, писавших сегодня");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpGet("{id:int}/summary")]
        public async Task<IActionResult> GetUserSummary(int id)
        {
            _logger.LogInformation("GET /api/v1/users/{Id}/summary - сводка по пользователю", id);

            try
            {
                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                var summary = await _userService.GetUserSummaryAsync(user.VkUserId);

                return Ok(new ApiResponse(true, "Сводка по пользователю получена", new
                {
                    userInfo = summary.UserInfo,
                    weeklyActivity = summary.WeeklyActivity,
                    averageResponseTime = summary.AverageResponseTimeSeconds > 0
                        ? $"{Math.Round(summary.AverageResponseTimeSeconds / 60, 1)} мин"
                        : "Нет данных",
                    messageStats = new
                    {
                        total = user.MessageCount,
                        lastActivity = user.LastActivity.ToString("yyyy-MM-dd HH:mm:ss"),
                        isOnline = user.IsOnline
                    },
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении сводки по пользователю ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpPost("{id:int}/test-messages")]
        public async Task<IActionResult> AddTestMessages(int id, [FromQuery] int count = 10)
        {
            _logger.LogInformation("POST /api/v1/users/{Id}/test-messages?count={Count}", id, count);

            try
            {
                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                var random = new Random();
                var testMessages = new[]
                {
                    "Привет! Как дела?",
                    "Какое сегодня расписание?",
                    "Сколько стоит билет?",
                    "Есть ли скидки для студентов?",
                    "Как доехать до аквапарка?",
                    "Какие горки сейчас работают?",
                    "Можно ли прийти с детьми?",
                    "Есть ли парковка?",
                    "Какие есть акции?",
                    "Спасибо за помощь!",
                    "Отличный сервис!",
                    "Хочу записаться на массаж",
                    "Есть ли ресторан?",
                    "Какое меню сегодня?",
                    "До скольки вы работаете?"
                };

                var botResponses = new[]
                {
                    "Здравствуйте! Чем могу помочь?",
                    "Рады вас видеть!",
                    "Спасибо за вопрос!",
                    "Будем рады помочь!",
                    "Это отличный выбор!",
                    "У нас для вас хорошие новости!",
                    "Ждем вас с нетерпением!",
                    "Обращайтесь в любое время!",
                    "Всегда к вашим услугам!",
                    "Приятного отдыха!"
                };

                var addedCount = 0;
                for (int i = 0; i < count; i++)
                {
                    var message = testMessages[random.Next(testMessages.Length)];
                    var isFromUser = random.Next(2) == 0; // 50% шанс что сообщение от пользователя

                    // Сообщение от пользователя
                    await _userService.AddMessageAsync(user.VkUserId, message, isFromUser);

                    // Ответ бота (если сообщение было от пользователя)
                    if (isFromUser && random.Next(2) == 0) // 50% шанс ответа
                    {
                        await _userService.AddMessageAsync(
                            user.VkUserId,
                            botResponses[random.Next(botResponses.Length)],
                            false); // false = сообщение от бота

                        addedCount++;
                    }

                    addedCount++;
                    await Task.Delay(10); // Небольшая задержка
                }

                // Обновляем счетчик сообщений
                await _userService.IncrementMessageCountAsync(user.VkUserId);

                return Ok(new ApiResponse(true, "Тестовые сообщения добавлены", new
                {
                    userId = id,
                    vkUserId = user.VkUserId,
                    messagesAdded = addedCount,
                    userMessages = count / 2,
                    botMessages = count / 2
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении тестовых сообщений");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpPost("import-messages")]
        public async Task<IActionResult> ImportMessages([FromBody] List<BotMessageImport> messages)
        {
            _logger.LogInformation("POST /api/v1/users/import-messages - импорт {Count} сообщений", messages.Count);

            try
            {
                if (messages == null || !messages.Any())
                {
                    return BadRequest(new ApiResponse(false, "Нет сообщений для импорта"));
                }

                await _userService.ImportBotMessagesAsync(messages);

                return Ok(new ApiResponse(true, "Сообщения импортированы", new
                {
                    importedCount = messages.Count,
                    uniqueUsers = messages.Select(m => m.VkUserId).Distinct().Count(),
                    dateRange = new
                    {
                        from = messages.Min(m => m.MessageDate).ToString("yyyy-MM-dd"),
                        to = messages.Max(m => m.MessageDate).ToString("yyyy-MM-dd")
                    },
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при импорте сообщений");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpPost("cleanup-messages")]
        public async Task<IActionResult> CleanupMessages([FromQuery] int days = 90)
        {
            _logger.LogInformation("POST /api/v1/users/cleanup-messages?days={Days}", days);

            try
            {
                if (days < 1)
                {
                    return BadRequest(new ApiResponse(false, "Количество дней должно быть больше 0"));
                }

                var deletedCount = await _userService.CleanupOldMessagesAsync(days);

                return Ok(new ApiResponse(true, "Очистка сообщений выполнена", new
                {
                    deletedCount,
                    days,
                    message = deletedCount > 0
                        ? $"Удалено {deletedCount} сообщений старше {days} дней"
                        : "Нет сообщений для удаления",
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при очистке сообщений");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // ==================== ТЕСТОВЫЕ МЕТОДЫ ====================

        [HttpPost("test")]
        public async Task<IActionResult> CreateTestUser()
        {
            _logger.LogInformation("POST /api/v1/users/test - создание тестового пользователя");

            try
            {
                var testUser = new User
                {
                    VkUserId = new Random().Next(1000000, 9999999),
                    FirstName = "Тестовый",
                    LastName = "Пользователь",
                    Username = "test_user_" + DateTime.Now.ToString("HHmmss"),
                    IsActive = true,
                    IsOnline = false,
                    LastActivity = DateTime.Now,
                    MessageCount = new Random().Next(1, 100),
                    RegistrationDate = DateTime.Now.AddDays(-new Random().Next(1, 30)),
                    IsBanned = false,
                    Status = "user",
                    Email = $"test{DateTime.Now:HHmmss}@example.com",
                    Phone = $"+7 999 {new Random().Next(100, 999)}-{new Random().Next(10, 99)}-{new Random().Next(10, 99)}",
                    Location = "Вологда"
                };

                var createdUser = await _userService.AddOrUpdateUserAsync(testUser);

                return Ok(new ApiResponse(true, "Тестовый пользователь создан", new
                {
                    user = createdUser,
                    message = $"Создан пользователь: {createdUser.FirstName} {createdUser.LastName}",
                    details = new
                    {
                        id = createdUser.Id,
                        vk_id = createdUser.VkUserId,
                        username = createdUser.Username,
                        email = createdUser.Email,
                        registration_date = createdUser.RegistrationDate.ToString("yyyy-MM-dd HH:mm:ss")
                    }
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании тестового пользователя");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpPost("bulk-test")]
        public async Task<IActionResult> CreateBulkTestUsers([FromQuery] int count = 5)
        {
            _logger.LogInformation("POST /api/v1/users/bulk-test?count={Count} - создание {Count} тестовых пользователей", count);

            try
            {
                if (count < 1 || count > 100)
                {
                    return BadRequest(new ApiResponse(false, "Количество должно быть от 1 до 100"));
                }

                var createdUsers = new List<User>();
                var random = new Random();

                for (int i = 0; i < count; i++)
                {
                    var testUser = new User
                    {
                        VkUserId = 1000000000 + random.Next(1000000, 9999999),
                        FirstName = GetRandomFirstName(),
                        LastName = GetRandomLastName(),
                        Username = $"user_{DateTime.Now:yyyyMMddHHmmss}_{i}",
                        IsActive = random.Next(0, 10) > 1, // 80% активных
                        IsOnline = random.Next(0, 10) > 7, // 30% онлайн
                        LastActivity = DateTime.Now.AddHours(-random.Next(0, 240)),
                        MessageCount = random.Next(0, 500),
                        RegistrationDate = DateTime.Now.AddDays(-random.Next(0, 365)),
                        IsBanned = random.Next(0, 20) == 0, // 5% забаненных
                        Status = GetRandomStatus(),
                        Email = $"user{i}_{DateTime.Now:HHmmss}@example.com",
                        Phone = $"+7 9{random.Next(10, 99)} {random.Next(100, 999)}-{random.Next(10, 99)}-{random.Next(10, 99)}",
                        Location = GetRandomCity()
                    };

                    var createdUser = await _userService.AddOrUpdateUserAsync(testUser);
                    createdUsers.Add(createdUser);
                }

                return Ok(new ApiResponse(true, $"Создано {createdUsers.Count} тестовых пользователей", new
                {
                    count = createdUsers.Count,
                    users = createdUsers.Select(u => new
                    {
                        id = u.Id,
                        vk_id = u.VkUserId,
                        name = $"{u.FirstName} {u.LastName}",
                        username = u.Username,
                        active = u.IsActive,
                        online = u.IsOnline,
                        banned = u.IsBanned,
                        messages = u.MessageCount
                    })
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании тестовых пользователей");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        [HttpGet("check-db")]
        public IActionResult CheckDatabase()
        {
            _logger.LogInformation("GET /api/v1/users/check-db - проверка базы данных");

            try
            {
                var dbPath = @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db";

                var fileExists = System.IO.File.Exists(dbPath);
                if (!fileExists)
                {
                    return Ok(new ApiResponse(false, "Файл базы данных не найден", new
                    {
                        path = dbPath,
                        exists = false,
                        suggestion = "Проверьте путь к базе данных"
                    }));
                }

                var fileInfo = new FileInfo(dbPath);
                var canRead = false;
                var canWrite = false;

                try
                {
                    using (var stream = fileInfo.Open(FileMode.Open, FileAccess.Read))
                        canRead = true;
                }
                catch { }

                try
                {
                    using (var stream = fileInfo.Open(FileMode.Open, FileAccess.Write))
                        canWrite = true;
                }
                catch { }

                return Ok(new ApiResponse(true, "Проверка базы данных", new
                {
                    path = dbPath,
                    exists = true,
                    size_kb = fileInfo.Length / 1024,
                    created = fileInfo.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    modified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    permissions = new
                    {
                        can_read = canRead,
                        can_write = canWrite
                    },
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке базы данных");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // ==================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ====================

        private async Task<BotApiUserListResponse?> GetUsersFromBotApi()
        {
            try
            {
                var botApiUrl = _configuration["BotApi:BaseUrl"] ?? "http://localhost:5000";
                _logger.LogInformation("Запрос к API бота: {Url}/api/adminapi/users?all=true", botApiUrl);

                var response = await _httpClient.GetAsync($"{botApiUrl}/api/adminapi/users?all=true");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("API бота вернул ошибку: {StatusCode}", response.StatusCode);
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Ответ API бота: {Content}", content);

                return JsonSerializer.Deserialize<BotApiUserListResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при запросе к API бота");
                return null;
            }
        }

        private List<BotApiUserResponse> FilterUsers(List<BotApiUserResponse> users, string search, string status)
        {
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

        private List<BotApiUserResponse> SortUsers(List<BotApiUserResponse> users, string sort)
        {
            return sort switch
            {
                "oldest" => users.OrderBy(u => u.RegisteredAt).ToList(),
                "name" => users.OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ToList(),
                "activity" => users.OrderByDescending(u => u.LastSeen).ToList(),
                _ => users.OrderByDescending(u => u.RegisteredAt).ToList() // newest
            };
        }

        private string GetRandomFirstName()
        {
            var names = new[] { "Александр", "Алексей", "Анна", "Артем", "Вадим", "Валерий", "Виктор",
                               "Галина", "Дарья", "Денис", "Евгений", "Екатерина", "Иван", "Ирина",
                               "Кирилл", "Мария", "Михаил", "Наталья", "Олег", "Ольга", "Павел",
                               "Сергей", "Татьяна", "Юрий" };
            return names[new Random().Next(names.Length)];
        }

        private string GetRandomLastName()
        {
            var names = new[] { "Иванов", "Петров", "Сидоров", "Смирнов", "Кузнецов", "Попов", "Васильев",
                               "Федоров", "Михайлов", "Новиков", "Фролов", "Волков", "Алексеев", "Лебедев",
                               "Семенов", "Егоров", "Павлов", "Козлов", "Степанов", "Николаев", "Орлов",
                               "Андреев", "Макаров", "Никитин" };
            return names[new Random().Next(names.Length)];
        }

        private string GetRandomStatus()
        {
            var statuses = new[] { "user", "vip", "admin", "moderator", "tester", "guest" };
            return statuses[new Random().Next(statuses.Length)];
        }

        private string GetRandomCity()
        {
            var cities = new[] { "Вологда", "Череповец", "Москва", "Санкт-Петербург", "Ярославль",
                                "Кострома", "Архангельск", "Великий Новгород", "Псков", "Тверь" };
            return cities[new Random().Next(cities.Length)];
        }
    }

    // ==================== МОДЕЛИ ЗАПРОСОВ ====================

    public class UpdateUserStatusRequest
    {
        public bool IsActive { get; set; } = true;
        public bool IsBanned { get; set; } = false;
    }

    public class UpdateUserActivityRequest
    {
        public bool IsOnline { get; set; }
        public DateTime? LastActivity { get; set; }
    }

    public class AddMessageRequest
    {
        public string MessageText { get; set; } = string.Empty;
        public bool? IsFromUser { get; set; } = true;
    }

    public class SendMessageRequest
    {
        public string Message { get; set; } = string.Empty;
    }
}