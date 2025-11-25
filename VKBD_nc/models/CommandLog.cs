using System;

namespace VKBD_nc.models
{
    public class CommandLog
    {
        public int Id { get; set; }
        public long UserId { get; set; }
        public string Command { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
