using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VKBot_nordciti.Services
{
    public interface IUserSyncService
    {
        Task<bool> SyncUserAsync(long vkUserId, string firstName, string lastName, string username, bool isOnline = true);
        Task<bool> UpdateActivityAsync(long vkUserId, bool isOnline);
        Task<bool> IncrementMessageCountAsync(long vkUserId);
        Task<string> GetStatsAsync();
        Task<string> SearchUsersAsync(string query, int limit = 5);
        Task<string> ManageUserAsync(long vkUserId, bool ban, string reason = "");
    }

    public class UserSyncService : IUserSyncService
    {
        private readonly HttpClient _httpClient;
        private readonly string _adminPanelBaseUrl;
        private readonly FileLogger _logger;
        private readonly bool _adminPanelEnabled;

        public UserSyncService(IHttpClientFactory httpClientFactory, FileLogger logger, IConfiguration configuration)
        {
            _httpClient = httpClientFactory.CreateClient();
            _adminPanelBaseUrl = configuration["AdminPanel:BaseUrl"] ?? "http://localhost:5215"; // 🔥 Ваш порт 5215
            _logger = logger;

            // Проверяем, указан ли URL админ-панели
            _adminPanelEnabled = !string.IsNullOrEmpty(_adminPanelBaseUrl);

            // Настраиваем таймаут
            _httpClient.Timeout = TimeSpan.FromSeconds(3); // 🔥 Уменьшили таймаут до 3 секунд

            _logger.Info($"Админ-панель: {(string.IsNullOrEmpty(_adminPanelBaseUrl) ? "Не настроена" : _adminPanelBaseUrl)}");
            _logger.Info($"Синхронизация с админ-панелью: {(_adminPanelEnabled ? "Включена" : "Отключена")}");
        }

        /// <summary>
        /// Полная синхронизация пользователя
        /// </summary>
        public async Task<bool> SyncUserAsync(long vkUserId, string firstName, string lastName, string username, bool isOnline = true)
        {
            // 🔥 Если админ-панель не настроена, просто возвращаем true
            if (!_adminPanelEnabled)
            {
                _logger.Info($"Синхронизация отключена. Пользователь {vkUserId} пропущен");
                return true;
            }

            try
            {
                var userData = new
                {
                    vkUserId = vkUserId,
                    firstName = firstName,
                    lastName = lastName,
                    username = username ?? "",
                    isActive = true,
                    isOnline = isOnline,
                    lastActivity = DateTime.UtcNow
                };

                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(userData),
                    Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PostAsync(
                    $"{_adminPanelBaseUrl}/api/users",
                    jsonContent
                );

                if (response.IsSuccessStatusCode)
                {
                    _logger.Info($"Пользователь {vkUserId} синхронизирован с админ-панелью");
                    return true;
                }
                else
                {
                    _logger.Warn($"Ошибка синхронизации пользователя {vkUserId}: {response.StatusCode}");
                    // Пробуем обновить активность если пользователь уже существует
                    return await UpdateActivityAsync(vkUserId, isOnline);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Ошибка синхронизации пользователя {vkUserId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Быстрое обновление активности
        /// </summary>
        public async Task<bool> UpdateActivityAsync(long vkUserId, bool isOnline)
        {
            if (!_adminPanelEnabled) return true;

            try
            {
                var activityData = new
                {
                    isOnline = isOnline,
                    lastActivity = DateTime.UtcNow
                };

                var response = await _httpClient.PatchAsync(
                    $"{_adminPanelBaseUrl}/api/users/vk/{vkUserId}/activity",
                    new StringContent(
                        JsonSerializer.Serialize(activityData),
                        Encoding.UTF8,
                        "application/json")
                    );

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Ошибка обновления активности пользователя {vkUserId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Увеличение счетчика сообщений
        /// </summary>
        public async Task<bool> IncrementMessageCountAsync(long vkUserId)
        {
            if (!_adminPanelEnabled) return true;

            try
            {
                var response = await _httpClient.PostAsync(
                    $"{_adminPanelBaseUrl}/api/users/vk/{vkUserId}/message",
                    new StringContent("{}", Encoding.UTF8, "application/json")
                );

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                else
                {
                    _logger.Warn($"Ошибка увеличения счетчика сообщений {vkUserId}: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Ошибка увеличения счетчика сообщений пользователя {vkUserId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Получение статистики
        /// </summary>
        public async Task<string> GetStatsAsync()
        {
            if (!_adminPanelEnabled)
                return "📊 Статистика недоступна\nАдмин-панель не настроена";

            try
            {
                var response = await _httpClient.GetAsync($"{_adminPanelBaseUrl}/api/users/stats");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);

                    var root = doc.RootElement;
                    if (root.TryGetProperty("success", out var successProp) && successProp.GetBoolean())
                    {
                        var data = root.GetProperty("data");
                        var totalUsers = data.GetProperty("totalUsers").GetInt32();
                        var activeUsers = data.GetProperty("activeUsers").GetInt32();
                        var onlineUsers = data.GetProperty("onlineUsers").GetInt32();
                        var newToday = data.TryGetProperty("newToday", out var newTodayProp) ? newTodayProp.GetInt32() : 0;

                        return $"📊 Статистика пользователей:\n" +
                               $"👥 Всего пользователей: {totalUsers}\n" +
                               $"🟢 Активных: {activeUsers}\n" +
                               $"🟡 Онлайн сейчас: {onlineUsers}\n" +
                               $"📅 Новых сегодня: {newToday}";
                    }
                }

                return "Не удалось получить статистику";
            }
            catch (Exception ex)
            {
                _logger.Warn($"Ошибка получения статистики: {ex.Message}");
                return "Ошибка при получении статистики";
            }
        }

        /// <summary>
        /// Поиск пользователей
        /// </summary>
        public async Task<string> SearchUsersAsync(string query, int limit = 5)
        {
            if (!_adminPanelEnabled)
                return "🔍 Поиск недоступен\nАдмин-панель не настроена";

            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_adminPanelBaseUrl}/api/users/search?query={Uri.EscapeDataString(query)}&limit={limit}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);

                    var root = doc.RootElement;
                    if (root.TryGetProperty("success", out var successProp) && successProp.GetBoolean())
                    {
                        var data = root.GetProperty("data");
                        if (data.GetArrayLength() == 0)
                        {
                            return "Пользователи не найдены";
                        }

                        var responseText = "🔍 Найденные пользователи:\n\n";
                        var count = 0;

                        foreach (var user in data.EnumerateArray())
                        {
                            if (count >= limit) break;

                            var firstName = user.GetProperty("firstName").GetString() ?? "";
                            var lastName = user.GetProperty("lastName").GetString() ?? "";
                            var username = user.TryGetProperty("username", out var un) ? un.GetString() : "";
                            var vkUserId = user.GetProperty("vkUserId").GetInt64();
                            var messageCount = user.GetProperty("messageCount").GetInt32();
                            var isOnline = user.GetProperty("isOnline").GetBoolean();
                            var isActive = user.GetProperty("isActive").GetBoolean();

                            responseText += $"👤 {firstName} {lastName}\n";
                            if (!string.IsNullOrEmpty(username))
                                responseText += $"   @{username}\n";
                            responseText += $"   VK ID: {vkUserId}\n";
                            responseText += $"   Сообщений: {messageCount}\n";
                            responseText += $"   Статус: {(isOnline ? "🟢 Онлайн" : isActive ? "🟢 Активен" : "⚪ Неактивен")}\n\n";

                            count++;
                        }

                        return responseText;
                    }
                }

                return "Ошибка поиска пользователей";
            }
            catch (Exception ex)
            {
                _logger.Warn($"Ошибка поиска пользователей: {ex.Message}");
                return "Ошибка при поиске пользователей";
            }
        }

        /// <summary>
        /// Управление пользователем (блокировка/разблокировка)
        /// </summary>
        public async Task<string> ManageUserAsync(long vkUserId, bool ban, string reason = "")
        {
            if (!_adminPanelEnabled)
                return "Админ-панель не настроена";

            try
            {
                // Получаем информацию о пользователе
                var userResponse = await _httpClient.GetAsync($"{_adminPanelBaseUrl}/api/users/vk/{vkUserId}");
                if (!userResponse.IsSuccessStatusCode)
                {
                    return $"Пользователь с VK ID {vkUserId} не найден";
                }

                var userContent = await userResponse.Content.ReadAsStringAsync();
                using var userDoc = JsonDocument.Parse(userContent);
                var userRoot = userDoc.RootElement;

                if (!userRoot.GetProperty("success").GetBoolean())
                {
                    return "Пользователь не найден";
                }

                var userData = userRoot.GetProperty("data");
                var userId = userData.GetProperty("id").GetInt32();
                var firstName = userData.GetProperty("firstName").GetString();
                var lastName = userData.GetProperty("lastName").GetString();

                // Обновляем статус
                var updateData = new
                {
                    isActive = !ban,
                    isBanned = ban
                };

                var updateResponse = await _httpClient.PatchAsync(
                    $"{_adminPanelBaseUrl}/api/users/{userId}/status",
                    new StringContent(
                        JsonSerializer.Serialize(updateData),
                        Encoding.UTF8,
                        "application/json")
                    );

                if (updateResponse.IsSuccessStatusCode)
                {
                    return ban
                        ? $"✅ Пользователь {firstName} {lastName} заблокирован{(string.IsNullOrEmpty(reason) ? "" : $". Причина: {reason}")}"
                        : $"✅ Пользователь {firstName} {lastName} разблокирован";
                }

                return "Ошибка обновления статуса пользователя";
            }
            catch (Exception ex)
            {
                _logger.Warn($"Ошибка управления пользователем: {ex.Message}");
                return "Ошибка выполнения команды";
            }
        }
    }
}