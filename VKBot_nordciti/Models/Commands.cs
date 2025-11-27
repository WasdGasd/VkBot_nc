namespace VKBot_nordciti.Models
{
    public class Command
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty; // основное название команды
        public string Triggers { get; set; } = string.Empty; // слова-триггеры через запятую
        public string Response { get; set; } = string.Empty;  // текст ответа
        public string? KeyboardJson { get; set; } // если команда использует клавиатуру
        public string CommandType { get; set; } = "text";
    }
}