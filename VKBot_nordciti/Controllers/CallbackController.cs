using Microsoft.AspNetCore.Mvc;
using VKBot_nordciti.Services;
using VKBot_nordciti.VK.Models;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using System.IO;

namespace VKBot_nordciti.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CallbackController : ControllerBase
    {
        private readonly IMessageService _messageService;
        private readonly IBotStatsService _botStatsService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CallbackController> _logger;
        private readonly IVkUserService _vkUserService;
        private static readonly HashSet<long> _processedUsersCache = new();

        public CallbackController(
            IMessageService messageService,
            IBotStatsService botStatsService,
            IConfiguration configuration,
            ILogger<CallbackController> logger,
            IVkUserService vkUserService)
        {
            _messageService = messageService;
            _botStatsService = botStatsService;
            _configuration = configuration;
            _logger = logger;
            _vkUserService = vkUserService;
        }

        [HttpPost]
        public async Task<IActionResult> Callback([FromBody] JsonElement request)
        {
            try
            {
                _logger.LogInformation($"=== RAW CALLBACK DATA ===");
                _logger.LogInformation($"{request}");

                if (request.TryGetProperty("type", out var typeProperty))
                {
                    var type = typeProperty.GetString();
                    _logger.LogInformation($"Callback type: {type}");

                    if (type == "confirmation")
                    {
                        var confirmCode = _configuration["VkSettings:ConfirmationToken"];
                        _logger.LogInformation($"Confirmation request, returning: {confirmCode}");
                        return Ok(confirmCode);
                    }

                    // Обработка события разрешения сообщений
                    if (type == "message_allow")
                    {
                        _logger.LogInformation("Processing message_allow event");

                        if (request.TryGetProperty("object", out var objectElement))
                        {
                            if (objectElement.TryGetProperty("user_id", out var userIdProp))
                            {
                                var userId = userIdProp.GetInt64();
                                _logger.LogInformation($"User {userId} allowed messages");

                                // Проверяем и сохраняем пользователя (с проверкой на дубли)
                                await CheckAndSaveUserToDatabase(userId, forceSave: true);

                                // Регистрируем в статистике
                                _botStatsService.RegisterCommandUsage(userId, "message_allow");
                                _botStatsService.UpdateUserActivity(userId, isOnline: true);

                                // Отправляем приветственное сообщение
                                await _messageService.HandleMessageAllowEvent(userId);
                            }
                        }
                        return Ok("ok");
                    }

                    if (type == "message_new")
                    {
                        var message = ParseMessage(request);
                        if (message != null)
                        {
                            var userId = message.FromId;

                            // Проверяем и сохраняем пользователя (с проверкой на дубли)
                            await CheckAndSaveUserToDatabase(userId, forceSave: false);

                            // Регистрируем в статистике
                            _botStatsService.RegisterCommandUsage(userId, message.Text);
                            _botStatsService.UpdateUserActivity(userId, isOnline: true);

                            await _messageService.ProcessMessageAsync(message);
                        }
                        return Ok("ok");
                    }

                    if (type == "message_event")
                    {
                        _logger.LogInformation("Processing message_event (button click)");

                        if (request.TryGetProperty("object", out var objectElement))
                        {
                            await ProcessMessageEvent(objectElement);
                        }
                        return Ok("ok");
                    }
                }

                _logger.LogWarning($"Unknown callback structure");
                return Ok("ok");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing callback");
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        // 🔥 ОСНОВНОЙ МЕТОД: Проверяет и сохраняет пользователя с проверкой на дублирование
        private async Task CheckAndSaveUserToDatabase(long vkUserId, bool forceSave = false)
        {
            try
            {
                // 1. Быстрая проверка в кэше памяти (оптимизация)
                if (!forceSave && _processedUsersCache.Contains(vkUserId))
                {
                    _logger.LogDebug($"🚫 Пользователь {vkUserId} уже в кэше памяти, пропускаем");
                    return;
                }

                // 2. Проверяем в БД
                var botDbPath = @"C:\Users\pog\Desktop\VkBot_nc\VKBot_nordciti\vkbot.db";

                // ИСПРАВЛЕНО: Используем System.IO.File вместо File
                if (!System.IO.File.Exists(botDbPath))
                {
                    _logger.LogWarning($"❌ Файл БД не найден: {botDbPath}");
                    return;
                }

                using var connection = new SqliteConnection($"Data Source={botDbPath};");
                await connection.OpenAsync();

                // Проверяем существование пользователя в БД
                var checkCommand = new SqliteCommand(
                    "SELECT COUNT(*) FROM Users WHERE VkUserId = @vkUserId",
                    connection);
                checkCommand.Parameters.AddWithValue("@vkUserId", vkUserId);

                var existsInDb = Convert.ToInt32(await checkCommand.ExecuteScalarAsync()) > 0;

                if (existsInDb && !forceSave)
                {
                    _logger.LogDebug($"🚫 Пользователь {vkUserId} уже существует в БД, пропускаем создание");

                    // Просто обновляем активность
                    var updateActivityCommand = new SqliteCommand(
                        "UPDATE Users SET LastActivity = datetime('now'), IsOnline = 1 WHERE VkUserId = @vkUserId",
                        connection);
                    updateActivityCommand.Parameters.AddWithValue("@vkUserId", vkUserId);
                    await updateActivityCommand.ExecuteNonQueryAsync();

                    _processedUsersCache.Add(vkUserId); // Добавляем в кэш
                    return;
                }

                // 3. Получаем информацию о пользователе из VK
                VkUserInfo userInfo = null;
                try
                {
                    userInfo = await _vkUserService.GetUserInfoAsync(vkUserId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"⚠️ Не удалось получить информацию о пользователе {vkUserId}: {ex.Message}");
                }

                // 4. Создаем или обновляем запись
                if (existsInDb)
                {
                    // Обновляем существующего
                    if (userInfo != null)
                    {
                        var updateCommand = new SqliteCommand(@"
                            UPDATE Users SET 
                                FirstName = @firstName,
                                LastName = @lastName,
                                Username = @username,
                                LastActivity = datetime('now'),
                                IsOnline = 1
                            WHERE VkUserId = @vkUserId",
                            connection);

                        updateCommand.Parameters.AddWithValue("@firstName", userInfo.FirstName ?? "");
                        updateCommand.Parameters.AddWithValue("@lastName", userInfo.LastName ?? "");
                        updateCommand.Parameters.AddWithValue("@username", userInfo.Username ?? "");
                        updateCommand.Parameters.AddWithValue("@vkUserId", vkUserId);

                        await updateCommand.ExecuteNonQueryAsync();
                        _logger.LogInformation($"✅ Обновлен пользователь {vkUserId} ({userInfo.FirstName} {userInfo.LastName})");
                    }
                    else
                    {
                        var updateActivityCommand = new SqliteCommand(
                            "UPDATE Users SET LastActivity = datetime('now'), IsOnline = 1 WHERE VkUserId = @vkUserId",
                            connection);
                        updateActivityCommand.Parameters.AddWithValue("@vkUserId", vkUserId);
                        await updateActivityCommand.ExecuteNonQueryAsync();
                        _logger.LogInformation($"✅ Обновлена активность пользователя {vkUserId}");
                    }
                }
                else
                {
                    // Создаем нового пользователя
                    var insertCommand = new SqliteCommand(@"
                        INSERT INTO Users (
                            VkUserId, 
                            FirstName, 
                            LastName, 
                            Username, 
                            IsActive, 
                            IsOnline, 
                            LastActivity, 
                            MessageCount, 
                            RegistrationDate
                        ) VALUES (
                            @vkUserId,
                            @firstName,
                            @lastName,
                            @username,
                            1,  -- IsActive
                            1,  -- IsOnline
                            datetime('now'),  -- LastActivity
                            0,  -- MessageCount
                            datetime('now')   -- RegistrationDate
                        )",
                        connection);

                    insertCommand.Parameters.AddWithValue("@vkUserId", vkUserId);

                    if (userInfo != null)
                    {
                        insertCommand.Parameters.AddWithValue("@firstName", userInfo.FirstName ?? "");
                        insertCommand.Parameters.AddWithValue("@lastName", userInfo.LastName ?? "");
                        insertCommand.Parameters.AddWithValue("@username", userInfo.Username ?? "");
                        _logger.LogInformation($"✅ СОЗДАН новый пользователь {vkUserId} ({userInfo.FirstName} {userInfo.LastName})");
                    }
                    else
                    {
                        insertCommand.Parameters.AddWithValue("@firstName", "Пользователь");
                        insertCommand.Parameters.AddWithValue("@lastName", vkUserId.ToString());
                        insertCommand.Parameters.AddWithValue("@username", "");
                        _logger.LogInformation($"✅ СОЗДАН новый пользователь {vkUserId} (без данных VK)");
                    }

                    await insertCommand.ExecuteNonQueryAsync();
                }

                // Добавляем в кэш памяти
                _processedUsersCache.Add(vkUserId);

                // Лимитируем размер кэша
                if (_processedUsersCache.Count > 10000)
                {
                    _processedUsersCache.Clear();
                    _logger.LogInformation("🧹 Очищен кэш обработанных пользователей");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка при работе с пользователем {vkUserId} в БД");
            }
        }

        private async Task ProcessMessageEvent(JsonElement eventElement)
        {
            try
            {
                long userId = 0;
                string eventId = string.Empty;
                string payload = string.Empty;

                if (eventElement.TryGetProperty("user_id", out var userIdProp))
                {
                    userId = userIdProp.GetInt64();
                }

                if (eventElement.TryGetProperty("event_id", out var eventIdProp))
                {
                    eventId = eventIdProp.GetString() ?? "unknown";
                }

                if (eventElement.TryGetProperty("payload", out var payloadProp))
                {
                    payload = payloadProp.ToString();
                }

                _logger.LogInformation($"Button click - UserId: {userId}, EventId: {eventId}, Payload: {payload}");

                // Проверяем и сохраняем пользователя (с проверкой на дубли)
                await CheckAndSaveUserToDatabase(userId, forceSave: false);

                // Регистрируем в статистике
                string command = ExtractCommandFromPayload(payload, eventId);
                _botStatsService.RegisterCommandUsage(userId, command);
                _botStatsService.UpdateUserActivity(userId, isOnline: true);

                // Обрабатываем нажатие кнопки
                await _messageService.ProcessButtonClickAsync(userId, eventId, payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message_event");
            }
        }

        private string ExtractCommandFromPayload(string payload, string fallbackCommand)
        {
            try
            {
                if (!string.IsNullOrEmpty(payload) && payload.StartsWith("{") && payload.EndsWith("}"))
                {
                    var json = JsonDocument.Parse(payload);
                    if (json.RootElement.TryGetProperty("command", out var commandProp))
                        return commandProp.GetString() ?? fallbackCommand;
                    if (json.RootElement.TryGetProperty("action", out var actionProp))
                        return actionProp.GetString() ?? fallbackCommand;
                    if (json.RootElement.TryGetProperty("type", out var typeProp))
                        return typeProp.GetString() ?? fallbackCommand;
                }
                return fallbackCommand;
            }
            catch
            {
                return fallbackCommand;
            }
        }

        private VkMessage? ParseMessage(JsonElement request)
        {
            try
            {
                var message = new VkMessage();

                if (request.TryGetProperty("object", out var objectElement))
                {
                    if (objectElement.TryGetProperty("message", out var messageElement))
                    {
                        return ParseMessageFromElement(messageElement);
                    }
                    else
                    {
                        return ParseMessageFromElement(objectElement);
                    }
                }

                _logger.LogWarning($"Could not find message data in callback");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing message");
                return null;
            }
        }

        private VkMessage ParseMessageFromElement(JsonElement element)
        {
            var message = new VkMessage();

            if (element.TryGetProperty("peer_id", out var peerIdProp))
                message.PeerId = peerIdProp.GetInt64();

            if (element.TryGetProperty("from_id", out var fromIdProp))
            {
                message.FromId = fromIdProp.GetInt64();
                if (message.PeerId == 0) message.PeerId = message.FromId;
            }

            if (element.TryGetProperty("user_id", out var userIdProp))
            {
                message.UserId = userIdProp.GetInt64();
                if (message.PeerId == 0) message.PeerId = message.UserId;
            }

            if (element.TryGetProperty("text", out var textProp))
                message.Text = textProp.GetString();

            _logger.LogInformation($"Parsed message - FromId: {message.FromId}, UserId: {message.UserId}, PeerId: {message.PeerId}, Text: {message.Text}");

            return message;
        }
    }
}