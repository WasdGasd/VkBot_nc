using VKBot_nordciti.Services;

namespace VKBot_nordciti.Helpers
{
    public static class ExternalApiSimulators
    {
        public static string GetParkLoadSimulated()
        {
            var random = new Random();
            var count = random.Next(50, 300);
            var load = random.Next(10, 95);

            string loadStatus = load switch
            {
                < 30 => "🟢 Низкая загруженность",
                < 60 => "🟡 Средняя загруженность",
                < 85 => "🟠 Высокая загруженность",
                _ => "🔴 Очень высокая загруженность"
            };

            string recommendation = load switch
            {
                < 30 => "🌟 Идеальное время для посещения!",
                < 50 => "👍 Хорошее время, народу немного",
                < 70 => "⚠️ Средняя загруженность, возможны очереди",
                < 85 => "📢 Много посетителей, лучше выбрать другое время",
                _ => "🚫 Очень высокая загруженность, не рекомендуется"
            };

            return $"📊 Загруженность аквапарка (тестовые данные):\n\n" +
                   $"👥 Количество посетителей: {count} чел.\n" +
                   $"📈 Уровень загруженности: {load}%\n" +
                   $"🏷 Статус: {loadStatus}\n\n" +
                   $"💡 Рекомендация:\n{recommendation}\n\n" +
                   $"🕐 Обновлено: {DateTime.Now:HH:mm}";
        }

        public static string GetSessionsSimulated(string date)
        {
            var sessions = new[]
            {
                new { Time = "10:00", Free = 25, Total = 50 },
                new { Time = "12:00", Free = 15, Total = 50 },
                new { Time = "14:00", Free = 8, Total = 50 },
                new { Time = "16:00", Free = 30, Total = 50 },
                new { Time = "18:00", Free = 45, Total = 50 },
                new { Time = "20:00", Free = 20, Total = 50 }
            };

            var text = $"🎟 Доступные сеансы на {date}:\n\n";

            foreach (var session in sessions)
            {
                string availability = session.Free switch
                {
                    0 => "🔴 Нет мест",
                    < 10 => "🔴 Мало мест",
                    < 20 => "🟡 Средняя загрузка",
                    _ => "🟢 Есть места"
                };

                text += $"⏰ {session.Time}\n";
                text += $"   Свободно: {session.Free}/{session.Total} мест\n";
                text += $"   {availability}\n\n";
            }

            return text;
        }

        public static string GetTariffsSimulated(string date, string sessionTime, string category)
        {
            string categoryTitle = category == "adult" ? "👤 ВЗРОСЛЫЕ БИЛЕТЫ" : "👶 ДЕТСКИЕ БИЛЕТЫ";

            var text = $"🎟 {categoryTitle}\n";
            text += $"⏰ Сеанс: {sessionTime}\n";
            text += $"📅 Дата: {date}\n\n";
            text += "💰 Стоимость билетов:\n\n";

            if (category == "adult")
            {
                text += "💎 VIP Весь день: 2500₽\n";
                text += "⭐ Стандарт 4 часа: 1500₽\n";
                text += "🎫 Базовый 2 часа: 1000₽\n";
            }
            else
            {
                text += "💎 VIP Весь день: 1800₽\n";
                text += "⭐ Стандарт 4 часа: 1000₽\n";
                text += "🎫 Базовый 2 часа: 700₽\n";
            }

            text += $"\n💡 Примечания:\n";
            text += $"• Детский билет - для детей от 4 до 12 лет\n";
            text += $"• Дети до 4 лет - бесплатно (с взрослым)\n";
            text += $"• VIP билеты включают дополнительные услуги\n";
            text += $"\n\n🔗 Купить онлайн: yes35.ru";

            return text;
        }
    }
}
