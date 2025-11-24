using System.Text.Json.Serialization;

namespace VK.Models
{
    public class VkEvent
    {
        [JsonPropertyName("message")]
        public MessageItem? Message { get; set; }

        [JsonPropertyName("user_id")]
        public long? UserId { get; set; }
    }

    public class MessageItem
    {
        [JsonPropertyName("from_id")]
        public long FromId { get; set; }

        [JsonPropertyName("peer_id")]
        public long PeerId { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}