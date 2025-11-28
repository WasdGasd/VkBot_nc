using System.Text.Json.Serialization;

namespace VKAdminPanel_NC.Models
{
    public class BotStats
    {
        [JsonPropertyName("totalUsers")]
        public int TotalUsers { get; set; }

        [JsonPropertyName("activeUsers")]
        public int ActiveUsers { get; set; }

        [JsonPropertyName("activeToday")]
        public int ActiveToday { get; set; }

        [JsonPropertyName("commandsExecuted")]
        public int CommandsExecuted { get; set; }

        [JsonPropertyName("messagesProcessed")]
        public int MessagesProcessed { get; set; }

        [JsonPropertyName("errorsToday")]
        public int ErrorsToday { get; set; }

        [JsonPropertyName("uptime")]
        public string Uptime { get; set; } = "0h 0m";

        [JsonPropertyName("startTime")]
        public DateTime StartTime { get; set; }
    }

    public class CommandStats
    {
        [JsonPropertyName("totalExecuted")]
        public int TotalExecuted { get; set; }

        [JsonPropertyName("dailyUsage")]
        public List<CommandUsage> DailyUsage { get; set; } = new();

        [JsonPropertyName("popularCommands")]
        public List<PopularCommand> PopularCommands { get; set; } = new();
    }

    public class CommandUsage
    {
        [JsonPropertyName("date")]
        public string Date { get; set; } = "";

        [JsonPropertyName("count")]
        public int Count { get; set; }
    }

    public class PopularCommand
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("usageCount")]
        public int UsageCount { get; set; }
    }

    public class SystemStats
    {
        [JsonPropertyName("responseTime")]
        public string ResponseTime { get; set; } = "0ms";

        [JsonPropertyName("memoryUsage")]
        public string MemoryUsage { get; set; } = "0%";

        [JsonPropertyName("cpuLoad")]
        public string CpuLoad { get; set; } = "0%";

        [JsonPropertyName("uptime")]
        public string Uptime { get; set; } = "0h 0m";
    }
}