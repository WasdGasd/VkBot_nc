using VKBot_nordciti.VK;
using VKBot_nordciti.VK.Models;

namespace VKBot_nordciti.Services
{
    public class MesService : IMessageService
    {
        private readonly VkApiManager _vk;
        private readonly KeyboardProvider _kb;
        private readonly ConversationStateService _state;
        private readonly FileLogger _logger;
        private readonly ICommandService _commandService;

        public MesService(
            VkApiManager vkApi,
            KeyboardProvider kb,
            ConversationStateService state,
            FileLogger logger,
            ICommandService commandService)
        {
            _vk = vkApi;
            _kb = kb;
            _state = state;
            _logger = logger;
            _commandService = commandService;
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
                await SendMessage(peerId, GenerateWelcomeText(), _kb.MainMenu());
                return;
            }

            if (text.ToLower() == "начать" || text.ToLower() == "start" || text.Contains("🎯"))
            {
                await SendMessage(peerId, "Добро пожаловать в аквапарк! 🏊‍♂️\n\nВыберите нужный раздел:", _kb.MainMenu());
                return;
            }

            if (text.Contains("📊") || text.ToLower().Contains("загруженность"))
            {
                var loadInfo = await _commandService.ProcessCommandAsync(new Models.Command { CommandType = "api_park_load" });
                await SendMessage(peerId, loadInfo, _kb.BackToMain());
                return;
            }

            if (text.Contains("ℹ️") || text.ToLower().Contains("информация"))
            {
                await SendMessage(peerId, "Выберите нужный раздел информации 👇", _kb.InfoMenu());
                return;
            }

            if (text.Contains("📅") || text.ToLower().Contains("билет"))
            {
                _state.SetState(userId, ConversationState.WaitingForDate);
                await SendMessage(peerId, "🎫 Покупка билетов\n\nВыберите дату для посещения:", _kb.TicketsDateKeyboard());
                return;
            }

            if (text.Contains("📞") || text.ToLower().Contains("контакт"))
            {
                await SendMessage(peerId, GetContactsText(), _kb.BackToInfo());
                return;
            }

            if (text.Contains("🕒") || text.ToLower().Contains("время работы"))
            {
                await SendMessage(peerId, GetWorkingHoursText(), _kb.BackToInfo());
                return;
            }

