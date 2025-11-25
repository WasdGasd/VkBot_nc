using Microsoft.EntityFrameworkCore;
using Models;
using System.Text.Json;
using VK;
using VK.Models;
using VKBD_nc.Data;
using VKBD_nc.models;

namespace BotServices
{
    public class MesService : IMessageService
    {
        private readonly VkApiManager _vk;
        private readonly KeyboardProvider _kb;
        private readonly ConversationStateService _state;
        private readonly FileLogger _logger;
        private readonly CommandService _commandService;
        private readonly BotDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;

        private readonly JsonSerializerOptions _jsonOptions =
            new() { PropertyNameCaseInsensitive = true };

        public MesService(
            VkApiManager vkApi,
            KeyboardProvider kb,
            ConversationStateService state,
            FileLogger logger,
            CommandService commandService,
            BotDbContext db,
            IHttpClientFactory httpClientFactory)
        {
            _vk = vkApi;
            _kb = kb;
            _state = state;
            _logger = logger;
            _commandService = commandService;
            _db = db;
            _httpClientFactory = httpClientFactory;
        }

        public async Task ProcessMessageAsync(VkMessage message)
        {
            try
            {
                var userId = message.UserId;
                var text = (message.Text ?? string.Empty).Trim();

                _logger.Info($"Received from {userId}: {text}");

                var state = _state.GetState(userId);

                // ======================================================
                //                КОМАНДЫ ИЗ БАЗЫ ДАННЫХ
                // ======================================================
                var dbCommand = await _commandService.FindCommandAsync(text);

                if (dbCommand != null)
                {
                    _db.CommandLogs.Add(new CommandLog
                    {
                        UserId = userId,
                        Command = dbCommand.Name,
                        Timestamp = DateTime.Now
                    });

                    await _db.SaveChangesAsync();

                    // ОСОБАЯ ОБРАБОТКА ДЛЯ ЗАГРУЖЕННОСТИ - ВЫЗОВ API
                    if (dbCommand.Name.Contains("📊") || dbCommand.Name.ToLower().Contains("загруженность"))
                    {
                        var loadInfo = await GetParkLoadAsync();
                        await _vk.SendMessageAsync(message.PeerId, loadInfo, _kb.BackToMain());
                    }
                    else if (!string.IsNullOrWhiteSpace(dbCommand.KeyboardJson))
                    {
                        await _vk.SendMessageAsync(message.PeerId, dbCommand.Response, dbCommand.KeyboardJson);
                    }
                    else
                    {
                        await _vk.SendMessageAsync(message.PeerId, dbCommand.Response);
                    }

                    _state.SetState(userId, ConversationState.Idle);
                    return;
                }

                // ======================================================
                // 2. ПОТОМ - API ДАННЫЕ (динамические через состояния)
                // ======================================================
                switch (state)
                {
                    case ConversationState.Idle:
                        // Обработка загруженности напрямую из сообщения
                        if (text.Contains("📊") || text.ToLower().Contains("загруженность"))
                        {
                            var loadInfo = await GetParkLoadAsync();
                            await _vk.SendMessageAsync(message.PeerId, loadInfo, _kb.BackToMain());
                        }
                        // Обработка начала покупки билетов
                        else if (text.Contains("📅") || text.ToLower().Contains("билеты") || text.ToLower().Contains("билет"))
                        {
                            _state.SetState(userId, ConversationState.WaitingForDate);
                            await _vk.SendMessageAsync(message.PeerId, "Выберите дату для посещения:", _kb.TicketsDateKeyboard());
                        }
                        else
                        {
                            await _vk.SendMessageAsync(message.PeerId, "Я вас не понял — выберите пункт меню 👇", _kb.MainMenu());
                        }
                        break;

                    case ConversationState.WaitingForDate:
                        if (text.StartsWith("📅"))
                        {
                            var date = text.Replace("📅", "").Trim();
                            _state.SetData(userId, "date", date);
                            _state.SetState(userId, ConversationState.WaitingForSession);

                            // API: получение сеансов
                            var (sessionsText, keyboardJson) = await GetSessionsForDateAsync(date);
                            await _vk.SendMessageAsync(message.PeerId, sessionsText, keyboardJson);
                        }
                        else
                        {
                            await _vk.SendMessageAsync(message.PeerId, "Пожалуйста, выберите дату кнопкой 📅", _kb.TicketsDateKeyboard());
                        }
                        break;

                    case ConversationState.WaitingForSession:
                        if (text.StartsWith("⏰"))
                        {
                            var session = text.Replace("⏰", "").Trim();
                            _state.SetData(userId, "session", session);
                            _state.SetState(userId, ConversationState.WaitingForCategory);

                            await _vk.SendMessageAsync(message.PeerId,
                                $"Вы выбрали сеанс {session}. Теперь выберите категорию билетов:",
                                _kb.TicketCategoryKeyboard());
                        }
                        else if (text.Contains("🔙") || text.ToLower().Contains("назад"))
                        {
                            _state.SetState(userId, ConversationState.WaitingForDate);
                            await _vk.SendMessageAsync(message.PeerId, "Выберите дату:", _kb.TicketsDateKeyboard());
                        }
                        else
                        {
                            await _vk.SendMessageAsync(message.PeerId, "Выберите сеанс кнопкой ⏰", _kb.BackToSessions());
                        }
                        break;

                    case ConversationState.WaitingForCategory:
                        if (IsTicketCategoryMessage(text))
                        {
                            var category = GetTicketCategoryFromMessage(text);
                            _state.SetData(userId, "category", category);

                            var date = _state.GetData(userId, "date") ?? "неизвестная дата";
                            var sessionSelected = _state.GetData(userId, "session") ?? "неизвестный сеанс";

                            // API: получение тарифов
                            var (tariffsText, tariffsKb) = await GetFormattedTariffsAsync(date, sessionSelected, category);

                            _state.SetState(userId, ConversationState.WaitingForPayment);
                            await _vk.SendMessageAsync(message.PeerId, tariffsText, tariffsKb);
                        }
                        else if (text.Contains("🔙") || text.ToLower().Contains("назад"))
                        {
                            _state.SetState(userId, ConversationState.WaitingForSession);
                            var dateVal = _state.GetData(userId, "date") ?? DateTime.Now.ToString("dd.MM.yyyy");
                            var (sessionsText, kbJson) = await GetSessionsForDateAsync(dateVal);
                            await _vk.SendMessageAsync(message.PeerId, sessionsText, kbJson);
                        }
                        else
                        {
                            await _vk.SendMessageAsync(message.PeerId, "Выберите категорию билетов:", _kb.TicketCategoryKeyboard());
                        }
                        break;

                    case ConversationState.WaitingForPayment:
                        if (text.Contains("💳") || text.ToLower().Contains("оплат"))
                        {
                            _state.SetState(userId, ConversationState.Idle);
                            await _vk.SendMessageAsync(message.PeerId, "✅ Оплата прошла успешно! Спасибо за покупку!", _kb.MainMenu());
                        }
                        else if (text.Contains("🔙") || text.ToLower().Contains("назад"))
                        {
                            _state.SetState(userId, ConversationState.WaitingForCategory);
                            await _vk.SendMessageAsync(message.PeerId, "Выберите категорию билетов:", _kb.TicketCategoryKeyboard());
                        }
                        else
                        {
                            await _vk.SendMessageAsync(message.PeerId, "Нажмите 💳 для оплаты или 🔙 чтобы вернуться", _kb.PaymentKeyboard());
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "ProcessMessageAsync");
            }
        }

        // ======================================================
        //               РЕАЛЬНЫЕ API МЕТОДЫ
        // ======================================================

        private async Task<string> GetParkLoadAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var requestData = new { SiteID = "1" };
                var response = await client.PostAsJsonAsync("https://apigateway.nordciti.ru/v1/aqua/CurrentLoad", requestData);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warn($"Не удалось получить данные о загруженности. Статус: {response.StatusCode}");
                    return "❌ Не удалось получить данные о загруженности. Попробуйте позже 😔";
                }

