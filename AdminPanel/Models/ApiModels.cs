namespace AdminPanel.Models
{
    public class UserListResponse
    {
        public List<User> Users { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public int ActiveCount { get; set; }
        public int OnlineCount { get; set; }
        public int NewTodayCount { get; set; }
    }

    public class MessagesStats
    {
        public int TotalMessages { get; set; }
        public int UniqueUsers { get; set; }
        public int FromUserCount { get; set; }
        public int FromBotCount { get; set; }
        public List<DailyMessageStats> DailyStats { get; set; } = new();
        public List<TopUserStats> TopUsers { get; set; } = new();
    }

    public class DailyMessageStats
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
    }

    public class TopUserStats
    {
        public long VkUserId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Username { get; set; }
        public int MessageCount { get; set; }
        public DateTime LastMessageDate { get; set; }
        public string FullName => $"{FirstName} {LastName}".Trim();
    }

    // Переименован в BotMessageImport чтобы избежать конфликта с Message из User.cs
    public class BotMessageImport
    {
        public long VkUserId { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool IsFromUser { get; set; } = true;
        public DateTime MessageDate { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Username { get; set; }
    }
}