using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Runtime;
using System.Text.Json;
using System.Text.Json.Serialization;
using VKB_WA.Services;
using VKBD_nc.Data;
using VKBD_nc.Models;

namespace VKBot.Services
{
    public class BotService : BackgroundService
    {
        private readonly ILogger<BotService> _log;
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _http;
        private readonly VkSettings _vk;
        private readonly ErrorLogger _errors;
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly ConcurrentDictionary<long, (string date, string session)> _userSelectedData = new();

        private readonly ConcurrentDictionary<long, DateTime> _userLastActivity = new();
        private int _totalMessagesProcessed = 0;
        private readonly Dictionary<string, int> _commandUsage = new();
        private readonly DateTime _startTime = DateTime.Now;

        public BotService(ILogger<BotService> log, IHttpClientFactory http,
                        IOptions<VkSettings> vkOptions, ErrorLogger errors,
                        ApplicationDbContext context)
        {
            _log = log;
            _http = http;
            _vk = vkOptions.Value;
            _errors = errors;
            _context = context;

        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (string.IsNullOrEmpty(_vk.AccessToken))
            {
                _log.LogError("Токен VK не настроен. Установите в appsettings.json или переменных окружения.");
                return;
            }

            if (string.IsNullOrEmpty(_vk.GroupId))
            {
                _log.LogWarning("GroupId VK не настроен. LongPoll может не работать.");
            }

            var client = _http.CreateClient();

            try
            {
                _log.LogInformation("Получение LongPoll сервера...");

                var serverResp = await client.GetFromJsonAsync<LongPollServerResponse>(
                    $"https://api.vk.com/method/groups.getLongPollServer?group_id={_vk.GroupId}&access_token={_vk.AccessToken}&v={_vk.ApiVersion}",
                    _jsonOptions, stoppingToken);

                if (serverResp?.Response == null)
                {
                    _log.LogError("Не удалось получить ответ от LongPoll сервера.");
                    return;
                }

                string server = serverResp.Response.Server;
                string key = serverResp.Response.Key;
                string ts = serverResp.Response.Ts;

                _log.LogInformation("LongPoll инициализирован. Ожидание событий...");

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var pollStr = await client.GetStringAsync($"{server}?act=a_check&key={key}&ts={ts}&wait=25", stoppingToken);
                        var poll = JsonSerializer.Deserialize<LongPollUpdate>(pollStr, _jsonOptions);
                        if (poll == null) continue;

                        if (!string.IsNullOrEmpty(poll.Ts)) ts = poll.Ts;

                        if (poll.Failed.HasValue && poll.Failed.Value != 0)
                        {
                            _log.LogWarning("LongPoll ошибка ({Failed}). Refreshing ts...", poll.Failed.Value);
                            var serverRespRefresh = await client.GetFromJsonAsync<LongPollServerResponse>(
                                $"https://api.vk.com/method/groups.getLongPollServer?group_id={_vk.GroupId}&access_token={_vk.AccessToken}&v={_vk.ApiVersion}",
                                _jsonOptions, stoppingToken);
                            if (serverRespRefresh?.Response != null)
                            {
                                server = serverRespRefresh.Response.Server;
                                key = serverRespRefresh.Response.Key;
                                ts = serverRespRefresh.Response.Ts;
                            }
                            continue;
                        }

                        if (poll.Updates?.Length > 0)
                        {
                            foreach (var u in poll.Updates)
                            {
                                await ProcessUpdateAsync(u, client);
                            }
                        }
                    }
                    catch (TaskCanceledException) { break; }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Ошибка цикла LongPoll");
                        await _errors.LogErrorAsync(ex, "CRITICAL", additional: new { Component = "MainLoop" });
                        await Task.Delay(3000, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogCritical(ex, "Ошибка инициализации бота");
                await _errors.LogErrorAsync(ex, "FATAL", additional: new { Component = "Initialization" });
            }
        }

