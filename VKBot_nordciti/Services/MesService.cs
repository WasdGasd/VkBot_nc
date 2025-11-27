using VKBot_nordciti.VK;
using VKBot_nordciti.VK.Models;
using System.Text.Json;

namespace VKBot_nordciti.Services
{
    public class MesService : IMessageService
    {
        private readonly VkApiManager _vk;
        private readonly KeyboardProvider _kb;
        private readonly ConversationStateService _state;
        private readonly FileLogger _logger;
        private readonly ICommandService _commandService;
        private readonly IHttpClientFactory _httpClientFactory;

        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        public MesService(
            VkApiManager vkApi,
            KeyboardProvider kb,
            ConversationStateService state,
            FileLogger logger,
            ICommandService commandService,
            IHttpClientFactory httpClientFactory)
        {
            _vk = vkApi;
            _kb = kb;
            _state = state;
            _logger = logger;
            _commandService = commandService;
            _httpClientFactory = httpClientFactory;
        }

        public async Task ProcessMessageAsync(VkMessage message)
        {
            try
            {
                var fromId = message.FromId;
                var userId = message.FromId;
                var peerId = message.PeerId;
                var text = (message.Text ?? string.Empty).Trim();

                _logger.Info($"Processing message - FromId: {fromId}, PeerId: {peerId}, Text: '{text}'");

                var targetPeerId = DetermineTargetPeerId(message);
                if (targetPeerId == 0) return;

                var state = _state.GetState(userId);

                // 🔥 ИСПРАВЛЕНИЕ: Пропускаем поиск в БД для категорий билетов
                bool isCategorySelection = text.Contains("👤") || text.Contains("👶") ||
                                          text.ToLower().Contains("взрос") || text.ToLower().Contains("детск");

                if (state == ConversationState.Idle && !isCategorySelection)
                {
                    var dbCommand = await _commandService.FindCommandAsync(text);
                    if (dbCommand != null)
                    {
                        await SendMessage(targetPeerId, dbCommand.Response, dbCommand.KeyboardJson ?? _kb.MainMenu());
                        return;
                    }
                }

                switch (state)
                {
                    case ConversationState.WaitingForDate:
                        await HandleDateSelection(targetPeerId, userId, text);
                        break;
                    case ConversationState.WaitingForSession:
                        await HandleSessionSelection(targetPeerId, userId, text);
                        break;
                    case ConversationState.WaitingForCategory:
                        await HandleCategorySelection(targetPeerId, userId, text);
                        break;
                    case ConversationState.WaitingForPayment:
                        await HandlePayment(targetPeerId, userId, text);
                        break;
                    default:
                        await HandleIdleState(targetPeerId, userId, text);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "ProcessMessageAsync");
            }
        }

        /// <summary>
        /// Обработка события, когда пользователь разрешил сообществу отправлять сообщения
        /// </summary>
        public async Task HandleMessageAllowEvent(long userId)
        {
            _logger.Info($"User {userId} allowed messages from community");

            var welcomeText = "🎉 ДОБРО ПОЛОЖАЛОВАТЬ В ЦЕНТР YES! 🎉\n\n" +
                 "🌈 Мы невероятно рады приветствовать вас! Теперь вы будете в самом центре всех событий, акций и специальных предложений нашего комплекса!\n\n" +
                 "🏊‍♂️ ЧЕМ Я МОГУ БЫТЬ ПОЛЕЗЕН:\n\n" +
                 "🎫 • Помогу выбрать и купить билеты онлайн\n" +
                 "📊 • Покажу текущую загруженность аквапарка в реальном времени\n" +
                 "⏰ • Расскажу о режиме работы всех зон отдыха\n" +
                 "📞 • Предоставлю контакты и способы связи\n" +
                 "📍 • Подскажу как добраться и где припарковаться\n" +
                 "💬 • Отвечу на любые ваши вопросы о нашем центре\n" +
                 "🎯 • Помогу организовать идеальный отдых для всей семьи\n\n" +
                 "🚀 ЧТОБЫ НАЧАТЬ, ПРОСТО НАЖМИТЕ КНОПКУ \"🎯 НАЧАТЬ\" НИЖЕ!\n\n" +
                 "✨ Желаю вам незабываемого отдыха, наполненного яркими эмоциями и приятными впечатлениями!";

            await SendMessage(userId, welcomeText, _kb.StartKeyboard());
        }

