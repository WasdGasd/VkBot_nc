namespace Models
{
    public class Command
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty; // пример: /start
        public string Response { get; set; } = string.Empty; // текст ответа
        public string? KeyboardJson { get; set; } // если null — клавиатуры нет
        public bool IsAdmin { get; set; } // если нужно для модераторов
    }
}