        private async Task ProcessUpdateAsync(UpdateItem update, HttpClient client)
        {
            try
            {
                if (update.Type == "message_allow" && update.Object?.UserId != null)
                {
                    var uid = update.Object.UserId.Value;
                    var welcome = GenerateWelcomeText();
                    var keyboard = GenerateWelcomeKeyboard();
                    await SendMessageAsync(client, uid, welcome, keyboard);
                    return;
                }

                if (update.Type == "message_new" && update.Object?.Message != null)
                {
                    await ProcessMessageAsync(update.Object.Message, client);
                }
            }
            catch (Exception ex)
            {
                long? uid = update.Object?.UserId ?? update.Object?.Message?.FromId;
                await _errors.LogErrorAsync(ex, "ERROR", uid, additional: new { Update = update });
            }
        }

        private async Task ProcessMessageAsync(MessageItem message, HttpClient client)
        {
            var msg = message.Text ?? string.Empty;
            var userId = message.FromId;


            // Сбор статистики
            Interlocked.Increment(ref _totalMessagesProcessed);
            _userLastActivity[userId] = DateTime.Now;

            var command = GetCommandFromMessage(msg);
            lock (_commandUsage)
            {
                if (_commandUsage.ContainsKey(command))
                    _commandUsage[command]++;
                else
                    _commandUsage[command] = 1;
            }

            _log.LogInformation("Сообщение от {user}: {text}", userId, msg);

            string reply = string.Empty;
            string? keyboard = null;

            try
            {
                if (IsTicketCategoryMessage(msg))
                {
                    if (_userSelectedData.TryGetValue(userId, out var td))
                    {
                        var category = GetTicketCategoryFromMessage(msg);
                        var (m, k) = await GetFormattedTariffsAsync(client, td.date, td.session, category);
                        reply = m;
                        keyboard = k;
                        _userSelectedData.AddOrUpdate(userId, (td.date, td.session), (key, old) => (td.date, td.session));
                    }
                    else
                    {
                        reply = "Сначала выберите дату и сеанс 📅";
                        keyboard = TicketsDateKeyboard();
                    }
                }
                else
                {
                    switch (msg.ToLowerInvariant())
                    {
                        case "/start":
                        case "начать":
                        case "🚀 начать":
                            reply = "Добро пожаловать! Выберите пункт 👇";
                            keyboard = MainMenuKeyboard();
                            break;
                        case "информация":
                        case "ℹ️ информация":
                            reply = "Выберите интересующую информацию 👇";
                            keyboard = InfoMenuKeyboard();
                            break;
                        case "время работы":
                        case "⏰ время работы":
                            reply = GetWorkingHours();
                            break;
                        case "контакты":
                        case "📞 контакты":
                            reply = GetContacts();
                            break;
                        case "🔙 назад":
                        case "назад":
                            reply = "Главное меню:";
                            keyboard = MainMenuKeyboard();
                            _userSelectedData.TryRemove(userId, out _);
                            break;
                        case "🔙 к сеансам":
                            if (_userSelectedData.TryGetValue(userId, out var sd))
                            {
                                var (m, k) = await GetSessionsForDateAsync(client, sd.date);
                                reply = m; keyboard = k;
                            }
                            else { reply = "Выберите дату для сеанса:"; keyboard = TicketsDateKeyboard(); }
                            break;
                        case "🔙 в начало":
                            reply = "Главное меню:";
                            keyboard = MainMenuKeyboard();
                            _userSelectedData.TryRemove(userId, out _);
                            break;
                        case "🎟 купить билеты":
                        case "билеты":
                            reply = "Выберите дату для сеанса:";
                            keyboard = TicketsDateKeyboard();
                            break;
                        case "📊 загруженность":
                        case "загруженность":
                            reply = await GetParkLoadAsync(client);
                            break;
                        default:
                            if (msg.StartsWith("📅") || msg.StartsWith("⏰"))
                            {
                                if (msg.StartsWith("📅"))
                                {
                                    var date = msg.Replace("📅", "").Trim();
                                    var (m, k) = await GetSessionsForDateAsync(client, date);
                                    reply = m; keyboard = k;
                                    _userSelectedData.AddOrUpdate(userId, (date, ""), (key, old) => (date, ""));
                                }
                                else if (msg.StartsWith("⏰"))
                                {
                                    var session = msg.Replace("⏰", "").Trim();
                                    if (!_userSelectedData.TryGetValue(userId, out var cur))
                                    {
                                        reply = "Сначала выберите дату 📅";
                                        keyboard = TicketsDateKeyboard();
                                    }
                                    else
                                    {
                                        _userSelectedData[userId] = (cur.date, session);
                                        reply = $"🎟 *Сеанс: {session} ({cur.date})*\n\nВыберите категорию билетов:";
                                        keyboard = TicketCategoryKeyboard();
                                    }
                                }
                            }
                            else
                            {
                                reply = "Я вас не понял, попробуйте еще раз 😅";
                            }
                            break;
                    }
                }

                await SendMessageAsync(client, userId, reply, keyboard);
            }
            catch (Exception ex)
            {
                await _errors.LogErrorAsync(ex, "ERROR", userId, additional: new { Message = msg, HasSelected = _userSelectedData.ContainsKey(userId) });
                var errMsg = "Произошла ошибка при обработке запроса. Мы уже работаем над этим! 🛠️";
                await SendMessageAsync(client, userId, errMsg);
            }
        }

