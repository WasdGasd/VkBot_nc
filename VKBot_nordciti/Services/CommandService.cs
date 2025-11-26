using VKBot_nordciti.Services;
using System.Net.Http.Json;
using System.Text.Json;
using VKBot_nordciti.Models;

namespace VKBot_nordciti.Services
{
    public class CommandService : ICommandService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly KeyboardProvider _kb;
        private readonly JsonSerializerOptions _jsonOptions;

        public CommandService(IHttpClientFactory httpClientFactory, KeyboardProvider kb)
        {
            _httpClientFactory = httpClientFactory;
            _kb = kb;
            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        public async Task<Command?> FindCommandAsync(string messageText)
        {
            // Временная заглушка - в реальности будет работать с БД
            return await Task.FromResult<Command?>(null);
        }

        public async Task<List<Command>> GetAllCommandsAsync()
        {
            // Временная заглушка
            return await Task.FromResult(new List<Command>());
        }

        public async Task<string> ProcessCommandAsync(Command command, Dictionary<string, string>? parameters = null)
        {
            return command.CommandType.ToLower() switch
            {
                "api_park_load" => await GetParkLoadAsync(),
                "api_sessions" => await GetSessionsAsync(parameters),
                "api_tariffs" => await GetTariffsAsync(parameters),
                _ => command.Response
            };
        }

        private async Task<string> GetParkLoadAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var requestData = new { SiteID = "1" };
                var response = await client.PostAsJsonAsync("https://apigateway.nordciti.ru/v1/aqua/CurrentLoad", requestData);

                if (!response.IsSuccessStatusCode)
                    return "❌ Не удалось получить данные о загруженности. Попробуйте позже 😔";

                var data = await response.Content.ReadFromJsonAsync<ParkLoadResponse>(_jsonOptions);
                if (data == null)
                    return "❌ Не удалось обработать данные о загруженности 😔";

                string loadStatus = data.Load switch
                {
                    < 30 => "🟢 Низкая загруженность",
                    < 60 => "🟡 Средняя загруженность",
                    < 85 => "🟠 Высокая загруженность",
                    _ => "🔴 Очень высокая загруженность"
                };

                string recommendation = data.Load switch
                {
                    < 30 => "🌟 Идеальное время для посещения!",
                    < 50 => "👍 Хорошее время, народу немного",
                    < 70 => "⚠️ Средняя загруженность, возможны очереди",
                    < 85 => "📢 Много посетителей, лучше выбрать другое время",
                    _ => "🚫 Очень высокая загруженность, не рекомендуется"
                };

                return $"📊 Загруженность аквапарка:\n\n" +
                       $"👥 Количество посетителей: {data.Count} чел.\n" +
                       $"📈 Уровень загруженности: {data.Load}%\n" +
                       $"🏷 Статус: {loadStatus}\n\n" +
                       $"💡 Рекомендация:\n{recommendation}\n\n" +
                       $"🕐 Обновлено: {DateTime.Now:HH:mm}";
            }
            catch (Exception)
            {
                return "❌ Не удалось получить информацию о загруженности. Попробуйте позже 😔";
            }
        }

        private async Task<string> GetSessionsAsync(Dictionary<string, string>? parameters)
        {
            return "Функция сеансов временно недоступна";
        }

        private async Task<string> GetTariffsAsync(Dictionary<string, string>? parameters)
        {
            return "Функция тарифов временно недоступна";
        }

        private class ParkLoadResponse
        {
            public int Count { get; set; }
            public int Load { get; set; }
        }
    }
}