                var data = await response.Content.ReadFromJsonAsync<ParkLoadResponse>(_jsonOptions);
                if (data == null)
                {
                    return "❌ Не удалось обработать данные о загруженности 😔";
                }

                string loadStatus = data.Load switch
                {
                    < 30 => "🟢 Низкая",
                    < 60 => "🟡 Средняя",
                    < 85 => "🟠 Высокая",
                    _ => "🔴 Очень высокая"
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
            catch (Exception ex)
            {
                _logger.Error(ex, "Ошибка получения данных о загруженности парка");
                return "❌ Не удалось получить информацию о загруженности. Попробуйте позже 😔";
            }
        }

        private async Task<(string message, string keyboard)> GetSessionsForDateAsync(string date)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var sessionsUrl = $"https://apigateway.nordciti.ru/v1/aqua/getSessionsAqua?date={date}";
                _logger.Info($"Запрос сеансов с: {sessionsUrl}");

                var sessionsResponse = await client.GetAsync(sessionsUrl);

                if (!sessionsResponse.IsSuccessStatusCode)
                {
                    _logger.Warn($"Не удалось получить сеансы. Статус: {sessionsResponse.StatusCode}");
                    return ($"⚠️ Ошибка при загрузке сеансов на {date}", _kb.TicketsDateKeyboard());
                }