        private async Task SendMessageAsync(HttpClient client, long userId, string message, string? keyboardJson = null)
        {
            var token = _vk.AccessToken;
            var v = _vk.ApiVersion ?? "5.131";

            var parameters = new List<KeyValuePair<string, string>>
            {
                new("user_id", userId.ToString()),
                new("random_id", Guid.NewGuid().GetHashCode().ToString()),
                new("message", message),
                new("access_token", token!),
                new("v", v)
            };

            if (!string.IsNullOrEmpty(keyboardJson) && keyboardJson != "{}")
            {
                parameters.Add(new KeyValuePair<string, string>("keyboard", keyboardJson ?? ""));
            }

            var content = new FormUrlEncodedContent(parameters);

            try
            {
                var response = await client.PostAsync("https://api.vk.com/method/messages.send", content);
                if (!response.IsSuccessStatusCode)
                {
                    _log.LogWarning("Не удалось отправить сообщение пользователю {UserId}. Статус: {StatusCode}", userId, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Ошибка отправки сообщения пользователю {UserId}", userId);
                await _errors.LogErrorAsync(ex, "ERROR", userId, additional: new { Action = "SendMessage" });
            }
        }

        // ---------------------- Helper methods ----------------------

        private static bool IsTicketCategoryMessage(string message)
        {
            var lowerMsg = message.ToLowerInvariant();
            return lowerMsg.Contains("взрос") || lowerMsg.Contains("детск") || lowerMsg.Contains("adult") || lowerMsg.Contains("child") ||
                   lowerMsg.Contains("kids") || lowerMsg == "👤" || lowerMsg == "👶" || lowerMsg == "взрослые" || lowerMsg == "детские";
        }

        private static string GetTicketCategoryFromMessage(string message)
        {
            var lowerMsg = message.ToLowerInvariant();
            return (lowerMsg.Contains("взрос") || lowerMsg.Contains("adult") || lowerMsg == "👤") ? "adult" : "child";
        }

        private static string MainMenuKeyboard() => JsonSerializer.Serialize(new
        {
            one_time = false,
            inline = false,
            buttons = new[] {
                new[] {
                    new { action = new { type = "text", label = "ℹ️ Информация" }, color = "primary" },
                    new { action = new { type = "text", label = "🎟 Купить билеты" }, color = "positive" },
                    new { action = new { type = "text", label = "📊 Загруженность" }, color = "secondary" }
                }
            }
        });

        private static string InfoMenuKeyboard() => JsonSerializer.Serialize(new
        {
            one_time = false,
            inline = false,
            buttons = new[] {
                new[] {
                    new { action = new { type = "text", label = "⏰ Время работы" }, color = "primary" },
                    new { action = new { type = "text", label = "📞 Контакты" }, color = "primary" }
                },
                new[] {
                    new { action = new { type = "text", label = "🔙 Назад" }, color = "negative" }
                }
            }
        });

        private static string TicketsDateKeyboard()
        {
            var buttons = new List<object[]>();
            var row1 = new List<object>();

            for (int i = 0; i < 3; i++)
            {
                string dateStr = DateTime.Now.AddDays(i).ToString("dd.MM.yyyy");
                row1.Add(new { action = new { type = "text", label = $"📅 {dateStr}" }, color = "primary" });
            }
            buttons.Add(row1.ToArray());

            var row2 = new List<object>();
            for (int i = 3; i < 5; i++)
            {
                string dateStr = DateTime.Now.AddDays(i).ToString("dd.MM.yyyy");
                row2.Add(new { action = new { type = "text", label = $"📅 {dateStr}" }, color = "primary" });
            }
            buttons.Add(row2.ToArray());

            buttons.Add(new object[] { new { action = new { type = "text", label = "🔙 Назад" }, color = "negative" } });

            return JsonSerializer.Serialize(new { one_time = true, inline = false, buttons = buttons });
        }

        private static string TicketCategoryKeyboard() => JsonSerializer.Serialize(new
        {
            one_time = true,
            inline = false,
            buttons = new[]
            {
                new[] {
                    new { action = new { type = "text", label = "👤 Взрослые билеты" }, color = "primary" },
                    new { action = new { type = "text", label = "👶 Детские билеты" }, color = "positive" }
                },
                new[] {
                    new { action = new { type = "text", label = "🔙 Назад" }, color = "negative" }
                }
            }
        });

        private static string BackKeyboard() => JsonSerializer.Serialize(new
        {
            one_time = true,
            inline = false,
            buttons = new[] { new[] { new { action = new { type = "text", label = "🔙 Назад" }, color = "negative" } } }
        });

        private static string GenerateWelcomeKeyboard() => JsonSerializer.Serialize(new
        {
            one_time = true,
            inline = false,
            buttons = new[] { new[] { new { action = new { type = "text", label = "🚀 Начать" }, color = "positive" } } }
        });

        private string GenerateWelcomeText() => string.Join("\n", new[] {
            "🌊 ДОБРО ПОЛОЖАЛОВАТЬ В ЦЕНТР YES!\n\n" +
"Я ваш персональный помощник для организации незабываемого отдыха! 🎯\n\n" +

"🎟 УМНАЯ ПОКУПКА БИЛЕТОВ\n" +
"- Выбор идеальной даты посещения\n" +
"- Подбор сеанса с учетом загруженности\n" +
"- Раздельный просмотр тарифов: взрослые/детские\n" +
"- Прозрачные цены без скрытых комиссий\n" +
"- Мгновенный переход к безопасной оплате онлайн\n\n" +

"📊 ОНЛАЙН-МОНИТОРИНГ ЗАГРУЖЕННОСТИ\n" +
"- Реальная картина посещаемости в реальном времени\n" +
"- Точное количество гостей в аквапарке\n" +
"- Процент заполненности для комфортного планирования\n" +
"- Рекомендации по лучшему времени для визита\n\n" +

"ℹ️ ПОЛНАЯ ИНФОРМАЦИЯ О ЦЕНТРЕ\n" +
"- Актуальное расписание всех зон и аттракционов\n" +
"- Контакты и способы связи с администрацией\n" +
"- Информация о временно закрытых объектах\n" +
"- Все необходимое для комфортного планирования\n\n" +

"🚀 Начните прямо сейчас!\n" +
"Выберите раздел в меню ниже, и я помогу организовать ваш идеальный визит! ✨\n\n" +
"💫 Центр YES - где рождаются воспоминания!"
        });

        private async Task<(string message, string keyboard)> GetSessionsForDateAsync(HttpClient client, string date)
        {
            try
            {
                var sessionsUrl = $"https://apigateway.nordciti.ru/v1/aqua/getSessionsAqua?date={date}";
                _log.LogInformation("Запрос сеансов с: {Url}", sessionsUrl);

                var sessionsResponse = await client.GetAsync(sessionsUrl);

                if (!sessionsResponse.IsSuccessStatusCode)
                {
                    _log.LogWarning("Не удалось получить сеансы. Статус: {StatusCode}", sessionsResponse.StatusCode);
                    return ($"⚠️ Ошибка при загрузке сеансов на {date}", TicketsDateKeyboard());
                }

                var sessionsJson = await sessionsResponse.Content.ReadAsStringAsync();
                _log.LogInformation("Сырой ответ сеансов: {Json}", sessionsJson);

                // Пробуем разные варианты парсинга
                try
                {
                    // Сначала пробуем распарсить как массив
                    var sessionsArray = JsonSerializer.Deserialize<JsonElement[]>(sessionsJson, _jsonOptions);
                    if (sessionsArray != null && sessionsArray.Length > 0)
                    {
                        return ProcessSessionsArray(sessionsArray, date);
                    }
                }
                catch (JsonException) { }

                try
                {
                    // Пробуем распарсить как объект с свойством result
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
                    else
                    {
                        // Пробуем найти любые массивы в JSON
                        foreach (var property in sessionsData.EnumerateObject())
                        {
                            if (property.Value.ValueKind == JsonValueKind.Array)
                            {
                                var array = property.Value.EnumerateArray().ToArray();
                                if (array.Length > 0 && array[0].ValueKind == JsonValueKind.Object)
                                {
                                    return ProcessSessionsArray(array, date);
                                }
                            }
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _log.LogError(ex, "Не удалось распарсить JSON сеансов");
                }

                _log.LogWarning("Сеансы не найдены в ответе. JSON: {Json}", sessionsJson);
                return ($"😔 На {date} нет доступных сеансов.", TicketsDateKeyboard());
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Ошибка в GetSessionsForDateAsync для даты {Date}", date);
                await _errors.LogErrorAsync(ex, "ERROR", additional: new { Component = "GetSessionsForDate", Date = date });
                return ($"❌ Ошибка при получении сеансов", TicketsDateKeyboard());
            }
        }

        // Вспомогательный метод для обработки массива сеансов
        private (string message, string keyboard) ProcessSessionsArray(JsonElement[] sessionsArray, string date)
        {
            string text = $"🎟 *Доступные сеансы на {date}:*\n\n";
            var buttonsList = new List<object[]>();
            int availableSessions = 0;

            foreach (var session in sessionsArray)
            {
                try
                {
                    // Пробуем получить время сеанса разными способами
                    string sessionTime = GetSessionTime(session);
                    if (string.IsNullOrEmpty(sessionTime))
                    {
                        _log.LogWarning("Не удалось получить время сеанса из элемента: {Element}", session);
                        continue;
                    }

                    // Пробуем получить количество свободных мест
                    int placesFree = GetPlacesFree(session);
                    int placesTotal = GetPlacesTotal(session);

                    // Если не можем определить количество мест, все равно показываем сеанс
                    if (placesFree == 0 && placesTotal == 0)
                    {
                        placesFree = 1; // Предполагаем, что есть места
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
                    _log.LogWarning(ex, "Ошибка обработки элемента сеанса: {Element}", session);
                    continue;
                }
            }

            if (availableSessions == 0)
            {
                return ($"😔 На {date} нет доступных сеансов или все заняты.", TicketsDateKeyboard());
            }

            // Добавляем кнопку назад
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

        // Метод для получения времени сеанса
        private string GetSessionTime(JsonElement session)
        {
            // Пробуем разные варианты названий полей
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

            // Пробуем собрать время из startTime и endTime
            string startTime = "";
            string endTime = "";

            string[] startFields = { "startTime", "StartTime", "timeStart", "TimeStart" };
            string[] endFields = { "endTime", "EndTime", "timeEnd", "TimeEnd" };

            foreach (var field in startFields)
            {
                if (session.TryGetProperty(field, out var startProp) && startProp.ValueKind == JsonValueKind.String)
                {
                    startTime = startProp.GetString() ?? "";
                    break;
                }
            }

            foreach (var field in endFields)
            {
                if (session.TryGetProperty(field, out var endProp) && endProp.ValueKind == JsonValueKind.String)
                {
                    endTime = endProp.GetString() ?? "";
                    break;
                }
            }

            if (!string.IsNullOrEmpty(startTime) && !string.IsNullOrEmpty(endTime))
                return $"{startTime}-{endTime}";

            return "Время не указано";
        }

        // Метод для получения свободных мест
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

        // Метод для получения общего количества мест
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

        private async Task<(string message, string keyboard)> GetFormattedTariffsAsync(HttpClient client, string date, string sessionTime, string category)
        {
            try
            {
                var tariffsUrl = $"https://apigateway.nordciti.ru/v1/aqua/getTariffsAqua?date={date}";
                var tariffsResponse = await client.GetAsync(tariffsUrl);

                if (!tariffsResponse.IsSuccessStatusCode)
                {
                    _log.LogWarning("Не удалось получить тарифы. Статус: {StatusCode}", tariffsResponse.StatusCode);
                    return ("⚠️ Ошибка при загрузке тарифов", BackKeyboard());
                }

                var tariffsJson = await tariffsResponse.Content.ReadAsStringAsync();
                _log.LogInformation("[ОТЛАДКА] Сырые данные тарифов: {TariffsJson}", tariffsJson);

                var tariffsData = JsonSerializer.Deserialize<JsonElement>(tariffsJson, _jsonOptions);

                if (!tariffsData.TryGetProperty("result", out var tariffsArray) || tariffsArray.GetArrayLength() == 0)
                {
                    return ("😔 На выбранную дату нет доступных тарифов", BackKeyboard());
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

                    // Создаем уникальный ключ для избежания дубликатов
                    string tariffKey = $"{name.ToLower()}_{price}";

                    if (seenTariffs.Contains(tariffKey)) continue;
                    seenTariffs.Add(tariffKey);

                    // Улучшенная фильтрация по категории
                    string nameLower = name.ToLower();
                    bool isAdult = nameLower.Contains("взрос") ||
                                  nameLower.Contains("adult") ||
                                  (nameLower.Contains("вип") && !nameLower.Contains("дет")) ||
                                  (nameLower.Contains("взр") && !nameLower.Contains("дет")) ||
                                  (price > 1000 && !nameLower.Contains("дет"));

                    bool isChild = nameLower.Contains("детск") ||
                                  nameLower.Contains("child") ||
                                  nameLower.Contains("kids") ||
                                  nameLower.Contains("дет") ||
                                  (price < 1000 && nameLower.Contains("билет") && !nameLower.Contains("взр"));

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
                    // Группируем и сортируем билеты
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
                    text += $"• Возможна оплата картой или наличными";
                }

                text += $"\n\n🔗 *Купить онлайн:* yes35.ru";

                // Исправленная строка 742 - явно указываем тип массива
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
                _log.LogError(ex, "Ошибка получения тарифов для даты {Date}, сеанс {Session}, категория {Category}", date, sessionTime, category);
                await _errors.LogErrorAsync(ex, "ERROR", additional: new { Component = "GetFormattedTariffs", Date = date, Session = sessionTime, Category = category });
                return ("❌ Ошибка при получении тарифов. Попробуйте позже 😔", BackKeyboard());
            }
        }

        // 📝 Вспомогательный метод для форматирования названий билетов
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
                .Replace("вечерний", "Вечерний")
                .Replace("утренний", "Утренний")
                .Replace("  ", " ")
                .Trim();

            // Убираем лишние пробелы и дублирования
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

        private async Task<string> GetParkLoadAsync(HttpClient client)
        {
            try
            {
                var requestData = new { SiteID = "1" };
                var response = await client.PostAsJsonAsync("https://apigateway.nordciti.ru/v1/aqua/CurrentLoad", requestData);

                if (!response.IsSuccessStatusCode)
                {
                    _log.LogWarning("Не удалось получить данные о загруженности парка. Статус: {StatusCode}", response.StatusCode);
                    return "❌ Не удалось получить данные о загруженности. Попробуйте позже 😔";
                }

                var data = await response.Content.ReadFromJsonAsync<ParkLoadResponse>(_jsonOptions);
                if (data == null)
                {
                    _log.LogWarning("Не удалось обработать ответ о загруженности парка");
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
                _log.LogError(ex, "Ошибка получения данных о загруженности парка");
                await _errors.LogErrorAsync(ex, "ERROR", additional: new { Component = "GetParkLoad" });
                return "❌ Не удалось получить информацию о загруженности. Попробуйте позже 😔";
            }
        }

        private static string GetWorkingHours() => "🏢 Режим работы точек Центра YES:\n\n" +

                   "🌊 Аквапарк\n" +
                   "⏰ 10:00 - 21:00 │ 📅 Ежедневно\n" +
                   "💧 Бассейны, горки, сауны\n\n" +

                   "🍽️ Ресторан\n" +
                   "⏰ 10:00 - 21:00 │ 📅 Ежедневно\n" +
                   "🍕 Кухня европейская и азиатская\n\n" +

                   "🎮 Игровой центр\n" +
                   "⏰ 10:00 - 18:00 │ 📅 Ежедневно\n" +
                   "🎯 Автоматы и симуляторы\n\n" +

                   "🦖 Динопарк\n" +
                   "⏰ 10:00 - 18:00 │ 📅 Ежедневно\n" +
                   "🦕 Интерактивные экспонаты\n\n" +

                   "🏨 Гостиница\n" +
                   "⏰ Круглосуточно │ 📅 Ежедневно\n" +
                   "🛏️ Номера различных категорий\n\n" +

                   "🔴 Временно не работают:\n" +
                   "• 🧗‍ Веревочный парк\n" +
                   "• 🧗‍ Скалодром\n" +
                   "• 🎡 Парк аттракционов\n" +
                   "• 🍔 MasterBurger\n\n" +

                   "📞 Уточнить информацию: (8172) 33-06-06";

        private static string GetContacts() => "📞 Контакты Центра YES\n\n" +

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

        // ↓↓↓ МЕТОДЫ ДЛЯ СТАТИСТИКИ ↓↓↓

        // Метод для определения команды из сообщения
        private string GetCommandFromMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return "unknown";

            var lowerMsg = message.ToLower();
            return lowerMsg switch
            {
                "начать" or "/start" or "🚀 начать" => "start",
                "информация" or "ℹ️ информация" => "info",
                "билеты" or "🎟 купить билеты" => "tickets",
                "загруженность" or "📊 загруженность" => "load",
                "время работы" or "⏰ время работы" => "hours",
                "контакты" or "📞 контакты" => "contacts",
                "🔙 назад" or "назад" => "back",
                "🔙 к сеансам" => "back_to_sessions",
                "🔙 в начало" => "back_to_start",
                _ when lowerMsg.StartsWith("📅") => "select_date",
                _ when lowerMsg.StartsWith("⏰") => "select_session",
                _ when IsTicketCategoryMessage(message) => "select_ticket_category",
                _ => "other"
            };
        }

        // Методы для получения статистики
        public int GetOnlineUsersCount() => _userLastActivity.Count(u => u.Value > DateTime.Now.AddMinutes(-5)); public DateTime GetStartTime() => _startTime;
        public Dictionary<string, int> GetCommandUsage() => new Dictionary<string, int>(_commandUsage);
        public int GetActiveUsersToday() => _userLastActivity.Count(u => u.Value.Date == DateTime.Today);

        // --- models ---
        public class ParkLoadResponse { public int Count { get; set; } public int Load { get; set; } }
        public class SessionItem
        {
            public string StartTime { get; set; } = "";
            public string EndTime { get; set; } = "";
            public int PlacesFree { get; set; }
            public int PlacesTotal { get; set; }
        
    }


        public object GetLiveStats()
        {
            var uptime = DateTime.Now - _startTime;

            return new
            {
                UsersOnline = GetOnlineUsersCount(), // ← ВОТ ТАК ДОЛЖНО БЫТЬ!
                MessagesProcessed = _totalMessagesProcessed,
                ActiveToday = GetActiveUsersToday(),
                Uptime = $"{uptime.Hours}h {uptime.Minutes}m",
                StartTime = _startTime
            };
        }

        public object GetCommandStats()
        {
            // ТОЛЬКО реальные данные
            var popularCommands = _commandUsage
                .OrderByDescending(x => x.Value)
                .Take(5)
                .Select(x => new { Name = x.Key, UsageCount = x.Value })
                .ToList();

            return new
            {
                TotalExecuted = _totalMessagesProcessed,
                DailyUsage = GenerateRealDailyUsage(), // Реальная активность
                PopularCommands = popularCommands
            };
        }

        // Вспомогательный метод для daily usage (можно оставить заглушку)
        private List<object> GenerateRealDailyUsage()
        {
            var dailyStats = new Dictionary<string, int>();
            var dayNames = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

            // Собираем статистику за последние 7 дней
            for (int i = 6; i >= 0; i--)
            {
                var date = DateTime.Now.AddDays(-i);
                var dayName = dayNames[(int)date.DayOfWeek];
                var activityCount = _userLastActivity.Count(u => u.Value.Date == date.Date);

                dailyStats[dayName] = activityCount;
            }

            var result = new List<object>();
            foreach (var day in dayNames)
            {
                dailyStats.TryGetValue(day, out var count);
                result.Add(new { Date = day, Count = count });
            }

            return result;
        }

    } }