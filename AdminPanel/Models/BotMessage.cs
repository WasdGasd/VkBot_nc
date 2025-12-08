// BotMessage.cs
namespace AdminPanel.Models
{
    public class BotMessage
    {
        public long VkUserId { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool IsFromUser { get; set; }
        public DateTime MessageDate { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Username { get; set; }
    }
}