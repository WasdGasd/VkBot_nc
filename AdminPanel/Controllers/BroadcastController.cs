using AdminPanel.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite; // Добавь этот using
using Microsoft.Extensions.Options;
using System.Text.Json;
namespace AdminPanel.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BroadcastController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly BotApiConfig _botSettings;
        private readonly ILogger<BroadcastController> _logger;
        private readonly IConfiguration _configuration;

        public BroadcastController(
            IHttpClientFactory httpClientFactory,
            IOptions<BotApiConfig> botSettings,
            ILogger<BroadcastController> logger,
            IConfiguration configuration)
        {
            _httpClient = httpClientFactory.CreateClient();
            _botSettings = botSettings.Value;
            _logger = logger;
            _configuration = configuration;
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        [HttpPost]
        public async Task<IActionResult> SendBroadcast([FromBody] BroadcastMessageRequest request)
        {
            try
            {
                _logger.LogInformation("Начинаем рассылку. Получатели: {RecipientType}, Длина сообщения: {Length}",
                    
                    request.RecipientType, request.Message?.Length ?? 0);

                if (string.IsNullOrWhiteSpace(request.Message))
                {
                    return BadRequest(new { success = false, message = "Сообщение не может быть пустым" });
                }

                var users = await GetUsersFromBot(request.RecipientType);

                if (users.Count == 0)
                {
                    return Ok(new
                    {
                        success = false,
                        message = "Нет пользователей для рассылки"
                    });
                }

                _logger.LogInformation("Найдено {Count} пользователей для рассылки", users.Count);

                var results = await SendMessagesViaVkApi(users, request.Message);

                _logger.LogInformation("Рассылка завершена. Успешно: {Success}, Ошибок: {Failed}",
                    results.SuccessCount, results.FailedCount);

                return Ok(new
                {
                    success = true,
                    message = $"Рассылка завершена! Успешно отправлено: {results.SuccessCount}, Ошибок: {results.FailedCount}",
                    total = users.Count,
                    successCount = results.SuccessCount,
                    failCount = results.FailedCount,
                    errors = results.Errors.Take(10).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в рассылке");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        private async Task<List<long>> GetUsersFromBot(string recipientType)
        {
            try
            {
                _logger.LogInformation("Получаем пользователей. Фильтр: {RecipientType}", recipientType);

                // Путь к БД бота (проверь что он правильный!)
                var botDbPath = @"C:\Users\pog\Desktop\VkBot_nc\VKBot_nordciti\vkbot.db";

                if (!System.IO.File.Exists(botDbPath))
                {
                    _logger.LogWarning("Файл БД бота не найден: {Path}", botDbPath);
                    return GetTestUsers();
                }

                var connectionString = $"Data Source={botDbPath};";
                using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                // РАЗНЫЕ ЗАПРОСЫ ДЛЯ РАЗНЫХ ФИЛЬТРОВ:
                string query;
                if (recipientType == "active")
                {
                    // Только те, кто писал СЕГОДНЯ
                    query = "SELECT DISTINCT UserId FROM UserActivity WHERE ActivityDate = date('now')ORDER BY LastActivity DESC";
                }
                else // "all"
                {
                    // ВСЕ пользователи из БД
                    query = "SELECT VkUserId FROM Users";
                }

                using var command = new SqliteCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                var users = new List<long>();
                while (await reader.ReadAsync())
                {
                    users.Add(reader.GetInt64(0));
                }

                _logger.LogInformation("Найдено {Count} пользователей (фильтр: {RecipientType})",
                    users.Count, recipientType);
                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка чтения БД бота");
                return GetTestUsers();
            }
        }

      

        private List<long> GetTestUsers()
        {
            var adminIds = _configuration.GetSection("VkApi:AdminIds").Get<long[]>() ?? Array.Empty<long>();
            return adminIds.ToList();
        }

        private async Task<MessageSendResults> SendMessagesViaVkApi(List<long> userIds, string message)
        {
            var results = new MessageSendResults();
            var vkToken = _configuration["VkApi:AccessToken"];

            if (string.IsNullOrEmpty(vkToken))
            {
                _logger.LogError("VK токен не настроен");
                results.Errors.Add("VK токен не настроен в конфигурации");
                return results;
            }

            foreach (var userId in userIds)
            {
                try
                {
                    var vkResponse = await SendVkMessage(userId, message, vkToken);

                    if (vkResponse.IsSuccessStatusCode)
                    {
                        results.SuccessCount++;
                    }
                    else
                    {
                        results.FailedCount++;
                        results.Errors.Add($"Пользователь {userId}: {vkResponse.StatusCode}");
                    }

                    if (results.SuccessCount % 10 == 0)
                    {
                        await Task.Delay(1000);
                    }
                    else
                    {
                        await Task.Delay(100);
                    }
                }
                catch (Exception ex)
                {
                    results.FailedCount++;
                    results.Errors.Add($"Пользователь {userId}: {ex.Message}");
                    _logger.LogWarning("Ошибка отправки пользователю {UserId}: {Error}", userId, ex.Message);
                }
            }

            return results;
        }

        private async Task<HttpResponseMessage> SendVkMessage(long userId, string message, string token)
        {
            var url = "https://api.vk.com/method/messages.send";

            var parameters = new Dictionary<string, string>
            {
                ["user_id"] = userId.ToString(),
                ["message"] = message,
                ["random_id"] = new Random().Next().ToString(),
                ["access_token"] = token,
                ["v"] = "5.131",
                ["group_id"] = "233846417"
            };

            var content = new FormUrlEncodedContent(parameters);
            return await _httpClient.PostAsync(url, content);
        }
    }

    // Модели внутри namespace но вне класса контроллера
    public class BroadcastMessageRequest
    {
        public string Message { get; set; } = string.Empty;
        public string RecipientType { get; set; } = "all";
    }

    public class BroadcastUsersResponse
    {
        public List<long> UserIds { get; set; } = new();
    }

    public class MessageSendResults
    {
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}