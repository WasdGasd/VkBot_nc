namespace VKBot_nordciti.Models
{
    public class Command
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string[] Triggers { get; set; } = Array.Empty<string>();
        public string Response { get; set; } = string.Empty;
        public string? KeyboardJson { get; set; }
        public string CommandType { get; set; } = "text";
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}