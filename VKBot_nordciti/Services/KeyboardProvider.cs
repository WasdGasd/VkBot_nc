using System.Text.Json;

namespace BotServices
{
    public class KeyboardProvider
    {
        private readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = null };

        public string MainMenu() => JsonSerializer.Serialize(new
        {
            one_time = false,
            buttons = new[]
            {
                new[] {
                    new { action = new { type = "text", label = "📅 Билеты" }, color = "primary" },
                    new { action = new { type = "text", label = "ℹ️ Информация" }, color = "secondary" }
                },
                new[] { new { action = new { type = "text", label = "📊 Загруженность" }, color = "positive" } }
            }
        }, _opts);

        public string InfoMenu() => JsonSerializer.Serialize(new
        {
            one_time = true,
            buttons = new[]
            {
                new[] { new { action = new { type = "text", label = "🕒 Время работы" }, color = "primary" } },
                new[] { new { action = new { type = "text", label = "📞 Контакты" }, color = "primary" } },
                new[] { new { action = new { type = "text", label = "📍 Как добраться" }, color = "primary" } },
                new[] { new { action = new { type = "text", label = "🔙 Главное меню" }, color = "negative" } }
            }
        }, _opts);

        public string TicketsDateKeyboard()
        {
            var buttons = new List<object[]>();
            for (int i = 0; i < 3; i++)
            {
                var date = DateTime.Now.AddDays(i).ToString("dd.MM.yyyy");
                buttons.Add(new object[] { new { action = new { type = "text", label = $"📅 {date}" }, color = "primary" } });
            }
            buttons.Add(new object[] { new { action = new { type = "text", label = "🔙 Главное меню" }, color = "negative" } });
            return JsonSerializer.Serialize(new { one_time = true, buttons = buttons.ToArray() }, _opts);
        }

        // ===========================================
        // Исправленная клавиатура выбора категории
        // ===========================================
        public string TicketCategoryKeyboard()
        {
            var buttons = new object[][]
            {
                new object[]
                {
                    new { action = new { type = "text", label = "👤 Взрослые" }, color = "primary" }
                },
                new object[]
                {
                    new { action = new { type = "text", label = "👶 Детские" }, color = "primary" }
                },
                new object[]
                {
                    new { action = new { type = "text", label = "🔙 Назад" }, color = "negative" }
                }
            };

            return JsonSerializer.Serialize(new { one_time = true, inline = false, buttons }, _opts);
        }

        public string PaymentKeyboard() => JsonSerializer.Serialize(new
        {
            one_time = true,
            buttons = new[] {
                new[] { new { action = new { type = "text", label = "💳 Оплатить" }, color = "positive" } },
                new[] { new { action = new { type = "text", label = "🔙 Назад" }, color = "negative" } }
            }
        }, _opts);

        public string BackToMain() => JsonSerializer.Serialize(new
        {
            one_time = false,
            buttons = new[] { new[] { new { action = new { type = "text", label = "🔙 Главное меню" }, color = "negative" } } }
        }, _opts);

        public string BackToSessions() => JsonSerializer.Serialize(new
        {
            one_time = true,
            buttons = new[] { new[] { new { action = new { type = "text", label = "🔙 К сеансам" }, color = "negative" } } }
        }, _opts);

        // ===========================================
        // НОВЫЕ МЕТОДЫ ДЛЯ ИНФОРМАЦИОННОГО МЕНЮ
        // ===========================================

        public string WorkingHoursKeyboard() => JsonSerializer.Serialize(new
        {
            one_time = true,
            buttons = new[]
    {
        new[] { new { action = new { type = "text", label = "🔙 К информации" }, color = "secondary" } },
        new[] { new { action = new { type = "text", label = "🔙 Главное меню" }, color = "negative" } }
    }
        }, _opts);

        public string ContactsKeyboard() => JsonSerializer.Serialize(new
        {
            one_time = true,
            buttons = new[]
            {
        new[] { new { action = new { type = "text", label = "🔙 К информации" }, color = "secondary" } },
        new[] { new { action = new { type = "text", label = "🔙 Главное меню" }, color = "negative" } }
    }
        }, _opts);

        public string LocationKeyboard() => JsonSerializer.Serialize(new
        {
            one_time = true,
            buttons = new[]
            {
        new[] { new { action = new { type = "text", label = "🔙 К информации" }, color = "secondary" } },
        new[] { new { action = new { type = "text", label = "🔙 Главное меню" }, color = "negative" } }
    }
        }, _opts);

        // ===========================================
        // ДОПОЛНИТЕЛЬНЫЕ ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
        // ===========================================

        public string SimpleBackKeyboard() => JsonSerializer.Serialize(new
        {
            one_time = true,
            buttons = new[] {
                new[] { new { action = new { type = "text", label = "🔙 Назад" }, color = "negative" } }
            }
        }, _opts);

        public string YesNoKeyboard() => JsonSerializer.Serialize(new
        {
            one_time = true,
            buttons = new[]
            {
                new[] {
                    new { action = new { type = "text", label = "✅ Да" }, color = "positive" },
                    new { action = new { type = "text", label = "❌ Нет" }, color = "negative" }
                },
                new[] { new { action = new { type = "text", label = "🔙 Главное меню" }, color = "secondary" } }
            }
        }, _opts);
    }
}