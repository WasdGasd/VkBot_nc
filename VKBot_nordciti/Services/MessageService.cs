using Microsoft.EntityFrameworkCore;
using Models;
using Services;
using System.Text.Json;
using VK;
using VK.Models;

namespace Services
{
    public class MessageService : IMessageService
    {
        private readonly VkApiManager _vk;
        private readonly KeyboardProvider _kb;
        private readonly ConversationStateService _state;
        private readonly FileLogger _logger;
        private readonly CommandService _commandService;
        private readonly BotDbContext _db;

        private readonly JsonSerializerOptions _jsonOptions =
            new() { PropertyNameCaseInsensitive = true };

        public MessageService(
            VkApiManager vkApi,
            KeyboardProvider kb,
            ConversationStateService state,
            FileLogger logger,
            CommandService commandService,
            BotDbContext db)
        {
            _vk = vkApi;
            _kb = kb;
            _state = state;
            _logger = logger;
            _commandService = commandService;
            _db = db;
        }

        public async Task ProcessMessageAsync(VkMessage message)
        {
            try
            {
                var userId = message.UserId;
                var text = (message.Text ?? string.Empty).Trim();

                _logger.Info($"Received from {userId}: {text}");

                var state = _state.GetState(userId);
                var lower = text.ToLowerInvariant();

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

                    if (!string.IsNullOrWhiteSpace(dbCommand.KeyboardJson))
                        await _vk.SendMessageAsync(message.PeerId, dbCommand.Response, dbCommand.KeyboardJson);
                    else
                        await _vk.SendMessageAsync(message.PeerId, dbCommand.Response);

                    _state.SetState(userId, ConversationState.Idle);
                    return;
                }

                // ======================================================
                //                   ДАЛЬШЕ — ВАШ СТАРЫЙ КОД
                // ======================================================

                if (lower.Contains("/start") || lower.Contains("начать") || lower.Contains("🚀"))
                {
                    _state.SetState(userId, ConversationState.Idle);
                    var welcome = TextGenerators.GenerateWelcomeText();
                    await _vk.SendMessageAsync(message.PeerId, welcome, _kb.MainMenu());
                    return;
                }

                if (lower.Contains("ℹ") || lower.Contains("информация"))
                {
                    await _vk.SendMessageAsync(message.PeerId, "Выберите раздел с информацией:", _kb.InfoMenu());
                    _state.SetState(userId, ConversationState.Idle);
                    return;
                }

                if (lower.Contains("📊") || lower.Contains("загруженн"))
                {
                    var load = await ExternalApiSimulators.GetParkLoadAsync();
                    await _vk.SendMessageAsync(message.PeerId, load, _kb.BackToMain());
                    _state.SetState(userId, ConversationState.Idle);
                    return;
                }

                if (lower.Contains("🕒") || lower.Contains("время работы"))
                {
                    await _vk.SendMessageAsync(message.PeerId, GetWorkingHours(), _kb.InfoMenu());
                    _state.SetState(userId, ConversationState.Idle);
                    return;
                }

                if (lower.Contains("📞") || lower.Contains("контакт"))
                {
                    await _vk.SendMessageAsync(message.PeerId, GetContacts(), _kb.InfoMenu());
                    _state.SetState(userId, ConversationState.Idle);
                    return;
                }

                if (lower.Contains("📍") || lower.Contains("добраться"))
                {
                    await _vk.SendMessageAsync(message.PeerId, GetLocationInfo(), _kb.InfoMenu());
                    _state.SetState(userId, ConversationState.Idle);
                    return;
                }

                if (lower.Contains("🔙") && lower.Contains("главное"))
                {
                    await _vk.SendMessageAsync(message.PeerId, "Главное меню:", _kb.MainMenu());
                    _state.SetState(userId, ConversationState.Idle);
                    return;
                }

                switch (state)
                {
                    case ConversationState.Idle:
                        if (lower.Contains("📅") || lower.Contains("билеты") || lower.Contains("билет"))
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
                            _state.SetState(userId, "date", date);
                            _state.SetState(userId, ConversationState.WaitingForSession);

                            var (sessionsText, keyboardJson) = await ExternalApiSimulators.GetSessionsForDateAsync(date);
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
                            _state.SetState(userId, "session", session);
                            _state.SetState(userId, ConversationState.WaitingForCategory);

                            await _vk.SendMessageAsync(message.PeerId,
                                $"Вы выбрали сеанс {session}. Теперь выберите категорию билетов:",
                                _kb.TicketCategoryKeyboard());
                        }
                        else if (lower.Contains("назад") || lower.Contains("🔙"))
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
                        if (IsTicketCategoryMessage(lower))
                        {
                            var category = GetTicketCategoryFromMessage(lower);
                            _state.SetState(userId, "category", category);

                            var date = _state.GetData(userId, "date") ?? "неизвестная дата";
                            var sessionSelected = _state.GetData(userId, "session") ?? "неизвестный сеанс";

                            var (tariffsText, tariffsKb) =
                                await ExternalApiSimulators.GetFormattedTariffsAsync(date, sessionSelected, category);

                            _state.SetState(userId, ConversationState.WaitingForPayment);
                            await _vk.SendMessageAsync(message.PeerId, tariffsText, tariffsKb);
                        }
                        else if (lower.Contains("назад") || lower.Contains("🔙"))
                        {
                            _state.SetState(userId, ConversationState.WaitingForSession);
                            var dateVal = _state.GetData(userId, "date") ?? DateTime.Now.ToString("dd.MM.yyyy");
                            var (sessionsText, kbJson) = await ExternalApiSimulators.GetSessionsForDateAsync(dateVal);
                            await _vk.SendMessageAsync(message.PeerId, sessionsText, kbJson);
                        }
                        else
                        {
                            await _vk.SendMessageAsync(message.PeerId, "Выберите категорию билетов:", _kb.TicketCategoryKeyboard());
                        }
                        break;

                    case ConversationState.WaitingForPayment:
                        if (lower.Contains("💳") || lower.Contains("оплат"))
                        {
                            _state.SetState(userId, ConversationState.Idle);
                            await _vk.SendMessageAsync(message.PeerId, "✅ Оплата прошла успешно! Спасибо за покупку!", _kb.MainMenu());
                        }
                        else if (lower.Contains("назад") || lower.Contains("🔙"))
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

        private static bool IsTicketCategoryMessage(string msg)
            => msg.Contains("взрос") || msg.Contains("дет") || msg.Contains("👤") || msg.Contains("👶");

        private static string GetTicketCategoryFromMessage(string msg)
            => (msg.Contains("дет") || msg.Contains("👶")) ? "child" : "adult";

        private static string GetWorkingHours() =>
            "🏢 Режим работы:\n\n⏰ Ежедневно: 10:00 - 22:00\n📅 Без выходных";

        private static string GetContacts() =>
            "📞 Контакты:\n" +
             "📱 Телефон для связи:\n" +
            "• Основной: (8172) 33-06-06\\n" +
            "• Ресторан: 8-800-200-67-71\n\n" +

            "📧 Электронная почта:\n" +
                    "yes@yes35.ru\n\n" +

            "🌐 Мы в соцсетях:\n" +
                    "ВКонтакте: vk.com/yes35\n" +
                    "Telegram: t.me/CentreYES35\n" +
                    "WhatsApp: ссылка в профиле\n\n" +

                    "⏰ Часы работы call-центра:\n" +
                    "🕙 09:00 - 22:00";

        private static string GetLocationInfo() =>
            "🏠 *Адрес:*\n" +
                 "Вологодская область, М.О. Вологодский\n" +
                 "д. Брагино, тер. Центр развлечений\n\n" +

                 "🚗 *На автомобиле:*\n" +
                 "• По федеральной трассе А114 'Вологда - Новая Ладога'\n" +
                 "• На повороте к Центру на трассе установлен заметный баннер-указатель.\n" +
                 "• 💰 *Бесплатная парковка* на территории\n\n" +

                 "🚍 *Общественный транспорт:*\n" +
                 "• От автовокзала Вологды (площадь Бабушкина, 10) ходят ежедневные и регулярные рейсовые автобусы\n" +

                 "🗺 *Координаты для навигатора:*\n" +
                 "59.1858° с.ш., 39.7685° в.д.\n\n" +

                 "⏱ *Расстояния:*\n" +
                 "• От г. Вологды: ~34 км\n" +
                 "• От г. Череповца: ~107 км\n" +


                 "🏞 *Расположение:*\n" +
                 "Круглогодичный развлекательный комплекс\n" +
                 "в живописной лесной зоне под Вологдой";
    }
}
