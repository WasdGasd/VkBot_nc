namespace AdminPanel.Models
{
    public class User
    {
        public int Id { get; set; }
        public long VkUserId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Username { get; set; }
        public bool IsActive { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastActivity { get; set; }
        public int MessageCount { get; set; }
        public DateTime RegistrationDate { get; set; }
        public bool IsBanned { get; set; }
        public string? Status { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Location { get; set; }
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

    public class UserWithMessages
    {
        public User User { get; set; } = new();
        public int MessagesCount { get; set; }
        public DateTime LastMessageDate { get; set; }
        public List<string> MessagesPreview { get; set; } = new();
        public bool HasRecentMessages { get; set; }
        public string LastMessagePreview =>
            MessagesPreview.FirstOrDefault()?.Length > 50
            ? MessagesPreview.FirstOrDefault()?.Substring(0, 50) + "..."
            : MessagesPreview.FirstOrDefault() ?? "Нет сообщений";
    }
}