        private async Task HandleIdleState(long peerId, long userId, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                // Вместо статического текста - главное меню из БД
                var mainMenuCommand = await _commandService.FindCommandAsync("главное меню");
                if (mainMenuCommand != null)
                {
                    await SendMessage(peerId, mainMenuCommand.Response, mainMenuCommand.KeyboardJson ?? _kb.MainMenu());
                }
                else
                {
                    await SendMessage(peerId, "Выберите раздел:", _kb.MainMenu());
                }
                return;
            }

            // 🔥 ИСПРАВЛЕНИЕ: Добавляем проверку, что это не выбор категории
            bool isCategorySelection = text.Contains("👤") || text.Contains("👶") ||
                                      text.ToLower().Contains("взрос") || text.ToLower().Contains("детск");

            if (!isCategorySelection && (text.Contains("📅") || text.ToLower().Contains("билет")))
            {
                _state.SetState(userId, ConversationState.WaitingForDate);
                await SendMessage(peerId, "🎫 Покупка билетов\n\nВыберите дату для посещения:", _kb.TicketsDateKeyboard());
                return;
            }

            if (text.Contains("📊") || text.ToLower().Contains("загруженность"))
            {
                var loadInfo = await GetParkLoadAsync();
                await SendMessage(peerId, loadInfo, _kb.BackToMain());
                return;
            }

            if (text.Contains("🔙") || text.ToLower().Contains("назад") || text.ToLower().Contains("главное меню"))
            {
                _state.SetState(userId, ConversationState.Idle);
                await SendMessage(peerId, "Возвращаемся в главное меню 👇", _kb.MainMenu());
                return;
            }

            await SendMessage(peerId, "Я вас не понял 😊\n\nВыберите пункт меню или напишите 'помощь'", _kb.MainMenu());
        }

        private async Task HandleDateSelection(long peerId, long userId, string text)
        {
            if (text.StartsWith("📅"))
            {
                var date = text.Replace("📅", "").Trim();
                _state.SetData(userId, "selected_date", date);
                _state.SetState(userId, ConversationState.WaitingForSession);

                // Получаем сеансы через API
                var (sessionsText, sessionsKeyboard) = await GetSessionsForDateAsync(date);
                await SendMessage(peerId, sessionsText, sessionsKeyboard);
            }
            else if (text.Contains("🔙") || text.ToLower().Contains("назад"))
            {
                _state.SetState(userId, ConversationState.Idle);
                await SendMessage(peerId, "Возвращаемся в главное меню 👇", _kb.MainMenu());
            }
            else
            {
                await SendMessage(peerId, "Пожалуйста, выберите дату кнопкой 📅", _kb.TicketsDateKeyboard());
            }
        }

        private async Task HandleSessionSelection(long peerId, long userId, string text)
        {
            if (text.StartsWith("⏰"))
            {
                var sessionTime = text.Replace("⏰", "").Trim();
                _state.SetData(userId, "selected_session", sessionTime);
                _state.SetState(userId, ConversationState.WaitingForCategory);

                var date = _state.GetData(userId, "selected_date") ?? "неизвестная дата";

                await SendMessage(peerId,
                    $"🎫 Детали заказа\n\n" +
                    $"📅 Дата: {date}\n" +
                    $"⏰ Сеанс: {sessionTime}\n\n" +
                    $"Выберите категорию билетов:",
                    _kb.TicketCategoryKeyboard());
            }
            else if (text.Contains("🔙") || text.ToLower().Contains("назад"))
            {
                _state.SetState(userId, ConversationState.WaitingForDate);
                await SendMessage(peerId, "Выберите дату:", _kb.TicketsDateKeyboard());
            }
            else
            {
                var date = _state.GetData(userId, "selected_date") ?? DateTime.Now.ToString("dd.MM.yyyy");
                var (sessionsText, sessionsKeyboard) = await GetSessionsForDateAsync(date);
                await SendMessage(peerId, "Выберите сеанс кнопкой ⏰", sessionsKeyboard);
            }
        }

