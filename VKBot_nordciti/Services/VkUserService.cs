using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace VKBot_nordciti.Services
{
    public interface IVkUserService
    {
        Task<VkUserInfo?> GetUserInfoAsync(long userId);
    }

    public class VkUserInfo
    {
        public long Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Username { get; set; } = "";
    }

    public class VkUserService : IVkUserService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly FileLogger _logger;

        public VkUserService(IHttpClientFactory httpClientFactory, IConfiguration configuration, FileLogger logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<VkUserInfo?> GetUserInfoAsync(long userId)
        {
            try
            {
                var token = _configuration["VkSettings:Token"];
                var apiVersion = _configuration["VkSettings:ApiVersion"] ?? "5.131";

                if (string.IsNullOrEmpty(token))
                {
                    _logger.Warn("VK Token не настроен");
                    return GetFallbackUserInfo(userId);
                }

                var client = _httpClientFactory.CreateClient();
                var url = $"https://api.vk.com/method/users.get?user_ids={userId}&fields=first_name,last_name,screen_name&access_token={token}&v={apiVersion}";

                _logger.Info($"Запрос информации о пользователе VK ID: {userId}");

                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warn($"Ошибка VK API: {response.StatusCode}");
                    return GetFallbackUserInfo(userId);
                }

                var json = await response.Content.ReadAsStringAsync();
                _logger.Info($"Ответ VK API: {json}");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("error", out _))
                {
                    _logger.Warn($"VK API вернул ошибку");
                    return GetFallbackUserInfo(userId);
                }

                if (root.TryGetProperty("response", out var responseProp) && responseProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var user in responseProp.EnumerateArray())
                    {
                        var userInfo = new VkUserInfo
                        {
                            Id = userId,
                            FirstName = user.GetProperty("first_name").GetString() ?? "",
                            LastName = user.GetProperty("last_name").GetString() ?? ""
                        };

                        if (user.TryGetProperty("screen_name", out var screenName))
                        {
                            userInfo.Username = screenName.GetString() ?? "";
                        }

                        _logger.Info($"Получена информация о пользователе: {userInfo.FirstName} {userInfo.LastName}");
                        return userInfo;
                    }
                }

                return GetFallbackUserInfo(userId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Ошибка получения информации о пользователе {userId}");
                return GetFallbackUserInfo(userId);
            }
        }

        private VkUserInfo GetFallbackUserInfo(long userId)
        {
            return new VkUserInfo
            {
                Id = userId,
                FirstName = "Пользователь",
                LastName = "",
                Username = $"id{userId}"
            };
        }
    }
}