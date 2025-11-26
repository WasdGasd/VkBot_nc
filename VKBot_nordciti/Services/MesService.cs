using VKBot_nordciti.VK.Models;
using VKBot_nordciti.VK;

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
                var userId = message.UserId;
                var text = (message.Text ?? string.Empty).Trim();

                // Используем PeerId из сообщения, если он есть, иначе используем UserId
                var peerId = message.PeerId != 0 ? message.PeerId : message.UserId;

                _logger.Info($"Processing message from {userId} (peer: {peerId}): '{text}'");

                // Если сообщение пустое
                if (string.IsNullOrEmpty(text))
                {
                    await SendMessage(peerId, "Привет! 👋 Я бот аквапарка. Выберите действие:", _kb.MainMenu());
                    return;
                }

                // Обработка команды "начать"
                if (text.ToLower() == "начать" || text.ToLower() == "start")
                {
                    await SendMessage(peerId,
                        "Добро пожаловать в аквапарк! 🏊‍♂️\n\nВыберите нужный раздел:",
                        _kb.MainMenu());
                    return;
                }

                // Обработка загруженности
                if (text.Contains("📊") || text.ToLower().Contains("загруженность"))
                {
                    var loadInfo = await _commandService.ProcessCommandAsync(new Models.Command
                    {
                        CommandType = "api_park_load"
                    });
                    await SendMessage(peerId, loadInfo, _kb.BackToMain());
                    return;
                }

                // Обработка информации
                if (text.Contains("ℹ️") || text.ToLower().Contains("информация"))
                {
                    await SendMessage(peerId,
                        "Выберите нужный раздел информации 👇",
                        _kb.InfoMenu());
                    return;
                }

                // Обработка билетов
                if (text.Contains("📅") || text.ToLower().Contains("билет"))
                {
                    await SendMessage(peerId,
                        "Выберите дату для посещения:",
                        _kb.TicketsDateKeyboard());
                    return;
                }

                // Обработка контактов
                if (text.Contains("📞") || text.ToLower().Contains("контакт"))
                {
                    await SendMessage(peerId,
                        "📞 Контакты:\n\n📱 Телефон для связи:\n• Основной: (8172) 33-06-06\n• Ресторан: 8-800-200-67-71\n\n📧 Электронная почта:\nyes@yes35.ru",
                        _kb.BackToInfo());
                    return;
                }

                // Обработка режима работы
                if (text.Contains("🕒") || text.ToLower().Contains("время работы"))
                {
                    await SendMessage(peerId,
                        "🏢 Режим работы:\n\n⏰ Ежедневно: 10:00 - 22:00\n📅 Без выходных",
                        _kb.BackToInfo());
                    return;
                }

                // Обработка местоположения
                if (text.Contains("📍") || text.ToLower().Contains("адрес"))
                {
                    await SendMessage(peerId,
                        "📍 Центр YES - Как добраться\n\n🏠 Адрес:\nВологодская область, М.О. Вологодский\nд. Брагино, тер. Центр развлечений\n\n🚗 На автомобиле:\n• По федеральной трассе А114 'Вологда - Новая Ладога'\n• На повороте к Центру на трассе установлен заметный баннер-указатель.",
                        _kb.BackToInfo());
                    return;
                }

                // Если команда не распознана
                await SendMessage(peerId,
                    "Я вас не понял 😊\n\nВыберите пункт меню или напишите 'помощь'",
                    _kb.MainMenu());

            }
            catch (Exception ex)
            {
                _logger.Error(ex, "ProcessMessageAsync");
                try
                {
                    // Используем PeerId из сообщения для отправки ошибки
                    var peerId = message.PeerId != 0 ? message.PeerId : message.UserId;
                    await SendMessage(peerId,
                        "Произошла ошибка при обработке сообщения. Попробуйте позже.",
                        _kb.MainMenu());
                }
                catch
                {
                    // Игнорируем ошибки отправки
                }
            }
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