                var sessionsJson = await sessionsResponse.Content.ReadAsStringAsync();
                _logger.Info($"Сырой ответ сеансов: {sessionsJson}");

                // Пробуем разные варианты парсинга
                try
                {
                    var sessionsData = JsonSerializer.Deserialize<JsonElement>(sessionsJson, _jsonOptions);

                    if (sessionsData.ValueKind == JsonValueKind.Array)
                    {
                        return ProcessSessionsArray(sessionsData.EnumerateArray().ToArray(), date);
                    }
                    else if (sessionsData.TryGetProperty("result", out var resultProp) && resultProp.ValueKind == JsonValueKind.Array)
                    {
                        return ProcessSessionsArray(resultProp.EnumerateArray().ToArray(), date);
                    }
                    else if (sessionsData.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                    {
                        return ProcessSessionsArray(dataProp.EnumerateArray().ToArray(), date);
                    }
                    else if (sessionsData.TryGetProperty("sessions", out var sessionsProp) && sessionsProp.ValueKind == JsonValueKind.Array)
                    {
                        return ProcessSessionsArray(sessionsProp.EnumerateArray().ToArray(), date);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.Error(ex, "Не удалось распарсить JSON сеансов");
                }

                return ($"😔 На {date} нет доступных сеансов.", _kb.TicketsDateKeyboard());
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Ошибка в GetSessionsForDateAsync для даты {date}");
                return ($"❌ Ошибка при получении сеансов", _kb.TicketsDateKeyboard());
            }
        }

        private async Task<(string message, string keyboard)> GetFormattedTariffsAsync(string date, string sessionTime, string category)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var tariffsUrl = $"https://apigateway.nordciti.ru/v1/aqua/getTariffsAqua?date={date}";
                var tariffsResponse = await client.GetAsync(tariffsUrl);

                if (!tariffsResponse.IsSuccessStatusCode)
                {
                    _logger.Warn($"Не удалось получить тарифы. Статус: {tariffsResponse.StatusCode}");
                    return ("⚠️ Ошибка при загрузке тарифов", _kb.BackKeyboard());
                }

                var tariffsJson = await tariffsResponse.Content.ReadAsStringAsync();
                _logger.Info($"[ОТЛАДКА] Сырые данные тарифов: {tariffsJson}");

                var tariffsData = JsonSerializer.Deserialize<JsonElement>(tariffsJson, _jsonOptions);

