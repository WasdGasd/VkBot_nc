namespace AdminPanel.Models
{
    public class Command
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Triggers { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public string? KeyboardJson { get; set; }
        public string CommandType { get; set; } = "text";
        public DateTime CreatedAt { get; set; }
    }
}