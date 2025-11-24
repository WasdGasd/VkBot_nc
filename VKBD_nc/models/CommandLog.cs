using System;

namespace Models
{
    public class CommandLog
    {
        public int Id { get; set; }
        public long UserId { get; set; }
        public string Command { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
