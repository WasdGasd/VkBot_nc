using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using VK.Models;

namespace VK
{
    // Простой менеджер для отправки сообщений через VK API
    public class VkApiManager
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration _config;
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        public VkApiManager(IHttpClientFactory httpFactory, IConfiguration config)
        {
            _httpFactory = httpFactory;
            _config = config;
        }

        public async Task SendMessageAsync(long peerId, string message, string? keyboardJson = null)
        {
            var client = _httpFactory.CreateClient();
            var token = _config["VkSettings:Token"] ?? throw new InvalidOperationException("Vk token missing");
            var v = _config["VkSettings:ApiVersion"] ?? "5.131";

            var parameters = new Dictionary<string, string>
            {
                ["peer_id"] = peerId.ToString(),
                ["random_id"] = new Random().Next().ToString(),
                ["message"] = message,
                ["access_token"] = token,
                ["v"] = v
            };

            if (!string.IsNullOrEmpty(keyboardJson))
                parameters["keyboard"] = keyboardJson;

            var resp = await client.PostAsync("https://api.vk.com/method/messages.send", new FormUrlEncodedContent(parameters));
            // Try to log if not OK
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new Exception($"VK API error {resp.StatusCode}: {body}");
            }
        }
    }
}