                if (!tariffsData.TryGetProperty("result", out var tariffsArray) || tariffsArray.GetArrayLength() == 0)
                {
                    return ("😔 На выбранную дату нет доступных тарифов", _kb.BackKeyboard());
                }

                string categoryTitle = category == "adult" ? "👤 ВЗРОСЛЫЕ БИЛЕТЫ" : "👶 ДЕТСКИЕ БИЛЕТЫ";
                string text = $"🎟 *{categoryTitle}*\n";
                text += $"⏰ Сеанс: {sessionTime}\n";
                text += $"📅 Дата: {date}\n\n";

                var filteredTariffs = new List<(string name, decimal price)>();
                var seenTariffs = new HashSet<string>();

                foreach (var t in tariffsArray.EnumerateArray())
                {
                    string name = t.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                    decimal price = t.TryGetProperty("Price", out var p) ? p.GetDecimal() : 0;

                    if (string.IsNullOrEmpty(name))
                        name = t.TryGetProperty("name", out var n2) ? n2.GetString() ?? "" : "";

                    if (price == 0)
                        price = t.TryGetProperty("price", out var p2) ? p2.GetDecimal() : 0;

                    string tariffKey = $"{name.ToLower()}_{price}";
                    if (seenTariffs.Contains(tariffKey)) continue;
                    seenTariffs.Add(tariffKey);

                    string nameLower = name.ToLower();
                    bool isAdult = nameLower.Contains("взрос") || nameLower.Contains("adult");
                    bool isChild = nameLower.Contains("детск") || nameLower.Contains("child") || nameLower.Contains("kids");

                    if ((category == "adult" && isAdult && !isChild) ||
                        (category == "child" && isChild && !isAdult))
                    {
                        filteredTariffs.Add((name, price));
                    }
                }

                if (filteredTariffs.Count == 0)
                {
                    text += "😔 Нет доступных билетов этой категории\n";
                    text += "💡 Попробуйте выбрать другую категорию";
                }
                else
                {
                    var groupedTariffs = filteredTariffs
                        .GroupBy(t => FormatTicketName(t.name))
                        .Select(g => g.First())
                        .OrderByDescending(t => t.price)
                        .ToList();

                    text += "💰 Стоимость билетов:\n\n";

                    foreach (var (name, price) in groupedTariffs)
                    {
                        string emoji = price > 2000 ? "💎" : price > 1000 ? "⭐" : "🎫";
                        string formattedName = FormatTicketName(name);
                        text += $"{emoji} *{formattedName}*: {price}₽\n";
                    }

                    text += $"\n💡 Примечания:\n";
                    text += $"• Детский билет - для детей от 4 до 12 лет\n";
                    text += $"• Дети до 4 лет - бесплатно (с взрослым)\n";
                    text += $"• VIP билеты включают дополнительные услуги\n";
                }

                text += $"\n\n🔗 *Купить онлайн:* yes35.ru";

                object[][] keyboardButtons = new object[][]
                {
                    new object[]
                    {
                        new { action = new { type = "open_link", link = "https://yes35.ru/aquapark/tickets", label = "🎟 Купить на сайте" } }
                    },
                    new object[]
                    {
                        new { action = new { type = "text", label = "👤 Взрослые" }, color = category == "adult" ? "positive" : "primary" },
                        new { action = new { type = "text", label = "👶 Детские" }, color = category == "child" ? "positive" : "primary" }
                    },
                    new object[]
                    {
                        new { action = new { type = "text", label = "🔙 К сеансам" }, color = "secondary" },
                        new { action = new { type = "text", label = "🔙 В начало" }, color = "negative" }
                    }
                };

                string keyboard = JsonSerializer.Serialize(new
                {
                    one_time = false,
                    inline = false,
                    buttons = keyboardButtons
                });

