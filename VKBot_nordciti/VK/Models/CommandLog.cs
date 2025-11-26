using System.ComponentModel.DataAnnotations;

namespace Models
{
    public class CommandLog
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty; // пример: /start
        public string Response { get; set; } = string.Empty; // текст ответа
        public string? KeyboardJson { get; set; } // если null — клавиатуры нет
    
    }
}
