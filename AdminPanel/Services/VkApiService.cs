using AdminPanel.Models;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdminPanel.Services
{
    public class VkApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _accessToken;
        private readonly string _apiVersion;
        private readonly string _groupId;
        private readonly ILogger<VkApiService> _logger;
        private readonly bool _isEnabled;
        private readonly IConfiguration _configuration; // Добавил поле

        public VkApiService(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<VkApiService> logger)
        {
            _configuration = configuration; // Сохраняем конфигурацию
            _logger = logger;
            _accessToken = configuration["VkApi:AccessToken"] ?? "";
            _apiVersion = configuration["VkApi:ApiVersion"] ?? "5.131";
            _groupId = configuration["VkApi:GroupId"] ?? "";
            _isEnabled = !string.IsNullOrEmpty(_accessToken) && _accessToken != "YOUR_VK_API_TOKEN_HERE";

            _httpClient = httpClientFactory.CreateClient("VkApi");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            _logger.LogInformation("VkApiService инициализирован. Включен: {IsEnabled}", _isEnabled);
        }

        public bool IsEnabled => _isEnabled;

        public async Task<List<long>> GetConversationMembersAsync(int count = 200)
        {
            var userIds = new List<long>();

            if (!_isEnabled)
            {
                _logger.LogWarning("VK API отключен. Возвращаем пустой список.");
                return userIds;
            }

            try
            {
                var url = $"https://api.vk.com/method/messages.getConversations?count={count}&access_token={_accessToken}&v={_apiVersion}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<VkConversationsApiResponse>(content);

                    if (result?.Response?.Items != null)
                    {
                        foreach (var conversation in result.Response.Items)
                        {
                            if (conversation.LastMessage != null)
                            {
                                userIds.Add(conversation.LastMessage.FromId);
                            }
                        }

                        _logger.LogInformation("Получено {Count} ID из бесед", userIds.Count);
                    }
                    else if (result?.Error != null)
                    {
                        _logger.LogError("VK API ошибка: {Code} - {Message}",
                            result.Error.ErrorCode, result.Error.ErrorMessage);
                    }
                }
                else
                {
                    _logger.LogError("Ошибка HTTP при получении бесед: {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения участников бесед");
            }

            return userIds.Distinct().ToList();
        }

        public async Task<List<VkUserInfo>> GetUsersInfoAsync(List<long> userIds, string fields = "photo_100,online,last_seen,city,country")
        {
            var users = new List<VkUserInfo>();

            if (!_isEnabled || !userIds.Any())
            {
                return users;
            }

            try
            {
                var idsString = string.Join(",", userIds.Take(1000));
                var url = $"https://api.vk.com/method/users.get?user_ids={idsString}&fields={fields}&access_token={_accessToken}&v={_apiVersion}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<VkUsersApiResponse>(content);

                    if (result?.Response != null)
                    {
                        users = result.Response;
                        _logger.LogInformation("Получена информация о {Count} пользователях", users.Count);
                    }
                    else if (result?.Error != null)
                    {
                        _logger.LogError("VK API ошибка при получении пользователей: {Code} - {Message}",
                            result.Error.ErrorCode, result.Error.ErrorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения информации о пользователях");
            }

            return users;
        }

        public async Task<VkUserInfo?> GetUserInfoAsync(long userId, string fields = "photo_100,online,last_seen,city,country")
        {
            if (!_isEnabled)
                return null;

            try
            {
                var users = await GetUsersInfoAsync(new List<long> { userId }, fields);
                return users.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения информации о пользователе {UserId}", userId);
                return null;
            }
        }

        public async Task<bool> SendMessageAsync(long userId, string message, int? randomId = null)
        {
            if (!_isEnabled)
            {
                _logger.LogWarning("VK API отключен. Сообщение не отправлено.");
                return false;
            }

            try
            {
                randomId ??= new Random().Next(1, 1000000);
                var encodedMessage = Uri.EscapeDataString(message);

                var url = $"https://api.vk.com/method/messages.send?user_id={userId}&message={encodedMessage}&random_id={randomId}&access_token={_accessToken}&v={_apiVersion}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(content);

                    if (result.TryGetProperty("response", out _))
                    {
                        _logger.LogInformation("Сообщение отправлено пользователю {UserId}", userId);
                        return true;
                    }
                    else if (result.TryGetProperty("error", out var error))
                    {
                        var errorCode = error.GetProperty("error_code").GetInt32();
                        var errorMsg = error.GetProperty("error_msg").GetString();
                        _logger.LogError("VK API ошибка отправки: {Code} - {Message}", errorCode, errorMsg);
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка отправки сообщения пользователю {UserId}", userId);
                return false;
            }
        }

        public async Task<bool> BanUserAsync(long userId, string reason, int duration = 0, bool comment = true, bool commentVisible = true)
        {
            if (!_isEnabled)
            {
                _logger.LogWarning("VK API отключен. Бан не выполнен.");
                return false;
            }

            try
            {
                var url = $"https://api.vk.com/method/groups.ban?group_id={_groupId}&owner_id={userId}&reason={Uri.EscapeDataString(reason)}&comment={Convert.ToInt32(comment)}&comment_visible={Convert.ToInt32(commentVisible)}";

                if (duration > 0)
                {
                    url += $"&end_date={DateTimeOffset.UtcNow.AddSeconds(duration).ToUnixTimeSeconds()}";
                }

                url += $"&access_token={_accessToken}&v={_apiVersion}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(content);

                    if (result.TryGetProperty("response", out _))
                    {
                        _logger.LogInformation("Пользователь {UserId} забанен. Причина: {Reason}", userId, reason);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка бана пользователя {UserId}", userId);
                return false;
            }
        }

        public async Task<bool> UnbanUserAsync(long userId)
        {
            if (!_isEnabled)
            {
                _logger.LogWarning("VK API отключен. Разбан не выполнен.");
                return false;
            }

            try
            {
                var url = $"https://api.vk.com/method/groups.unban?group_id={_groupId}&owner_id={userId}&access_token={_accessToken}&v={_apiVersion}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(content);

                    if (result.TryGetProperty("response", out _))
                    {
                        _logger.LogInformation("Пользователь {UserId} разбанен", userId);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка разбана пользователя {UserId}", userId);
                return false;
            }
        }

        public async Task<int> GetTotalConversationsCountAsync()
        {
            if (!_isEnabled)
                return 0;

            try
            {
                var url = $"https://api.vk.com/method/messages.getConversations?count=0&access_token={_accessToken}&v={_apiVersion}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<VkConversationsApiResponse>(content);

                    return result?.Response?.Count ?? 0;
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения количества бесед");
                return 0;
            }
        }

        public async Task<List<long>> GetAllUserIdsFromConversationsAsync()
        {
            var allUserIds = new List<long>();

            if (!_isEnabled)
                return allUserIds;

            try
            {
                var total = await GetTotalConversationsCountAsync();
                var batchSize = 200;
                var offset = 0;

                _logger.LogInformation("Всего бесед: {Total}. Начинаем получение...", total);

                while (offset < total && offset < 1000) // Ограничим 1000 для безопасности
                {
                    var url = $"https://api.vk.com/method/messages.getConversations?offset={offset}&count={batchSize}&access_token={_accessToken}&v={_apiVersion}";

                    var response = await _httpClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var result = JsonSerializer.Deserialize<VkConversationsApiResponse>(content);

                        if (result?.Response?.Items != null)
                        {
                            foreach (var conversation in result.Response.Items)
                            {
                                if (conversation.LastMessage != null)
                                {
                                    allUserIds.Add(conversation.LastMessage.FromId);
                                }
                            }
                        }
                    }

                    offset += batchSize;
                    await Task.Delay(200); // Небольшая задержка между запросами
                }

                _logger.LogInformation("Получено {Count} уникальных ID пользователей", allUserIds.Distinct().Count());
                return allUserIds.Distinct().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения всех ID пользователей из бесед");
                return allUserIds;
            }
        }

        public async Task<bool> IsUserAdminAsync(long userId)
        {
            if (!_isEnabled)
                return false;

            try
            {
                var adminIds = _configuration.GetSection("VkApi:AdminIds").Get<long[]>() ?? Array.Empty<long>();
                return adminIds.Contains(userId);
            }
            catch
            {
                return false;
            }
        }

        public async Task<DateTime?> GetLastActivityAsync(long userId)
        {
            var userInfo = await GetUserInfoAsync(userId);
            return userInfo?.LastSeenDate;
        }
    }

    // Вспомогательные классы для десериализации
    public class VkUsersApiResponse
    {
        [JsonPropertyName("response")]
        public List<VkUserInfo>? Response { get; set; }

        [JsonPropertyName("error")]
        public VkApiError? Error { get; set; }
    }

    public class VkConversationsApiResponse
    {
        [JsonPropertyName("response")]
        public VkConversationsResponse? Response { get; set; }

        [JsonPropertyName("error")]
        public VkApiError? Error { get; set; }
    }
}