                return (text, keyboard);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Ошибка получения тарифов для даты {date}, сеанс {sessionTime}, категория {category}");
                return ("❌ Ошибка при получении тарифов. Попробуйте позже 😔", _kb.BackKeyboard());
            }
        }

        // ======================================================
        //               ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
        // ======================================================

        private (string message, string keyboard) ProcessSessionsArray(JsonElement[] sessionsArray, string date)
        {
            string text = $"🎟 *Доступные сеансы на {date}:*\n\n";
            var buttonsList = new List<object[]>();
            int availableSessions = 0;

            foreach (var session in sessionsArray)
            {
                try
                {
                    string sessionTime = GetSessionTime(session);
                    if (string.IsNullOrEmpty(sessionTime)) continue;

                    int placesFree = GetPlacesFree(session);
                    int placesTotal = GetPlacesTotal(session);

                    if (placesFree == 0 && placesTotal == 0)
                    {
                        placesFree = 1;
                        placesTotal = 50;
                    }

                    string availability = placesFree switch
                    {
                        0 => "🔴 Нет мест",
                        < 10 => "🔴 Мало мест",
                        < 20 => "🟡 Средняя загрузка",
                        _ => "🟢 Есть места"
                    };

                    text += $"⏰ *{sessionTime}*\n";
                    text += $"   Свободно: {placesFree}/{placesTotal} мест\n";
                    text += $"   {availability}\n\n";

                    buttonsList.Add(new[]
                    {
                        new { action = new { type = "text", label = $"⏰ {sessionTime}" }, color = "primary" }
                    });

                    availableSessions++;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Ошибка обработки элемента сеанса");
                    continue;
                }
            }

            if (availableSessions == 0)
            {
                return ($"😔 На {date} нет доступных сеансов или все заняты.", _kb.TicketsDateKeyboard());
            }

            buttonsList.Add(new[]
            {
                new { action = new { type = "text", label = "🔙 Назад" }, color = "negative" }
            });

            string keyboard = JsonSerializer.Serialize(new
            {
                one_time = true,
                inline = false,
                buttons = buttonsList.ToArray()
            });

            return (text, keyboard);
        }

        private string GetSessionTime(JsonElement session)
        {
            string[] timeFields = { "sessionTime", "SessionTime", "time", "Time", "name", "Name", "title", "Title" };

            foreach (var field in timeFields)
            {
                if (session.TryGetProperty(field, out var timeProp) && timeProp.ValueKind == JsonValueKind.String)
                {
                    var time = timeProp.GetString();
                    if (!string.IsNullOrEmpty(time))
                        return time;
                }
            }
            return "Время не указано";
        }

        private int GetPlacesFree(JsonElement session)
        {
            string[] freeFields = { "availableCount", "AvailableCount", "placesFree", "PlacesFree", "free", "Free", "available", "Available" };

            foreach (var field in freeFields)
            {
                if (session.TryGetProperty(field, out var freeProp) && freeProp.ValueKind == JsonValueKind.Number)
                {
                    return freeProp.GetInt32();
                }
            }
            return 0;
        }

        private int GetPlacesTotal(JsonElement session)
        {
            string[] totalFields = { "totalCount", "TotalCount", "placesTotal", "PlacesTotal", "total", "Total", "capacity", "Capacity" };

            foreach (var field in totalFields)
            {
                if (session.TryGetProperty(field, out var totalProp) && totalProp.ValueKind == JsonValueKind.Number)
                {
                    return totalProp.GetInt32();
                }
            }
            return 0;
        }

        private static string FormatTicketName(string name)
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

        private static bool IsTicketCategoryMessage(string msg)
        {
            var lower = msg.ToLower();
            return lower.Contains("взрос") || lower.Contains("дет") || lower.Contains("👤") || lower.Contains("👶");
        }

        private static string GetTicketCategoryFromMessage(string msg)
        {
            var lower = msg.ToLower();
            return (lower.Contains("дет") || lower.Contains("👶")) ? "child" : "adult";
        }

        private class ParkLoadResponse
        {
            public int Count { get; set; }
            public int Load { get; set; }
        }
    }
}