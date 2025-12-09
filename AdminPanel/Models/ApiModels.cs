namespace AdminPanel.Models
{
    // Существующие классы остаются без изменений
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

// Создаем отдельное пространство имен для моделей API бота
namespace AdminPanel.Models.BotApi
{
    // Новые классы для работы с реальным API бота
    public class RealUserResponse
    {
        public long VkId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? Username { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsOnline { get; set; }
        public DateTime LastSeen { get; set; }
        public int MessagesCount { get; set; }
        public DateTime RegisteredAt { get; set; }
        public bool IsBanned { get; set; }
        public string? Status { get; set; }
    }

    public class RealUserListResponse
    {
        public List<RealUserResponse> Users { get; set; } = new();
        public int TotalCount { get; set; }
        public int ActiveCount { get; set; }
        public int OnlineCount { get; set; }
        public int NewTodayCount { get; set; }
    }

    public class RealUserDetailResponse : RealUserResponse
    {
        public List<RealMessage> Messages { get; set; } = new();
        public Dictionary<string, int> Stats { get; set; } = new();
    }

    public class RealMessage
    {
        public int Id { get; set; }
        public long VkId { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool IsFromUser { get; set; } = true;
        public DateTime SentAt { get; set; }
    }

    public class RealMessageListResponse
    {
        public List<RealMessage> Messages { get; set; } = new();
        public int TotalCount { get; set; }
        public int UserCount { get; set; }
        public DateTime FirstMessageDate { get; set; }
        public DateTime LastMessageDate { get; set; }
    }
}