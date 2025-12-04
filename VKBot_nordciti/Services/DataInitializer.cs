using VKBot_nordciti.Data;
using VKBot_nordciti.Models;

namespace VKBot_nordciti.Services
{
    public class DataInitializer : IDataInitializer
    {
        private readonly BotDbContext _dbContext;

        public DataInitializer(BotDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task InitializeAsync()
        {
            try
            {
                // Проверяем, есть ли уже команды в базе
                if (!_dbContext.Commands.Any())
                {
                    // Добавляем базовые команды
                    var commands = new List<Command>
                    {
                        new Command
                        {
                            Name = "Помощь",
                            Triggers = "помощь,помоги,help,что ты умеешь,команды",
                            Response = "🤖 Я ваш помощник по аквапарку YES! Вот что я умею:\n\n" +
                                      "📅 **Билеты** - Покупка билетов онлайн\n" +
                                      "📊 **Загруженность** - Текущая загруженность аквапарка\n" +
                                      "ℹ️ **Информация** - Вся информация о центре\n" +
                                      "🕒 **Время работы** - Расписание работы зон\n" +
                                      "📞 **Контакты** - Контакты и связь\n" +
                                      "📍 **Как добраться** - Схема проезда и парковка\n\n" +
                                      "Просто выберите нужный пункт в меню или напишите команду!",
                            KeyboardJson = null,
                            CommandType = "text"
                        },
                        new Command
                        {
                            Name = "Информация",
                            Triggers = "информация,info,инфо,о центре,про центр",
                            Response = "📋 **ИНФОРМАЦИЯ О ЦЕНТРЕ YES**\n\n" +
                                      "🏊 **Аквапарк**:\n" +
                                      "- Горки для всех возрастов\n" +
                                      "- Волновой бассейн\n" +
                                      "- Детская зона\n" +
                                      "- СПА-комплекс\n\n" +
                                      "🍽 **Рестораны и кафе**:\n" +
                                      "- Основной ресторан\n" +
                                      "- Фаст-фуд зона\n" +
                                      "- Бар у бассейна\n\n" +
                                      "🎳 **Развлечения**:\n" +
                                      "- Боулинг\n" +
                                      "- Кинотеатр\n" +
                                      "- Детские игровые зоны\n\n" +
                                      "Выберите нужный раздел ниже 👇",
                            KeyboardJson = null,
                            CommandType = "text"
                        },
                        new Command
                        {
                            Name = "Контакты",
                            Triggers = "контакты,телефон,адрес,связаться,звонок",
                            Response = "📞 **КОНТАКТЫ ЦЕНТРА YES**\n\n" +
                                      "📍 **Адрес**:\n" +
                                      "г. Вологда, ул. Примерная, 123\n\n" +
                                      "📱 **Телефоны**:\n" +
                                      "• Общая информация: +7 (8172) 12-34-56\n" +
                                      "• Бронирование: +7 (8172) 12-34-57\n" +
                                      "• Аквапарк: +7 (8172) 12-34-58\n\n" +
                                      "📧 **Email**:\n" +
                                      "• info@yes35.ru\n" +
                                      "• booking@yes35.ru\n\n" +
                                      "🌐 **Сайт**: https://yes35.ru\n\n" +
                                      "⏰ **Режим работы**:\n" +
                                      "Ежедневно с 10:00 до 22:00",
                            KeyboardJson = null,
                            CommandType = "text"
                        },
                        new Command
                        {
                            Name = "Главное меню",
                            Triggers = "меню,начать,старт,главная,main,start",
                            Response = "🏊 **ДОБРО ПОЛОЖАЛОВАТЬ В ЦЕНТР YES!**\n\n" +
                                      "Выберите нужный раздел 👇",
                            KeyboardJson = null,
                            CommandType = "text"
                        }
                    };

                    await _dbContext.Commands.AddRangeAsync(commands);
                    await _dbContext.SaveChangesAsync();

                    Console.WriteLine("✅ База данных инициализирована с базовыми командами");
                }
                else
                {
                    Console.WriteLine("✅ База данных уже содержит команды");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка инициализации базы данных: {ex.Message}");
            }
        }
    }
}