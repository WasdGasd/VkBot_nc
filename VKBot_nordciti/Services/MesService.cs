using Microsoft.EntityFrameworkCore;
using Models;
using System.Text.Json;
using VK;
using VK.Models;
using VKBD_nc.Data;
using VKBD_nc.models;
using BotServices;

namespace BotServices
{
    public class MesService : IMessageService
    {
        // Зависимости сервиса
        private readonly VkApiManager _vk;
        private readonly KeyboardProvider _kb;
        private readonly ConversationStateService _state;
        private readonly FileLogger _logger;
        private readonly CommandService _commandService;
        private readonly BotDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;

        // Настройки для десериализации JSON
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

        /// Основной метод обработки входящих сообщений
        public async Task ProcessMessageAsync(VkMessage message)
        {
            try
            {
                var userId = message.UserId;
                var text = (message.Text ?? string.Empty).Trim();

                _logger.Info($"Received from {userId}: {text}");

                // Получаем текущее состояние диалога
                var state = _state.GetState(userId);

                // ======================================================
                //                КОМАНДЫ ИЗ БАЗЫ ДАННЫХ
                // ======================================================

                // Пытаемся найти команду в базе данных по тексту сообщения
                var dbCommand = await _commandService.FindCommandAsync(text);

                // ЕСЛИ НАШЛИ КОМАНДУ В БД - ОТПРАВЛЯЕМ ОТВЕТ И ВЫХОДИМ
                if (dbCommand != null)
                {
                    await _vk.SendMessageAsync(
                        message.PeerId,
                        dbCommand.Response,           // Текст ответа из БД
                        dbCommand.KeyboardJson        // Клавиатура из БД  
                    );
                    return; // ВАЖНО: выходим, чтобы не обрабатывать дальше
                }

                // ======================================================
                // УНИВЕРСАЛЬНАЯ ОБРАБОТКА КНОПОК ВОЗВРАТА
                // ======================================================

                // Обработка различных вариантов команды "назад"
                if (text.Contains("🔙 В начало") || text.Contains("🔙 Главное меню") ||
                    text.ToLower().Contains("главное меню") || text.ToLower().Contains("в начало"))
                {
                    _state.SetState(userId, ConversationState.Idle);
                    await _vk.SendMessageAsync(message.PeerId, "Возвращаемся в главное меню 👇", _kb.MainMenu());
                    return;
                }

                // Возврат к выбору сеансов
                if (text.Contains("🔙 К сеансам") || text.ToLower().Contains("к сеансам"))
                {
                    var date = _state.GetData(userId, "date") ?? DateTime.Now.ToString("dd.MM.yyyy");
                    var (sessionsText, kbJson) = await GetSessionsForDateAsync(date);
                    _state.SetState(userId, ConversationState.WaitingForSession);
                    await _vk.SendMessageAsync(message.PeerId, sessionsText, kbJson);
                    return;
                }

                if (text.Contains("🔙 К информации") || text.ToLower().Contains("к информации"))
                {
                    _state.SetState(userId, ConversationState.Idle);
                    await _vk.SendMessageAsync(message.PeerId, "Выберите нужный раздел информации 👇", _kb.InfoMenu());
                    return;
                }




                    // ======================================================
                    // 2. ПОТОМ - API ДАННЫЕ (динамические через состояния)
                    // ======================================================

                    // Обработка в зависимости от текущего состояния диалога
                    switch (state)
                {
                    case ConversationState.Idle:
                        // Главное меню - обработка основных команд
                        if (text.Contains("🔙") || text.ToLower().Contains("назад"))
                        {
                            await _vk.SendMessageAsync(message.PeerId, "Вы уже в главном меню 👇", _kb.MainMenu());
                            break;
                        }

                        // Запрос загруженности парка
                        if (text.Contains("📊") || text.ToLower().Contains("загруженность"))
                        {
                            var loadInfo = await GetParkLoadAsync();
                            await _vk.SendMessageAsync(message.PeerId, loadInfo, _kb.BackToMain());
                        }
                        // Начало процесса покупки билетов
                        else if (text.Contains("📅") || text.ToLower().Contains("билеты") || text.ToLower().Contains("билет"))
                        {
                            _state.SetState(userId, ConversationState.WaitingForDate);
                            await _vk.SendMessageAsync(message.PeerId, "Выберите дату для посещения:", _kb.TicketsDateKeyboard());
                        }
                        // Меню информации
                        else if (text.Contains("ℹ️") || text.ToLower().Contains("информация") || text.ToLower().Contains("инфо"))
                        {
                            await _vk.SendMessageAsync(message.PeerId, "Выберите нужный раздел информации 👇", _kb.InfoMenu());
                        }
                        else
                        {
                            // Сообщение по умолчанию, если команда не распознана
                            await _vk.SendMessageAsync(message.PeerId, "Я вас не понял — выберите пункт меню 👇", _kb.MainMenu());
                        }
                        break;

                    case ConversationState.WaitingForDate:
                        // Ожидание выбора даты
                        if (text.StartsWith("📅"))
                        {
                            var date = text.Replace("📅", "").Trim();
                            _state.SetData(userId, "date", date);
                            _state.SetState(userId, ConversationState.WaitingForSession);

                            // API: получение доступных сеансов для выбранной даты
                            var (sessionsText, keyboardJson) = await GetSessionsForDateAsync(date);
                            await _vk.SendMessageAsync(message.PeerId, sessionsText, keyboardJson);
                        }
                        else if (text.Contains("🔙") || text.ToLower().Contains("назад"))
                        {
                            _state.SetState(userId, ConversationState.Idle);
                            await _vk.SendMessageAsync(message.PeerId, "Возвращаемся в главное меню 👇", _kb.MainMenu());
                        }
                        else
                        {
                            await _vk.SendMessageAsync(message.PeerId, "Пожалуйста, выберите дату кнопкой 📅", _kb.TicketsDateKeyboard());
                        }
                        break;

                    case ConversationState.WaitingForSession:
                        // Ожидание выбора сеанса
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
                        // Ожидание выбора категории билетов
                        if (IsTicketCategoryMessage(text))
                        {
                            var category = GetTicketCategoryFromMessage(text);
                            _state.SetData(userId, "category", category);

                            var date = _state.GetData(userId, "date") ?? "неизвестная дата";
                            var sessionSelected = _state.GetData(userId, "session") ?? "неизвестный сеанс";

                            // API: получение тарифов для выбранных параметров
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
                        // Ожидание оплаты (симуляция)
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

                // ======================================================
                // ОБРАБОТКА ИНФОРМАЦИОННЫХ КОМАНД ИЗ ЛЮБОГО СОСТОЯНИЯ
                // ======================================================

                // Контактная информация
                if (text.Contains("📞") || text.ToLower().Contains("контакт") || text.ToLower().Contains("телефон"))
                {
                    await _vk.SendMessageAsync(message.PeerId, GetContacts(), _kb.ContactsKeyboard());
                    return;
                }

                // Информация о местоположении
                if (text.Contains("📍") || text.ToLower().Contains("адрес") || text.ToLower().Contains("как добраться") || text.ToLower().Contains("местоположение"))
                {
                    await _vk.SendMessageAsync(message.PeerId, GetLocationInfo(), _kb.LocationKeyboard());
                    return;
                }

                // Режим работы
                if (text.Contains("⏰") || text.ToLower().Contains("режим работы") || text.ToLower().Contains("время работы"))
                {
                    await _vk.SendMessageAsync(message.PeerId, GetWorkingHours(), _kb.WorkingHoursKeyboard());
                    return;
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

        /// Получение информации о загруженности аквапарка
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

                // Определение уровня загруженности
                string loadStatus = data.Load switch
                {
                    < 30 => "🟢 Низкая загруженность",
                    < 60 => "🟡 Средняя загруженность",
                    < 85 => "🟠 Высокая загруженность",
                    _ => "🔴 Очень высокая загруженность"
                };

                // Рекомендации в зависимости от загруженности
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

        /// Получение списка сеансов для указанной даты
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

                // Пробуем разные варианты парсинга JSON ответа
                try
                {
                    var sessionsData = JsonSerializer.Deserialize<JsonElement>(sessionsJson, _jsonOptions);

                    // Проверяем различные возможные структуры ответа
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

        /// Получение отформатированных тарифов для выбранных параметров
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

                // Формируем заголовок в зависимости от категории
                string categoryTitle = category == "adult" ? "👤 ВЗРОСЛЫЕ БИЛЕТЫ" : "👶 ДЕТСКИЕ БИЛЕТЫ";
                string text = $"🎟 *{categoryTitle}*\n";
                text += $"⏰ Сеанс: {sessionTime}\n";
                text += $"📅 Дата: {date}\n\n";

                var filteredTariffs = new List<(string name, decimal price)>();
                var seenTariffs = new HashSet<string>();

                // Обрабатываем каждый тариф из ответа API
                foreach (var t in tariffsArray.EnumerateArray())
                {
                    string name = t.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                    decimal price = t.TryGetProperty("Price", out var p) ? p.GetDecimal() : 0;

                    // Альтернативные названия свойств
                    if (string.IsNullOrEmpty(name))
                        name = t.TryGetProperty("name", out var n2) ? n2.GetString() ?? "" : "";

                    if (price == 0)
                        price = t.TryGetProperty("price", out var p2) ? p2.GetDecimal() : 0;

                    // Убираем дубликаты
                    string tariffKey = $"{name.ToLower()}_{price}";
                    if (seenTariffs.Contains(tariffKey)) continue;
                    seenTariffs.Add(tariffKey);

                    // Фильтруем по категории
                    string nameLower = name.ToLower();
                    bool isAdult = nameLower.Contains("взрос") || nameLower.Contains("adult");
                    bool isChild = nameLower.Contains("детск") || nameLower.Contains("child") || nameLower.Contains("kids");

                    if ((category == "adult" && isAdult && !isChild) ||
                        (category == "child" && isChild && !isAdult))
                    {
                        filteredTariffs.Add((name, price));
                    }
                }

                // Формируем текст ответа в зависимости от наличия тарифов
                if (filteredTariffs.Count == 0)
                {
                    text += "😔 Нет доступных билетов этой категории\n";
                    text += "💡 Попробуйте выбрать другую категорию";
                }
                else
                {
                    // Группируем и сортируем тарифы
                    var groupedTariffs = filteredTariffs
                        .GroupBy(t => FormatTicketName(t.name))
                        .Select(g => g.First())
                        .OrderByDescending(t => t.price)
                        .ToList();

                    text += "💰 Стоимость билетов:\n\n";

                    // Добавляем каждый тариф в текст
                    foreach (var (name, price) in groupedTariffs)
                    {
                        string emoji = price > 2000 ? "💎" : price > 1000 ? "⭐" : "🎫";
                        string formattedName = FormatTicketName(name);
                        text += $"{emoji} *{formattedName}*: {price}₽\n";
                    }

                    // Добавляем примечания
                    text += $"\n💡 Примечания:\n";
                    text += $"• Детский билет - для детей от 4 до 12 лет\n";
                    text += $"• Дети до 4 лет - бесплатно (с взрослым)\n";
                    text += $"• VIP билеты включают дополнительные услуги\n";
                }

                text += $"\n\n🔗 *Купить онлайн:* yes35.ru";

                // Формируем клавиатуру для ответа
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
                        new { action = new { type = "text", label = "🔙 Главное меню" }, color = "negative" }
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

        /// Обработка массива сеансов и формирование ответа
        private (string message, string keyboard) ProcessSessionsArray(JsonElement[] sessionsArray, string date)
        {
            string text = $"🎟 Доступные сеансы на {date}:\n\n";
            var buttonsList = new List<object[]>();
            int availableSessions = 0;

            // Обрабатываем каждый сеанс
            foreach (var session in sessionsArray)
            {
                try
                {
                    string sessionTime = GetSessionTime(session);
                    if (string.IsNullOrEmpty(sessionTime)) continue;

                    int placesFree = GetPlacesFree(session);
                    int placesTotal = GetPlacesTotal(session);

                    // Если данные о местах отсутствуют, используем значения по умолчанию
                    if (placesFree == 0 && placesTotal == 0)
                    {
                        placesFree = 1;
                        placesTotal = 50;
                    }

                    // Определяем статус доступности
                    string availability = placesFree switch
                    {
                        0 => "🔴 Нет мест",
                        < 10 => "🔴 Мало мест",
                        < 20 => "🟡 Средняя загрузка",
                        _ => "🟢 Есть места"
                    };

                    // Добавляем информацию о сеансе в текст
                    text += $"⏰ *{sessionTime}*\n";
                    text += $"   Свободно: {placesFree}/{placesTotal} мест\n";
                    text += $"   {availability}\n\n";

                    // Добавляем кнопку для выбора сеанса
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

            // Если нет доступных сеансов
            if (availableSessions == 0)
            {
                return ($"😔 На {date} нет доступных сеансов или все заняты.", _kb.TicketsDateKeyboard());
            }

            // Добавляем кнопку "Назад"
            buttonsList.Add(new[]
            {
                new { action = new { type = "text", label = "🔙 Назад" }, color = "negative" }
            });

            // Формируем клавиатуру
            string keyboard = JsonSerializer.Serialize(new
            {
                one_time = true,
                inline = false,
                buttons = buttonsList.ToArray()
            });

            return (text, keyboard);
        }

        /// Извлечение времени сеанса из JSON элемента
        private string GetSessionTime(JsonElement session)
        {
            string[] timeFields = { "sessionTime", "SessionTime", "time", "Time", "name", "Name", "title", "Title" };

            // Пробуем разные возможные названия полей
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

        /// Извлечение количества свободных мест из JSON элемента
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
        /// Извлечение общего количества мест из JSON элемента
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

        /// Форматирование названия билета для красивого отображения
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

            // Обработка VIP билетов
            if (formatted.StartsWith("VIP") || formatted.StartsWith("Вип"))
            {
                formatted = "VIP" + formatted.Substring(3).Trim();
            }

            // Убираем двойные пробелы
            while (formatted.Contains("  "))
            {
                formatted = formatted.Replace("  ", " ");
            }

            return string.IsNullOrEmpty(formatted) ? "Стандартный" : formatted;
        }

        /// Проверка, является ли сообщение выбором категории билетов
        private static bool IsTicketCategoryMessage(string msg)
        {
            var lower = msg.ToLower();
            return lower.Contains("взрос") || lower.Contains("дет") || lower.Contains("👤") || lower.Contains("👶");
        }

        /// Определение категории билета из текста сообщения
        private static string GetTicketCategoryFromMessage(string msg)
        {
            var lower = msg.ToLower();
            return (lower.Contains("дет") || lower.Contains("👶")) ? "child" : "adult";
        }

        // ======================================================
        //               ИНФОРМАЦИОННЫЕ МЕТОДЫ
        // ======================================================

        private static string GetWorkingHours() => "🏢 Режим работы:\n\n⏰ Ежедневно: 10:00 - 22:00\n📅 Без выходных";

        private static string GetContacts() => "📞 Контакты:\n" +
            "📱 Телефон для связи:\n" +
            "• Основной: (8172) 33-06-06\n" +
            "• Ресторан: 8-800-200-67-71\n\n" +

            "📧 Электронная почта:\n" +
                    "yes@yes35.ru\n\n" +

            "🌐 Мы в соцсетях:\n" +
                    "ВКонтакте: vk.com/yes35\n" +
                    "Telegram: t.me/CentreYES35\n" +
                    "WhatsApp: ссылка в профиле\n\n" +

                    "⏰ Часы работы call-центра:\n" +
                    "🕙 09:00 - 22:00";

        private static string GetLocationInfo() => "📍 *Центр YES - Как добраться*\n\n" +

                 "🏠 Адрес:\n" +
                 "Вологодская область, М.О. Вологодский\n" +
                 "д. Брагино, тер. Центр развлечений\n\n" +

                 "🚗 На автомобиле:\n" +
                 "• По федеральной трассе А114 'Вологда - Новая Ладога'\n" +
                 "• На повороте к Центру на трассе установлен заметный баннер-указатель.\n" +
                 "• 💰 Бесплатная парковка* на территории\n\n" +

                 "🚍 Общественный транспорт:\n" +
                 "• От автовокзала Вологды (площадь Бабушкина, 10) ходят ежедневные и регулярные рейсовые автобусы\n" +

                 "🗺 Координаты для навигатора:\n" +
                 "59.1858° с.ш., 39.7685° в.д.\n\n" +

                 "⏱ Расстояния:\n" +
                 "• От г. Вологды: ~34 км\n" +
                 "• От г. Череповца: ~107 км\n" +


                 "🏞 Расположение:\n" +
                 "Круглогодичный развлекательный комплекс\n" +
                 "в живописной лесной зоне под Вологдой";

        /// Модель для десериализации ответа о загруженности парка
        private class ParkLoadResponse
        {
            public int Count { get; set; }
            public int Load { get; set; }
        }
    }
}