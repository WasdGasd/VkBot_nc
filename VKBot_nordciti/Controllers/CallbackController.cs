using Microsoft.AspNetCore.Mvc;
using VKBot_nordciti.Services;
using VKBot_nordciti.VK.Models;
using System.Text.Json;

namespace VKBot_nordciti.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CallbackController : ControllerBase
    {
        private readonly IMessageService _messageService;
        private readonly IBotStatsService _botStatsService; // ← ДОБАВИЛ
        private readonly IConfiguration _configuration;
        private readonly ILogger<CallbackController> _logger;

        // Изменил конструктор - добавил IBotStatsService
        public CallbackController(
            IMessageService messageService,
            IBotStatsService botStatsService, // ← ДОБАВИЛ
            IConfiguration configuration,
            ILogger<CallbackController> logger)
        {
            _messageService = messageService;
            _botStatsService = botStatsService; // ← ДОБАВИЛ
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Callback([FromBody] JsonElement request)
        {
            try
            {
                _logger.LogInformation($"=== RAW CALLBACK DATA ===");
                _logger.LogInformation($"{request}");

                // Пробуем распарсить как VkUpdate
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

                                // Регистрируем в статистике
                                _botStatsService.RegisterCommandUsage(userId, "message_allow");
                                _botStatsService.UpdateUserActivity(userId, isOnline: true);

                                // Вызываем метод для отправки приветственного сообщения
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
                            // РЕГИСТРАЦИЯ 1
                            _botStatsService.RegisterCommandUsage(message.FromId, message.Text);
                            _botStatsService.UpdateUserActivity(message.FromId, isOnline: true);

                            await _messageService.ProcessMessageAsync(message);
                        }
                        return Ok("ok");
                    }

                    // ВАЖНО: ДОБАВИЛ обработку нажатий кнопок
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

        // НОВЫЙ МЕТОД: Обработка нажатий кнопок
        private async Task ProcessMessageEvent(JsonElement eventElement)
        {
            try
            {
                // Получаем данные из event
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

                // Регистрируем в статистике как команду
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

        // Вспомогательный метод для извлечения команды из payload
        private string ExtractCommandFromPayload(string payload, string fallbackCommand)
        {
            try
            {
                if (!string.IsNullOrEmpty(payload) &&
                    payload.StartsWith("{") && payload.EndsWith("}"))
                {
                    var json = JsonDocument.Parse(payload);
                    if (json.RootElement.TryGetProperty("command", out var commandProp))
                    {
                        return commandProp.GetString() ?? fallbackCommand;
                    }
                    if (json.RootElement.TryGetProperty("action", out var actionProp))
                    {
                        return actionProp.GetString() ?? fallbackCommand;
                    }
                    if (json.RootElement.TryGetProperty("type", out var typeProp))
                    {
                        return typeProp.GetString() ?? fallbackCommand;
                    }
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

                // Пробуем разные пути к данным сообщения
                if (request.TryGetProperty("object", out var objectElement))
                {
                    // Вариант 1: object -> message
                    if (objectElement.TryGetProperty("message", out var messageElement))
                    {
                        return ParseMessageFromElement(messageElement);
                    }
                    // Вариант 2: object напрямую содержит данные сообщения
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

            // Пробуем разные варианты получения peer_id
            if (element.TryGetProperty("peer_id", out var peerIdProp))
            {
                message.PeerId = peerIdProp.GetInt64();
            }

            if (element.TryGetProperty("from_id", out var fromIdProp))
            {
                message.FromId = fromIdProp.GetInt64();
                // Если peer_id не нашли, используем from_id
                if (message.PeerId == 0)
                {
                    message.PeerId = message.FromId;
                }
            }

            if (element.TryGetProperty("user_id", out var userIdProp))
            {
                message.UserId = userIdProp.GetInt64();
                // Если peer_id все еще не нашли, используем user_id
                if (message.PeerId == 0)
                {
                    message.PeerId = message.UserId;
                }
            }

            if (element.TryGetProperty("text", out var textProp))
            {
                message.Text = textProp.GetString();
            }

            _logger.LogInformation($"Parsed message - FromId: {message.FromId}, UserId: {message.UserId}, PeerId: {message.PeerId}, Text: {message.Text}");

            return message;
        }
    }
}