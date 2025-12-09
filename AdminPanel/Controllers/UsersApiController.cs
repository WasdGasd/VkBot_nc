using Microsoft.AspNetCore.Mvc;
using AdminPanel.Services;
using AdminPanel.Models;
using Microsoft.Data.Sqlite;

namespace AdminPanel.Controllers
{
    [ApiController]
    [Route("api/v1/users")]
    public class UsersApiController : ControllerBase
    {
        private readonly UserService _userService;
        private readonly VkApiService _vkApiService;
        private readonly ILogger<UsersApiController> _logger;
        private readonly IConfiguration _configuration;
        private readonly bool _enableRealVkIntegration;

        public UsersApiController(
            UserService userService,
            VkApiService vkApiService,
            ILogger<UsersApiController> logger,
            IConfiguration configuration)
        {
            _userService = userService;
            _vkApiService = vkApiService;
            _logger = logger;
            _configuration = configuration;
            _enableRealVkIntegration = configuration.GetValue<bool>("AdminSettings:EnableRealVkIntegration", false);
        }

        // ==================== НОВЫЕ МЕТОДЫ ДЛЯ VK ИНТЕГРАЦИИ ====================

        // POST: api/v1/users/sync-vk
        [HttpPost("sync-vk")]
        public async Task<IActionResult> SyncWithVk([FromQuery] bool fullSync = false)
        {
            _logger.LogInformation("POST /api/v1/users/sync-vk?fullSync={FullSync}", fullSync);

            try
            {
                if (!_vkApiService.IsEnabled)
                {
                    return BadRequest(new ApiResponse(false, "VK API отключен. Проверьте настройки."));
                }

                var syncedCount = await _userService.SyncWithVkAsync(fullSync);

                return Ok(new ApiResponse(true, "Синхронизация завершена", new
                {
                    syncedCount,
                    fullSync,
                    message = syncedCount > 0
                        ? $"Синхронизировано {syncedCount} пользователей"
                        : "Нет новых пользователей для синхронизации",
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка синхронизации с VK");
                return StatusCode(500, new ApiResponse(false, $"Ошибка синхронизации: {ex.Message}"));
            }
        }

        // POST: api/v1/users/{id}/ban
        [HttpPost("{id:int}/ban")]
        public async Task<IActionResult> BanUser(int id, [FromBody] BanUserRequest request)
        {
            _logger.LogInformation("POST /api/v1/users/{Id}/ban", id);

            try
            {
                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                if (string.IsNullOrWhiteSpace(request.Reason))
                {
                    return BadRequest(new ApiResponse(false, "Причина бана обязательна"));
                }

                var success = await _userService.BanUserAsync(user.VkUserId, request.Reason);

                return Ok(new ApiResponse(success, success ? "Пользователь забанен" : "Не удалось забанить пользователя", new
                {
                    userId = id,
                    vkUserId = user.VkUserId,
                    reason = request.Reason,
                    bannedAt = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка бана пользователя ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // POST: api/v1/users/{id}/unban
        [HttpPost("{id:int}/unban")]
        public async Task<IActionResult> UnbanUser(int id)
        {
            _logger.LogInformation("POST /api/v1/users/{Id}/unban", id);

            try
            {
                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                var success = await _userService.UnbanUserAsync(user.VkUserId);

                return Ok(new ApiResponse(success, success ? "Пользователь разбанен" : "Не удалось разбанить пользователя", new
                {
                    userId = id,
                    vkUserId = user.VkUserId,
                    unbannedAt = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка разбана пользователя ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // POST: api/v1/users/{id}/send-real-message
        [HttpPost("{id:int}/send-real-message")]
        public async Task<IActionResult> SendRealMessage(int id, [FromBody] SendMessageRequest request)
        {
            _logger.LogInformation("POST /api/v1/users/{Id}/send-real-message", id);

            try
            {
                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                if (string.IsNullOrWhiteSpace(request.Message))
                {
                    return BadRequest(new ApiResponse(false, "Сообщение не может быть пустым"));
                }

                if (request.Message.Length > 4000)
                {
                    return BadRequest(new ApiResponse(false, "Сообщение не должно превышать 4000 символов"));
                }

                var success = await _userService.SendMessageToVkUserAsync(user.VkUserId, request.Message);

                return Ok(new ApiResponse(success, success ? "Сообщение отправлено" : "Не удалось отправить сообщение", new
                {
                    userId = id,
                    vkUserId = user.VkUserId,
                    messagePreview = request.Message.Length > 100
                        ? request.Message.Substring(0, 100) + "..."
                        : request.Message,
                    sentAt = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка отправки сообщения пользователю ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // POST: api/v1/users/{id}/refresh-vk
        [HttpPost("{id:int}/refresh-vk")]
        public async Task<IActionResult> RefreshFromVk(int id)
        {
            _logger.LogInformation("POST /api/v1/users/{Id}/refresh-vk", id);

            try
            {
                if (!_vkApiService.IsEnabled)
                {
                    return BadRequest(new ApiResponse(false, "VK API отключен"));
                }

                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                await _userService.UpdateUserFromVkAsync(user.VkUserId);

                // Получаем обновленные данные
                var updatedUser = await _userService.GetUserByIdAsync(id);

                return Ok(new ApiResponse(true, "Данные обновлены из VK", updatedUser));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обновления пользователя из VK ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // GET: api/v1/users/vk/status
        [HttpGet("vk/status")]
        public IActionResult GetVkApiStatus()
        {
            _logger.LogInformation("GET /api/v1/users/vk/status");

            try
            {
                var status = new
                {
                    isEnabled = _vkApiService.IsEnabled,
                    isIntegrationEnabled = _enableRealVkIntegration,
                    hasAccessToken = !string.IsNullOrEmpty(_configuration["VkApi:AccessToken"]),
                    hasGroupId = !string.IsNullOrEmpty(_configuration["VkApi:GroupId"]),
                    configuration = new
                    {
                        accessTokenLength = _configuration["VkApi:AccessToken"]?.Length ?? 0,
                        apiVersion = _configuration["VkApi:ApiVersion"],
                        groupId = _configuration["VkApi:GroupId"],
                        adminIds = _configuration.GetSection("VkApi:AdminIds").Get<long[]>()?.Length ?? 0
                    }
                };

                return Ok(new ApiResponse(true, "Статус VK API", status));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения статуса VK API");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // GET: api/v1/users/online
        [HttpGet("online")]
        public async Task<IActionResult> GetOnlineUsers()
        {
            _logger.LogInformation("GET /api/v1/users/online");

            try
            {
                var users = await _userService.GetOnlineUsersAsync();

                return Ok(new ApiResponse(true, "Онлайн пользователи", new
                {
                    users,
                    count = users.Count,
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения онлайн пользователей");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // GET: api/v1/users/banned
        [HttpGet("banned")]
        public async Task<IActionResult> GetBannedUsers()
        {
            _logger.LogInformation("GET /api/v1/users/banned");

            try
            {
                var users = await _userService.GetBannedUsersAsync();

                return Ok(new ApiResponse(true, "Забаненные пользователи", new
                {
                    users,
                    count = users.Count,
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения забаненных пользователей");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // GET: api/v1/users/active
        [HttpGet("active")]
        public async Task<IActionResult> GetActiveUsers([FromQuery] int days = 7)
        {
            _logger.LogInformation("GET /api/v1/users/active?days={Days}", days);

            try
            {
                var users = await _userService.GetActiveUsersAsync(days);

                return Ok(new ApiResponse(true, "Активные пользователи", new
                {
                    users,
                    count = users.Count,
                    days,
                    period = $"{DateTime.Now.AddDays(-days):yyyy-MM-dd} - {DateTime.Now:yyyy-MM-dd}",
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения активных пользователей");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // GET: api/v1/users/advanced-stats
        [HttpGet("advanced-stats")]
        public async Task<IActionResult> GetAdvancedStats()
        {
            _logger.LogInformation("GET /api/v1/users/advanced-stats");

            try
            {
                var stats = await _userService.GetAdvancedStatsAsync();

                return Ok(new ApiResponse(true, "Расширенная статистика", stats));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения расширенной статистики");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // POST: api/v1/users/{id}/toggle-status
        [HttpPost("{id:int}/toggle-status")]
        public async Task<IActionResult> ToggleUserStatus(int id)
        {
            _logger.LogInformation("POST /api/v1/users/{Id}/toggle-status", id);

            try
            {
                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                user.IsActive = !user.IsActive;
                var updatedUser = await _userService.AddOrUpdateUserAsync(user);

                return Ok(new ApiResponse(true, "Статус пользователя изменен", new
                {
                    userId = id,
                    vkUserId = user.VkUserId,
                    isActive = user.IsActive,
                    action = user.IsActive ? "активирован" : "деактивирован",
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка изменения статуса пользователя ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // POST: api/v1/users/{id}/update-status
        [HttpPost("{id:int}/update-status")]
        public async Task<IActionResult> UpdateUserStatus(int id, [FromBody] UpdateUserStatusRequest request)
        {
            _logger.LogInformation("POST /api/v1/users/{Id}/update-status", id);

            try
            {
                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                user.Status = request.Status;
                var updatedUser = await _userService.AddOrUpdateUserAsync(user);

                return Ok(new ApiResponse(true, "Статус пользователя обновлен", new
                {
                    userId = id,
                    vkUserId = user.VkUserId,
                    oldStatus = user.Status,
                    newStatus = request.Status,
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обновления статуса пользователя ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // POST: api/v1/users/{id}/notes
        [HttpPost("{id:int}/notes")]
        public async Task<IActionResult> UpdateUserNotes(int id, [FromBody] UpdateUserNotesRequest request)
        {
            _logger.LogInformation("POST /api/v1/users/{Id}/notes", id);

            try
            {
                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                user.Notes = request.Notes;
                var updatedUser = await _userService.AddOrUpdateUserAsync(user);

                return Ok(new ApiResponse(true, "Заметки пользователя обновлены", new
                {
                    userId = id,
                    vkUserId = user.VkUserId,
                    notesLength = request.Notes?.Length ?? 0,
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обновления заметок пользователя ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // POST: api/v1/users/{id}/update-info
        [HttpPost("{id:int}/update-info")]
        public async Task<IActionResult> UpdateUserInfo(int id, [FromBody] UpdateUserInfoRequest request)
        {
            _logger.LogInformation("POST /api/v1/users/{Id}/update-info", id);

            try
            {
                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                // Обновляем только предоставленные поля
                if (!string.IsNullOrWhiteSpace(request.Email))
                    user.Email = request.Email;

                if (!string.IsNullOrWhiteSpace(request.Phone))
                    user.Phone = request.Phone;

                if (!string.IsNullOrWhiteSpace(request.Location))
                    user.Location = request.Location;

                var updatedUser = await _userService.AddOrUpdateUserAsync(user);

                return Ok(new ApiResponse(true, "Информация о пользователе обновлена", updatedUser));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обновления информации о пользователе ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // ==================== ОБНОВЛЕННЫЕ СУЩЕСТВУЮЩИЕ МЕТОДЫ ====================

        // GET: api/v1/users
        [HttpGet]
        public async Task<IActionResult> GetUsers(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string search = "",
            [FromQuery] string status = "all",
            [FromQuery] string sort = "newest",
            [FromQuery] bool includeVkInfo = false)
        {
            _logger.LogInformation("GET /api/v1/users?page={Page}&pageSize={PageSize}&search={Search}&status={Status}&sort={Sort}&includeVkInfo={IncludeVkInfo}",
                page, pageSize, search ?? "", status, sort, includeVkInfo);

            try
            {
                var result = await _userService.GetUsersAsync(page, pageSize, search, status, sort, includeVkInfo);

                // Добавляем информацию о VK интеграции
                var responseData = new
                {
                    users = result.Users,
                    pagination = new
                    {
                        page = result.Page,
                        pageSize = result.PageSize,
                        totalCount = result.TotalCount,
                        totalPages = result.TotalPages
                    },
                    stats = new
                    {
                        totalUsers = result.TotalCount,
                        activeUsers = result.ActiveCount,
                        onlineUsers = result.OnlineCount,
                        newToday = result.NewTodayCount
                    },
                    vkIntegration = new
                    {
                        enabled = _vkApiService.IsEnabled && _enableRealVkIntegration,
                        includeVkInfo,
                        lastSync = DateTime.UtcNow
                    },
                    timestamp = DateTime.UtcNow
                };

                return Ok(new ApiResponse(true, "Пользователи получены", responseData));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователей");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // GET: api/v1/users/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetUser(
            int id,
            [FromQuery] bool includeMessages = true,
            [FromQuery] bool includeVkInfo = true)
        {
            _logger.LogInformation("GET /api/v1/users/{Id}?includeMessages={IncludeMessages}&includeVkInfo={IncludeVkInfo}",
                id, includeMessages, includeVkInfo);

            try
            {
                var user = await _userService.GetUserByIdAsync(id);

                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                // Обновляем информацию из VK если нужно
                if (includeVkInfo && _vkApiService.IsEnabled && _enableRealVkIntegration)
                {
                    await _userService.UpdateUserFromVkAsync(user.VkUserId);
                    user = await _userService.GetUserByIdAsync(id); // Получаем обновленные данные
                }

                var responseData = new
                {
                    user,
                    recentMessages = includeMessages
                        ? await _userService.GetUserMessagesAsync(user.VkUserId, 20)
                        : null,
                    vkInfo = includeVkInfo && _vkApiService.IsEnabled
                        ? await _vkApiService.GetUserInfoAsync(user.VkUserId)
                        : null,
                    statistics = new
                    {
                        totalMessages = user.MessageCount,
                        isOnline = user.IsOnline,
                        lastActivity = user.LastActivity,
                        daysSinceRegistration = (DateTime.Now - user.RegistrationDate).Days
                    },
                    vkIntegration = new
                    {
                        enabled = _vkApiService.IsEnabled && _enableRealVkIntegration,
                        canSendMessage = _vkApiService.IsEnabled && user.IsActive && !user.IsBanned,
                        canBan = _vkApiService.IsEnabled,
                        lastUpdated = DateTime.UtcNow
                    }
                };

                return Ok(new ApiResponse(true, "Пользователь найден", responseData));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователя ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // POST: api/v1/users
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
        {
            _logger.LogInformation("POST /api/v1/users - создание пользователя VK ID: {VkUserId}", request.VkUserId);

            try
            {
                // Валидация
                if (request.VkUserId <= 0)
                {
                    return BadRequest(new ApiResponse(false, "VK ID обязателен и должен быть больше 0"));
                }

                if (string.IsNullOrWhiteSpace(request.FirstName))
                {
                    return BadRequest(new ApiResponse(false, "Имя пользователя обязательно"));
                }

                // Проверяем, существует ли пользователь в VK
                if (_vkApiService.IsEnabled)
                {
                    var vkUser = await _vkApiService.GetUserInfoAsync(request.VkUserId);
                    if (vkUser == null)
                    {
                        return BadRequest(new ApiResponse(false, "Пользователь не найден в VK"));
                    }

                    // Используем данные из VK если они не указаны
                    request.FirstName = vkUser.FirstName;
                    request.LastName = vkUser.LastName;
                    request.Username = vkUser.Domain ?? request.Username;
                }

                var user = new User
                {
                    VkUserId = request.VkUserId,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Username = request.Username ?? string.Empty,
                    IsActive = request.IsActive ?? true,
                    IsOnline = false,
                    LastActivity = DateTime.Now,
                    MessageCount = 0,
                    RegistrationDate = DateTime.Now,
                    IsBanned = false,
                    Status = request.Status ?? "user",
                    Email = request.Email ?? string.Empty,
                    Phone = request.Phone ?? string.Empty,
                    Location = request.Location ?? string.Empty
                };

                var createdUser = await _userService.AddOrUpdateUserAsync(user);

                return Ok(new ApiResponse(true, "Пользователь создан/обновлен", new
                {
                    user = createdUser,
                    vkIntegration = new
                    {
                        enabled = _vkApiService.IsEnabled,
                        verified = _vkApiService.IsEnabled,
                        timestamp = DateTime.UtcNow
                    }
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании пользователя");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // PUT: api/v1/users/{id}
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest request)
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
                if (!string.IsNullOrWhiteSpace(request.FirstName))
                    existingUser.FirstName = request.FirstName;

                if (!string.IsNullOrWhiteSpace(request.LastName))
                    existingUser.LastName = request.LastName;

                if (!string.IsNullOrWhiteSpace(request.Username))
                    existingUser.Username = request.Username;

                if (!string.IsNullOrWhiteSpace(request.Email))
                    existingUser.Email = request.Email;

                if (!string.IsNullOrWhiteSpace(request.Phone))
                    existingUser.Phone = request.Phone;

                if (!string.IsNullOrWhiteSpace(request.Location))
                    existingUser.Location = request.Location;

                if (!string.IsNullOrWhiteSpace(request.Status))
                    existingUser.Status = request.Status;

                if (request.IsActive.HasValue)
                    existingUser.IsActive = request.IsActive.Value;

                if (request.IsBanned.HasValue)
                    existingUser.IsBanned = request.IsBanned.Value;

                if (request.IsOnline.HasValue)
                    existingUser.IsOnline = request.IsOnline.Value;

                var updatedUser = await _userService.AddOrUpdateUserAsync(existingUser);

                return Ok(new ApiResponse(true, "Пользователь обновлен", new
                {
                    user = updatedUser,
                    changes = request,
                    timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении пользователя ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // ==================== ДИАГНОСТИКА ====================

        // GET: api/v1/users/diagnostic
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

        // ==================== ОСНОВНЫЕ МЕТОДЫ ДЛЯ ПОЛЬЗОВАТЕЛЕЙ (остальные без изменений) ====================

        // GET: api/v1/users/vk/{vkUserId}
        [HttpGet("vk/{vkUserId:long}")]
        public async Task<IActionResult> GetUserByVkId(long vkUserId)
        {
            _logger.LogInformation("GET /api/v1/users/vk/{VkUserId}", vkUserId);

            try
            {
                var user = await _userService.GetUserAsync(vkUserId);

                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с VK ID {vkUserId} не найден"));
                }

                return Ok(new ApiResponse(true, "Пользователь найден", user));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователя VK ID {VkUserId}", vkUserId);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // ==================== МЕТОДЫ ДЛЯ СТАТУСА И АКТИВНОСТИ ====================

        // PATCH: api/v1/users/{id}/status
        [HttpPatch("{id:int}/status")]
        public async Task<IActionResult> UpdateUserStatusOld(int id, [FromBody] UpdateUserStatusRequestOld request)
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

        // PATCH: api/v1/users/vk/{vkUserId}/activity
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

        // POST: api/v1/users/vk/{vkUserId}/message
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

        // DELETE: api/v1/users/{id}
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

        // GET: api/v1/users/search
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

        // GET: api/v1/users/stats
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

        // GET: api/v1/users/with-messages
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

        // GET: api/v1/users/{id}/messages
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

        // POST: api/v1/users/{id}/messages
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

        // POST: api/v1/users/{id}/send-message
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

        // GET: api/v1/users/messages/stats
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

        // GET: api/v1/users/recent-chats
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

        // GET: api/v1/users/today
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

        // GET: api/v1/users/{id}/summary
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

        // POST: api/v1/users/{id}/test-messages
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

        // POST: api/v1/users/import-messages
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

        // POST: api/v1/users/cleanup-messages
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

        // POST: api/v1/users/test
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

        // POST: api/v1/users/bulk-test
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

        // GET: api/v1/users/check-db
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

    public class CreateUserRequest
    {
        public long VkUserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Location { get; set; }
        public string? Status { get; set; }
        public bool? IsActive { get; set; }
    }

    public class UpdateUserRequest
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Location { get; set; }
        public string? Status { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsBanned { get; set; }
        public bool? IsOnline { get; set; }
    }

    public class BanUserRequest
    {
        public string Reason { get; set; } = string.Empty;
        public int? DurationHours { get; set; }
        public string? Comment { get; set; }
    }

    public class UpdateUserStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }

    public class UpdateUserNotesRequest
    {
        public string? Notes { get; set; }
    }

    public class UpdateUserInfoRequest
    {
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Location { get; set; }
    }

    // Существующие модели остаются...
    public class UpdateUserStatusRequestOld
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