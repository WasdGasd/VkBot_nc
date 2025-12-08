namespace AdminPanel.Models
{
    public class User
    {
        public int Id { get; set; }
        public long VkUserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public bool IsOnline { get; set; }
        public DateTime LastActivity { get; set; } = DateTime.Now;
        public int MessageCount { get; set; }
        public DateTime RegistrationDate { get; set; } = DateTime.Now;
        public bool IsBanned { get; set; }
        public string Status { get; set; } = "user";
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string? PhotoUrl { get; set; }
        public string? Notes { get; set; }

        // Вычисляемые свойства
        public string FullName => $"{FirstName} {LastName}".Trim();
        public string DisplayName => !string.IsNullOrEmpty(Username) ? $"@{Username}" : FullName;
    }

    public class Message
    {
        public int Id { get; set; }
        public long VkUserId { get; set; }
        public string MessageText { get; set; } = string.Empty;
        public bool IsFromUser { get; set; } = true;
        public DateTime MessageDate { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class UserWithMessages : User
    {
        public int MessagesCount { get; set; }
        public DateTime LastMessageDate { get; set; }
        public List<Message> RecentMessages { get; set; } = new();
        public bool HasRecentMessages { get; set; }
        public string LastMessagePreview =>
            RecentMessages.Any() ?
            (RecentMessages.First().MessageText.Length > 50
                ? RecentMessages.First().MessageText.Substring(0, 50) + "..."
                : RecentMessages.First().MessageText)
            : "Нет сообщений";
    }
}