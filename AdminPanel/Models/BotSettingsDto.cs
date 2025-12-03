namespace AdminPanel.Models
{
    public class BotSettingsDto
    {
        public int Id { get; set; }
        public string BotName { get; set; } = "VK Бот";
        public string VkToken { get; set; } = "";
        public string GroupId { get; set; } = "";
        public bool AutoStart { get; set; } = true;
        public bool NotifyNewUsers { get; set; } = true;
        public bool NotifyErrors { get; set; } = true;
        public string NotifyEmail { get; set; } = "";
        public DateTime LastUpdated { get; set; }
    }
}