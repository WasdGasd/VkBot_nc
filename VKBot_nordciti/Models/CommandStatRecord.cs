namespace VKBot_nordciti.Models
{
    public class CommandStatRecord
    {
        public int Id { get; set; }
        public string CommandName { get; set; } = string.Empty;
        public int UsageCount { get; set; }
        public DateTime Date { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}