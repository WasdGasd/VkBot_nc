using System.Text.Json;

public static class ExternalApiSimulators
{
    public static Task<string> GetParkLoadAsync()
    {
        var text = "📊 Загруженность аквапарка:\n\n👥 Количество посетителей: 420 чел.\n📈 Загруженность: 48%\n\n💡 Рекомендация: Сейчас хорошее время для визита.";
        return Task.FromResult(text);
    }

    public static Task<(string, string)> GetSessionsForDateAsync(string date)
    {
        string text = $"🎟 Доступные сеансы на {date}:\n\n⏰ 10:00 — Свободно\n⏰ 13:00 — Мало мест\n⏰ 16:00 — Есть места";
        // Keyboard with times and back
        var keyboard = new Services.KeyboardProvider().TicketsDateKeyboard(); // reuse provider for simplicity
        // But better create a sessions keyboard
        string sessionsKeyboard = JsonSerializer.Serialize(new
        {
            one_time = true,
            buttons = new[]
            {
                new[] { new { action = new { type = "text", label = "⏰ 10:00" }, color = "primary" } },
                new[] { new { action = new { type = "text", label = "⏰ 13:00" }, color = "primary" } },
                new[] { new { action = new { type = "text", label = "⏰ 16:00" }, color = "primary" } },
                new[] { new { action = new { type = "text", label = "🔙 Назад" }, color = "negative" } }
            }
        });
        return Task.FromResult((text, sessionsKeyboard));
    }

    public static Task<(string, string)> GetFormattedTariffsAsync(string date, string session, string category)
    {
        // simulate tariffs list
        string title = category == "child" ? "👶 ДЕТСКИЕ БИЛЕТЫ" : "👤 ВЗРОСЛЫЕ БИЛЕТЫ";
        string text = $"🎟 *{title}*\n⏰ Сеанс: {session}\n📅 Дата: {date}\n\n💰 Стоимость:\n• Стандарт: 1000₽\n• VIP: 2500₽\n\n🔗 Купить: https://yes35.ru/aquapark/tickets";
        string keyboard = JsonSerializer.Serialize(new
        {
            one_time = false,
            buttons = new[]
            {
                new[] { new { action = new { type = "text", label = "💳 Оплатить" }, color = "positive" } },
                new[] { new { action = new { type = "text", label = "🔙 К сеансам" }, color = "secondary" }, new { action = new { type = "text", label = "🔙 В начало" }, color = "negative" } }
            }
        });

        return Task.FromResult((text, keyboard));
    }
}
