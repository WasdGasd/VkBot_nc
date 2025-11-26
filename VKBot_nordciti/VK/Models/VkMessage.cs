using System.Text.Json;
using System.Text.Json.Serialization;

namespace VKBot_nordciti.VK.Models
{
    public class VkUpdate
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("object")]
        public JsonElement Object { get; set; }

        [JsonPropertyName("group_id")]
        public long GroupId { get; set; }
    }

    public class VkMessage
    {
        [JsonPropertyName("from_id")]
        public long FromId { get; set; }

        [JsonPropertyName("peer_id")]
        public long PeerId { get; set; }

        [JsonPropertyName("user_id")]
        public long UserId { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("conversation_message_id")]
        public long ConversationMessageId { get; set; }
    }
}