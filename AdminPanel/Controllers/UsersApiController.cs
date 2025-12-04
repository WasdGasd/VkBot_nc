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
        private readonly ILogger<UsersApiController> _logger;
        private readonly IConfiguration _configuration;

        public UsersApiController(
            UserService userService,
            ILogger<UsersApiController> logger,
            IConfiguration configuration)
        {
            _userService = userService;
            _logger = logger;
            _configuration = configuration;
        }

        // ==================== ДИАГНОСТИКА ====================

        // GET: api/v1/users/diagnostic
        [HttpGet("diagnostic")]
        public async Task<IActionResult> Diagnostic()
        {
            _logger.LogInformation("GET /api/v1/users/diagnostic");

            try
            {
                var dbPath = @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db";
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
                AdminPanel.Models.UserListResponse? usersResponse = null;
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

        // GET: api/v1/users
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
                var result = await _userService.GetUsersAsync(page, pageSize, search, status, sort);

                return Ok(new ApiResponse(true, "Пользователи получены", result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователей");
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // GET: api/v1/users/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetUser(int id)
        {
            _logger.LogInformation("GET /api/v1/users/{Id}", id);

            try
            {
                var user = await _userService.GetUserByIdAsync(id);

                if (user == null)
                {
                    return NotFound(new ApiResponse(false, $"Пользователь с ID {id} не найден"));
                }

                return Ok(new ApiResponse(true, "Пользователь найден", user));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении пользователя ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

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

        // POST: api/v1/users
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

        // PUT: api/v1/users/{id}
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

                var updatedUser = await _userService.AddOrUpdateUserAsync(existingUser);

                return Ok(new ApiResponse(true, "Пользователь обновлен", updatedUser));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении пользователя ID {Id}", id);
                return StatusCode(500, new ApiResponse(false, $"Ошибка: {ex.Message}"));
            }
        }

        // ==================== МЕТОДЫ ДЛЯ СТАТУСА И АКТИВНОСТИ ====================

        // PATCH: api/v1/users/{id}/status
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
                        var botResponses = new[]
                        {
                            "Спасибо за вопрос!",
                            "Рады помочь!",
                            "Обращайтесь еще!",
                            "Хорошего дня!",
                            "Отличный вопрос!",
                            "Благодарим за обращение!"
                        };

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
        public async Task<IActionResult> ImportMessages([FromBody] List<BotMessage> messages)
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
}