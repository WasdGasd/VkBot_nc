using System.Text.Json;

namespace Services
{
    // Возвращает JSON-клавиатуры для отправки в VK
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
                new[] {
                    new { action = new { type = "text", label = "📊 Загруженность" }, color = "positive" }
                }
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

        public string TicketCategoryKeyboard() => JsonSerializer.Serialize(new
        {
            one_time = true,
            buttons = new[]
            {
                new[] { new { action = new { type = "text", label = "👤 Взрослые билеты" }, color = "primary" },
                        new { action = new { type = "text", label = "👶 Детские билеты" }, color = "positive" } },
                new[] { new { action = new { type = "text", label = "🔙 К сеансам" }, color = "secondary" } }
            }
        }, _opts);

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
            buttons = new[] { new[] { new { action = new { type = "text", label = "🔙 К сеансам" }, color = "secondary" } } }
        }, _opts);
    }
}