            if (text.Contains("📍") || text.ToLower().Contains("адрес"))
            {
                await SendMessage(peerId, GetLocationText(), _kb.BackToInfo());
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

        private string GenerateWelcomeText()
        {
            return "🌊 ДОБРО ПОЛОЖАЛОВАТЬ В ЦЕНТР YES!\n\n" +
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
                   "💫 Центр YES - где рождаются воспоминания!";
        }

        private async Task HandleDateSelection(long peerId, long userId, string text)
        {
            if (text.StartsWith("📅"))
            {
                var date = text.Replace("📅", "").Trim();
                _state.SetData(userId, "selected_date", date);
                _state.SetState(userId, ConversationState.WaitingForSession);

                // Получаем сеансы через API
                var parameters = new Dictionary<string, string> { ["date"] = date };
                var sessionsInfo = await _commandService.ProcessCommandAsync(
                    new Models.Command { CommandType = "api_sessions" }, parameters);

                var sessionsKeyboard = await CreateSessionsKeyboard(date);
                await SendMessage(peerId, sessionsInfo, sessionsKeyboard);
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
                var sessionsKeyboard = await CreateSessionsKeyboard(date);
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
                var parameters = new Dictionary<string, string>
                {
                    ["date"] = date,
                    ["session"] = session,
                    ["category"] = category
                };
                var tariffsInfo = await _commandService.ProcessCommandAsync(
                    new Models.Command { CommandType = "api_tariffs" }, parameters);

                _state.SetState(userId, ConversationState.WaitingForPayment);
                await SendMessage(peerId, tariffsInfo, _kb.PaymentKeyboard());
            }
            else if (text.Contains("🔙") || text.ToLower().Contains("назад"))
            {
                _state.SetState(userId, ConversationState.WaitingForSession);
                var date = _state.GetData(userId, "selected_date") ?? DateTime.Now.ToString("dd.MM.yyyy");
                var sessionsKeyboard = await CreateSessionsKeyboard(date);
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

        private async Task<string> CreateSessionsKeyboard(string date)
        {
            try
            {
                // Получаем реальные сеансы из API
                var commandService = _commandService as CommandService;
                if (commandService != null)
                {
                    var sessions = await commandService.GetSessionsListAsync(date);

                    if (sessions.Count > 0)
                    {
                        var buttons = new List<object[]>();

                        foreach (var session in sessions)
                        {
                            // Показываем только сеансы со свободными местами
                            if (session.PlacesFree > 0)
                            {
                                buttons.Add(new[]
                                {
                                    new { action = new { type = "text", label = $"⏰ {session.Time}" }, color = "primary" }
                                });
                            }
                        }

                        if (buttons.Count == 0)
                        {
                            return CreateNoSessionsKeyboard();
                        }

                        buttons.Add(new[]
                        {
                            new { action = new { type = "text", label = "🔙 Назад" }, color = "negative" }
                        });

                        return System.Text.Json.JsonSerializer.Serialize(new
                        {
                            one_time = true,
                            buttons = buttons.ToArray()
                        });
                    }
                }

                return CreateNoSessionsKeyboard();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "CreateSessionsKeyboard");
                return CreateNoSessionsKeyboard();
            }
        }

        private string CreateNoSessionsKeyboard()
        {
            var buttons = new List<object[]>
            {
                new[] { new { action = new { type = "text", label = "🔙 Назад" }, color = "negative" } }
            };

            return System.Text.Json.JsonSerializer.Serialize(new
            {
                one_time = true,
                buttons = buttons.ToArray()
            });
        }

        private string GetContactsText()
        {
            return "📞 Контакты:\n\n" +
                   "📱 Телефон для связи:\n" +
                   "• Основной: (8172) 33-06-06\n" +
                   "• Ресторан: 8-800-200-67-71\n\n" +
                   "📧 Электронная почта:\n" +
                   "yes@yes35.ru\n\n" +
                   "🌐 Мы в соцсетях:\n" +
                   "ВКонтакте: vk.com/yes35\n" +
                   "Telegram: t.me/CentreYES35\n\n" +
                   "⏰ Часы работы call-центра:\n" +
                   "🕙 09:00 - 22:00";
        }

        private string GetWorkingHoursText()
        {
            return "🏢 РЕЖИМ РАБОТЫ ЦЕНТРА YES\n\n" +

                       "🌊 Аквапарк:\n" +
                       "⏰ 10:00 - 21:00 │ 📅 Ежедневно\n\n" +

                       "🐇 Зоопарк:\n" +
                       "⏰ 10:00 - 18:00 │ 📅 Ежедневно\n\n" +

                       "🍽️ Ресторан:\n" +
                       "1 этаж: сб - вс с 10.00 до 18.00\n" +
                       "2 этаж: с 10.00 до 21.00 - ежедневно\n" +
                       "Заказы принимаются до 20:00\n\n" +

                       "🧗 Скалодром:\n" +
                       "Временно закрыт\n" +
                       "До встречи в следующем сезоне!\n\n" +

                       "🏞️ Веревочный парк:\n" +
                       "Временно закрыт\n" +
                       "До встречи в следующем сезоне!\n\n" +

                       "🎢 Парк атракционов:\n" +
                       "Временно закрыт\n" +
                       "До встречи в следующем сезоне!\n\n" +

                       "🎮 Игровой центр:\n" +
                       "⏰ 10:00 - 18:00 │ 📅 Ежедневно\n\n" +

                       "🦖 Динопарк:\n" +
                       "⏰ 10:00 - 18:00 │ 📅 Ежедневно\n" +
                       "Динозавры ушли в спячку до весны(движение и звук отключены)\n\n" +

                       "🏨 Гостиница:\n" +
                       "⏰ Круглосуточно │ 📅 Ежедневно\n\n" +

                       "📞 Уточнить информацию: 8 (800) 101-35-01";
        }

        private string GetLocationText()
        {
            return "📍 Центр YES - Как добраться\n\n" +
                   "🏠 Адрес:\n" +
                   "Вологодская область, М.О. Вологодский\n" +
                   "д. Брагино, тер. Центр развлечений\n\n" +
                   "🚗 На автомобиле:\n" +
                   "• По федеральной трассе А114 'Вологда - Новая Ладога'\n" +
                   "• На повороте к Центру на трассе установлен заметный баннер-указатель.\n" +
                   "• 💰 Бесплатная парковка на территории\n\n" +
                   "🗺 Координаты для навигатора:\n" +
                   "59.1858° с.ш., 39.7685° в.д.";
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
    }
}