namespace VK.Models
{
    public class VkMessage
    {
        public long UserId { get; set; }
        public long PeerId { get; set; }
        public string Text { get; set; } = "";
    }
}
