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
            return await Task.FromResult<Command?>(null);
        }

        public async Task<List<Command>> GetAllCommandsAsync()
        {
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

        public async Task<List<SessionInfo>> GetSessionsListAsync(string date)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var sessionsUrl = $"https://apigateway.nordciti.ru/v1/aqua/getSessionsAqua?date={date}";

                var sessionsResponse = await client.GetAsync(sessionsUrl);
                if (!sessionsResponse.IsSuccessStatusCode)
                    return new List<SessionInfo>();

                var sessionsJson = await sessionsResponse.Content.ReadAsStringAsync();
                var sessionsData = JsonSerializer.Deserialize<JsonElement>(sessionsJson, _jsonOptions);

                return ParseSessionsFromArray(GetSessionsArray(sessionsData));
            }
            catch (Exception)
            {
                return new List<SessionInfo>();
            }
        }

        private JsonElement GetSessionsArray(JsonElement sessionsData)
        {
            if (sessionsData.ValueKind == JsonValueKind.Array)
            {
                return sessionsData;
            }
            else if (sessionsData.TryGetProperty("result", out var resultProp) && resultProp.ValueKind == JsonValueKind.Array)
            {
                return resultProp;
            }
            else if (sessionsData.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
            {
                return dataProp;
            }
            else if (sessionsData.TryGetProperty("sessions", out var sessionsProp) && sessionsProp.ValueKind == JsonValueKind.Array)
            {
                return sessionsProp;
            }

            return default;
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
            try
            {
                var date = parameters?["date"] ?? DateTime.Now.ToString("dd.MM.yyyy");
                var sessions = await GetSessionsListAsync(date);

                if (sessions.Count == 0)
                {
                    return $"😔 На {date} нет доступных сеансов.";
                }

                var text = $"🎟 Доступные сеансы на {date}:\n\n";

                foreach (var session in sessions)
                {
                    string availability = session.PlacesFree switch
                    {
                        0 => "🔴 Нет мест",
                        < 10 => "🔴 Мало мест",
                        < 20 => "🟡 Средняя загрузка",
                        _ => "🟢 Есть места"
                    };

                    text += $"⏰ *{session.Time}*\n";
                    text += $"   Свободно: {session.PlacesFree}/{session.PlacesTotal} мест\n";
                    text += $"   {availability}\n\n";
                }

                return text;
            }
            catch (Exception ex)
            {
                return $"❌ Ошибка при получении сеансов: {ex.Message}";
            }
        }

        private async Task<string> GetTariffsAsync(Dictionary<string, string>? parameters)
        {
            try
            {
                var date = parameters?["date"] ?? DateTime.Now.ToString("dd.MM.yyyy");
                var sessionTime = parameters?["session"];
                var category = parameters?["category"];

                var client = _httpClientFactory.CreateClient();
                var tariffsUrl = $"https://apigateway.nordciti.ru/v1/aqua/getTariffsAqua?date={date}";
                var tariffsResponse = await client.GetAsync(tariffsUrl);

                if (!tariffsResponse.IsSuccessStatusCode)
                    return "❌ Ошибка при загрузке тарифов";

                var tariffsJson = await tariffsResponse.Content.ReadAsStringAsync();
                var tariffsData = JsonSerializer.Deserialize<JsonElement>(tariffsJson, _jsonOptions);

                return ProcessTariffsData(tariffsData, date, sessionTime, category);
            }
            catch (Exception ex)
            {
                return $"❌ Ошибка при получении тарифов: {ex.Message}";
            }
        }

        private List<SessionInfo> ParseSessionsFromArray(JsonElement array)
        {
            var sessions = new List<SessionInfo>();

            if (array.ValueKind != JsonValueKind.Array)
                return sessions;

            foreach (var item in array.EnumerateArray())
            {
                try
                {
                    var session = new SessionInfo();

                    // Пробуем разные названия полей для времени
                    string[] timeFields = { "sessionTime", "SessionTime", "time", "Time", "name", "Name", "title", "Title" };
                    foreach (var field in timeFields)
                    {
                        if (item.TryGetProperty(field, out var timeProp) && timeProp.ValueKind == JsonValueKind.String)
                        {
                            session.Time = timeProp.GetString() ?? "";
                            break;
                        }
                    }

                    // Пробуем разные названия полей для свободных мест
                    string[] freeFields = { "availableCount", "AvailableCount", "placesFree", "PlacesFree", "free", "Free", "available", "Available" };
                    foreach (var field in freeFields)
                    {
                        if (item.TryGetProperty(field, out var freeProp) && freeProp.ValueKind == JsonValueKind.Number)
                        {
                            session.PlacesFree = freeProp.GetInt32();
                            break;
                        }
                    }

                    // Пробуем разные названия полей для общих мест
                    string[] totalFields = { "totalCount", "TotalCount", "placesTotal", "PlacesTotal", "total", "Total", "capacity", "Capacity" };
                    foreach (var field in totalFields)
                    {
                        if (item.TryGetProperty(field, out var totalProp) && totalProp.ValueKind == JsonValueKind.Number)
                        {
                            session.PlacesTotal = totalProp.GetInt32();
                            break;
                        }
                    }

                    // Если время не найдено в стандартных полях, ищем в любом строковом поле
                    if (string.IsNullOrEmpty(session.Time))
                    {
                        foreach (var property in item.EnumerateObject())
                        {
                            if (property.Value.ValueKind == JsonValueKind.String)
                            {
                                var value = property.Value.GetString();
                                if (!string.IsNullOrEmpty(value) && value.Contains(":") && value.Length <= 8)
                                {
                                    session.Time = value;
                                    break;
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(session.Time) && session.PlacesFree >= 0)
                    {
                        sessions.Add(session);
                    }
                }
                catch
                {
                    continue;
                }
            }

            return sessions;
        }

        private string ProcessTariffsData(JsonElement tariffsData, string date, string sessionTime, string category)
        {
            try
            {
                string categoryTitle = category == "adult" ? "👤 ВЗРОСЛЫЕ БИЛЕТЫ" : "👶 ДЕТСКИЕ БИЛЕТЫ";
                var text = $"🎟 *{categoryTitle}*\n";
                text += $"⏰ Сеанс: {sessionTime}\n";
                text += $"📅 Дата: {date}\n\n";

                var tariffs = new List<TariffInfo>();

                if (tariffsData.ValueKind == JsonValueKind.Array)
                {
                    tariffs = ParseTariffsFromArray(tariffsData, category);
                }
                else if (tariffsData.TryGetProperty("result", out var resultProp) && resultProp.ValueKind == JsonValueKind.Array)
                {
                    tariffs = ParseTariffsFromArray(resultProp, category);
                }

                if (tariffs.Count == 0)
                {
                    text += "😔 Нет доступных билетов этой категории\n";
                    text += "💡 Попробуйте выбрать другую категорию";
                }
                else
                {
                    text += "💰 Стоимость билетов:\n\n";

                    foreach (var tariff in tariffs)
                    {
                        string emoji = tariff.Price > 2000 ? "💎" : tariff.Price > 1000 ? "⭐" : "🎫";
                        text += $"{emoji} *{tariff.Name}*: {tariff.Price}₽\n";
                    }

                    text += $"\n💡 Примечания:\n";
                    text += $"• Детский билет - для детей от 4 до 12 лет\n";
                    text += $"• Дети до 4 лет - бесплатно (с взрослым)\n";
                    text += $"• VIP билеты включают дополнительные услуги\n";
                }

                text += $"\n\n🔗 *Купить онлайн:* yes35.ru";

                return text;
            }
            catch (Exception)
            {
                return "❌ Ошибка при обработке данных тарифов";
            }
        }

        private List<TariffInfo> ParseTariffsFromArray(JsonElement array, string category)
        {
            var tariffs = new List<TariffInfo>();

            foreach (var item in array.EnumerateArray())
            {
                try
                {
                    var tariff = new TariffInfo();

                    // Получаем название
                    string[] nameFields = { "Name", "name", "Title", "title" };
                    foreach (var field in nameFields)
                    {
                        if (item.TryGetProperty(field, out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                        {
                            tariff.Name = nameProp.GetString() ?? "";
                            break;
                        }
                    }

                    // Получаем цену
                    string[] priceFields = { "Price", "price", "Cost", "cost" };
                    foreach (var field in priceFields)
                    {
                        if (item.TryGetProperty(field, out var priceProp) && priceProp.ValueKind == JsonValueKind.Number)
                        {
                            tariff.Price = priceProp.GetDecimal();
                            break;
                        }
                    }

                    // Фильтруем по категории
                    if (!string.IsNullOrEmpty(tariff.Name) && tariff.Price > 0)
                    {
                        string nameLower = tariff.Name.ToLower();
                        bool isAdult = nameLower.Contains("взрос") || nameLower.Contains("adult");
                        bool isChild = nameLower.Contains("детск") || nameLower.Contains("child") || nameLower.Contains("kids");

                        if ((category == "adult" && isAdult && !isChild) ||
                            (category == "child" && isChild && !isAdult))
                        {
                            // Форматируем название
                            tariff.Name = FormatTicketName(tariff.Name);
                            tariffs.Add(tariff);
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            return tariffs.DistinctBy(t => t.Name).OrderByDescending(t => t.Price).ToList();
        }

        private string FormatTicketName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "Стандартный";

            var formatted = name
                .Replace("Билет", "")
                .Replace("билет", "")
                .Replace("Вип", "VIP")
                .Replace("вип", "VIP")
                .Replace("весь день", "Весь день")
                .Replace("взрослый", "")
                .Replace("детский", "")
                .Replace("  ", " ")
                .Trim();

            if (formatted.StartsWith("VIP") || formatted.StartsWith("Вип"))
            {
                formatted = "VIP" + formatted.Substring(3).Trim();
            }

            while (formatted.Contains("  "))
            {
                formatted = formatted.Replace("  ", " ");
            }

            return string.IsNullOrEmpty(formatted) ? "Стандартный" : formatted;
        }

        private class ParkLoadResponse
        {
            public int Count { get; set; }
            public int Load { get; set; }
        }

        public class SessionInfo
        {
            public string Time { get; set; } = string.Empty;
            public int PlacesFree { get; set; }
            public int PlacesTotal { get; set; }
        }

        private class TariffInfo
        {
            public string Name { get; set; } = string.Empty;
            public decimal Price { get; set; }
        }
    }
}