using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace VKBot_nordciti.VK
{
    public class VkApiManager
    {
        private readonly HttpClient _httpClient;
        private readonly string _accessToken;
        private readonly string _apiVersion;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ILogger<VkApiManager> _logger;

        public VkApiManager(HttpClient httpClient, IConfiguration configuration, ILogger<VkApiManager> logger)
        {
            _httpClient = httpClient;
            _accessToken = configuration["VkSettings:Token"] ?? "";
            _apiVersion = configuration["VkSettings:ApiVersion"] ?? "5.131";
            _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            _logger = logger;

            _logger.LogInformation($"VkApiManager initialized with Token: {_accessToken.Substring(0, 10)}...");
        }

        public async Task<bool> SendMessageAsync(long peerId, string message, string? keyboardJson = null)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Clear();

                var parameters = new Dictionary<string, string>
                {
                    ["peer_id"] = peerId.ToString(),
                    ["message"] = message,
                    ["random_id"] = new Random().Next(1000000, 9999999).ToString(),
                    ["access_token"] = _accessToken,
                    ["v"] = _apiVersion
                };

                if (!string.IsNullOrEmpty(keyboardJson))
                {
                    parameters["keyboard"] = keyboardJson;
                }

                _logger.LogInformation($"Sending message to peer {peerId}: {message.Substring(0, Math.Min(50, message.Length))}...");

                var content = new FormUrlEncodedContent(parameters);
                var response = await _httpClient.PostAsync("https://api.vk.com/method/messages.send", content);

                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"VK API Response: {responseContent}");

                if (response.IsSuccessStatusCode && responseContent.Contains("response"))
                {
                    _logger.LogInformation($"Message sent successfully to peer {peerId}");
                    return true;
                }

                _logger.LogError($"VK API Error: {response.StatusCode} - {responseContent}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"VK API Exception for peer {peerId}");
                return false;
            }
        }
    }
}