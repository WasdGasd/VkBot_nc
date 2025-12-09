using System.Text.Json.Serialization;

namespace AdminPanel.Models
{
    // Для запроса пользователей
    public class VkUsersApiResponse
    {
        [JsonPropertyName("response")]
        public List<VkUserInfo>? Response { get; set; }

        [JsonPropertyName("error")]
        public VkApiError? Error { get; set; }
    }

    // Для запроса бесед
    public class VkConversationsApiResponse
    {
        [JsonPropertyName("response")]
        public VkConversationsResponse? Response { get; set; }

        [JsonPropertyName("error")]
        public VkApiError? Error { get; set; }
    }

    public class VkUserInfo
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("last_name")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("online")]
        public int Online { get; set; }

        [JsonPropertyName("last_seen")]
        public VkLastSeen? LastSeen { get; set; }

        [JsonPropertyName("photo_100")]
        public string? PhotoUrl { get; set; }

        [JsonPropertyName("photo_200")]
        public string? PhotoUrlLarge { get; set; }

        [JsonPropertyName("domain")]
        public string? Domain { get; set; }

        [JsonPropertyName("bdate")]
        public string? BirthDate { get; set; }

        [JsonPropertyName("city")]
        public VkCity? City { get; set; }

        [JsonPropertyName("country")]
        public VkCountry? Country { get; set; }

        [JsonPropertyName("sex")]
        public int Sex { get; set; }

        [JsonPropertyName("can_write_private_message")]
        public int CanWritePrivateMessage { get; set; }

        public string FullName => $"{FirstName} {LastName}";

        public bool IsOnline => Online == 1;

        public DateTime? LastSeenDate => LastSeen != null
            ? DateTimeOffset.FromUnixTimeSeconds(LastSeen.Time).DateTime
            : null;
    }

    public class VkLastSeen
    {
        [JsonPropertyName("time")]
        public long Time { get; set; }

        [JsonPropertyName("platform")]
        public int Platform { get; set; }
    }

    public class VkCity
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
    }

    public class VkCountry
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
    }

    public class VkApiError
    {
        [JsonPropertyName("error_code")]
        public int ErrorCode { get; set; }

        [JsonPropertyName("error_msg")]
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class VkConversationsResponse
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("items")]
        public List<VkConversation>? Items { get; set; }
    }

    public class VkConversation
    {
        [JsonPropertyName("conversation")]
        public VkConversationInfo? Conversation { get; set; }

        [JsonPropertyName("last_message")]
        public VkLastMessage? LastMessage { get; set; }
    }

    public class VkConversationInfo
    {
        [JsonPropertyName("peer")]
        public VkPeer? Peer { get; set; }

        [JsonPropertyName("in_read")]
        public int InRead { get; set; }

        [JsonPropertyName("out_read")]
        public int OutRead { get; set; }
    }

    public class VkPeer
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("local_id")]
        public int LocalId { get; set; }
    }

    public class VkLastMessage
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("date")]
        public long Date { get; set; }

        [JsonPropertyName("from_id")]
        public long FromId { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("out")]
        public int Out { get; set; }
    }
}