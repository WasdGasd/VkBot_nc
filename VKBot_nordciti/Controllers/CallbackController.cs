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
        private readonly IConfiguration _configuration;
        private readonly ILogger<CallbackController> _logger;

        public CallbackController(IMessageService messageService, IConfiguration configuration, ILogger<CallbackController> logger)
        {
            _messageService = messageService;
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
                            await _messageService.ProcessMessageAsync(message);
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