namespace AdminPanel.Models
{
    public class BotApiUserResponse
    {
        public long VkId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Username { get; set; }
        public bool IsActive { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastSeen { get; set; }
        public int MessagesCount { get; set; }
        public DateTime RegisteredAt { get; set; }
        public bool IsBanned { get; set; }
        public string? Status { get; set; }
    }

    public class BotApiUserListResponse
    {
        public List<BotApiUserResponse> Users { get; set; } = new();
        public int TotalCount { get; set; }
        public int ActiveCount { get; set; }
        public int OnlineCount { get; set; }
        public int NewTodayCount { get; set; }
    }

    public class BotApiUserDetailResponse
    {
        public BotApiUserResponse User { get; set; } = new();
        public List<BotApiMessage> Messages { get; set; } = new();
        public Dictionary<string, object> Stats { get; set; } = new();
    }

    public class BotApiMessage
    {
        public int Id { get; set; }
        public long VkId { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool IsFromUser { get; set; }
        public DateTime SentAt { get; set; }
    }

    public class BotApiMessageListResponse
    {
        public List<BotApiMessage> Messages { get; set; } = new();
        public int TotalCount { get; set; }
    }
}