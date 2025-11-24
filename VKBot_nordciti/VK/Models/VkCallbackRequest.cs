using System.Text.Json.Serialization;

namespace VK.Models
{
    public class VkCallbackRequest
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("object")]
        public VkEvent? Object { get; set; }

        [JsonPropertyName("secret")]
        public string Secret { get; set; } = "";

        [JsonPropertyName("group_id")]
        public long GroupId { get; set; }
    }
}