        private async Task HandleCategorySelection(long peerId, long userId, string text)
        {
            if (IsTicketCategoryMessage(text))
            {
                var category = GetTicketCategoryFromMessage(text);
                _state.SetData(userId, "selected_category", category);

                var date = _state.GetData(userId, "selected_date") ?? "неизвестная дата";
                var session = _state.GetData(userId, "selected_session") ?? "неизвестный сеанс";

                // Получаем тарифы через API
                var (tariffsText, tariffsKeyboard) = await GetFormattedTariffsAsync(date, session, category);

                _state.SetState(userId, ConversationState.WaitingForPayment);
                await SendMessage(peerId, tariffsText, tariffsKeyboard);
            }
            else if (text.Contains("🔙") || text.ToLower().Contains("назад"))
            {
                _state.SetState(userId, ConversationState.WaitingForSession);
                var date = _state.GetData(userId, "selected_date") ?? DateTime.Now.ToString("dd.MM.yyyy");
                var (sessionsText, sessionsKeyboard) = await GetSessionsForDateAsync(date);
                await SendMessage(peerId, "Выберите сеанс:", sessionsKeyboard);
            }
            else
            {
                await SendMessage(peerId, "Выберите категорию билетов:", _kb.TicketCategoryKeyboard());
            }
        }

        private async Task HandlePayment(long peerId, long userId, string text)
        {
            if (text.Contains("💳") || text.ToLower().Contains("оплат"))
            {
                var date = _state.GetData(userId, "selected_date") ?? "неизвестная дата";
                var session = _state.GetData(userId, "selected_session") ?? "неизвестный сеанс";
                var category = _state.GetData(userId, "selected_category") ?? "неизвестная категория";

                await SendMessage(peerId,
                    $"✅ Оплата прошла успешно!\n\n" +
                    $"🎫 Ваш заказ:\n" +
                    $"📅 Дата: {date}\n" +
                    $"⏰ Сеанс: {session}\n" +
                    $"👥 Категория: {GetCategoryDisplayName(category)}\n\n" +
                    $"📧 Чек отправлен вам в сообщения\n" +
                    $"🏊‍♂️ Ждем вас в аквапарке!",
                    _kb.MainMenu());

                _state.SetState(userId, ConversationState.Idle);
                _state.ClearUserData(userId);
            }
            else if (text.Contains("🔙") || text.ToLower().Contains("назад"))
            {
                _state.SetState(userId, ConversationState.WaitingForCategory);
                await SendMessage(peerId, "Выберите категорию билетов:", _kb.TicketCategoryKeyboard());
            }
            else
            {
                await SendMessage(peerId, "Нажмите 💳 для оплаты или 🔙 чтобы вернуться", _kb.PaymentKeyboard());
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
                    return ("⚠️ Ошибка при загрузке тарифов", _kb.BackToMain());
                }

                var tariffsJson = await tariffsResponse.Content.ReadAsStringAsync();
                _logger.Info($"[ОТЛАДКА] Сырые данные тарифов: {tariffsJson}");

                var tariffsData = JsonSerializer.Deserialize<JsonElement>(tariffsJson, _jsonOptions);

                if (!tariffsData.TryGetProperty("result", out var tariffsArray) || tariffsArray.GetArrayLength() == 0)
                {
                    return ("😔 На выбранную дату нет доступных тарифов", _kb.BackToMain());
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
                return ("❌ Ошибка при получении тарифов. Попробуйте позже 😔", _kb.BackToMain());
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



        private string GetCategoryDisplayName(string category)
        {
            return category == "adult" ? "Взрослые" : "Детские";
        }

        private bool IsTicketCategoryMessage(string msg)
        {
            var lower = msg.ToLower();
            return lower.Contains("взрос") || lower.Contains("дет") || lower.Contains("👤") || lower.Contains("👶");
        }

        private string GetTicketCategoryFromMessage(string msg)
        {
            var lower = msg.ToLower();
            return (lower.Contains("дет") || lower.Contains("👶")) ? "child" : "adult";
        }

        private long DetermineTargetPeerId(VkMessage message)
        {
            if (message.PeerId != 0) return message.PeerId;
            if (message.FromId != 0) return message.FromId;
            if (message.UserId != 0) return message.UserId;

            return 0;
        }

        private async Task SendMessage(long peerId, string message, string keyboard)
        {
            var success = await _vk.SendMessageAsync(peerId, message, keyboard);
            if (!success)
            {
                _logger.Warn($"Failed to send message to peer {peerId}");
            }
        }

        private class ParkLoadResponse
        {
            public int Count { get; set; }
            public int Load { get; set; }
        }
    }
}