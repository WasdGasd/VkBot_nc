using VKAdminPanel_NC.Models; 


namespace VKAdminPanel_NC.Services
{
    public class StatsService
    {
        private readonly SimpleStatsService _simpleStats;

        public StatsService(SimpleStatsService simpleStats)
        {
            _simpleStats = simpleStats;
        }

        public BotStats GetBotStats()
        {
            return new BotStats
            {
                TotalUsers = _simpleStats.GetOnlineUsersCount(),
                ActiveUsers = _simpleStats.GetOnlineUsersCount(),
                ActiveToday = _simpleStats.GetActiveUsersToday(),
                MessagesProcessed = _simpleStats.GetTotalMessages(),
                CommandsExecuted = _simpleStats.GetCommandUsage().Values.Sum(),
                ErrorsToday = 0,
                Uptime = $"{(DateTime.Now - _simpleStats.GetStartTime()):h'h 'm'm'}",
                StartTime = _simpleStats.GetStartTime()
            };
        }

        public CommandStats GetCommandStats()
        {
            var commandUsage = _simpleStats.GetCommandUsage();

            return new CommandStats
            {
                TotalExecuted = commandUsage.Values.Sum(),
                DailyUsage = GenerateDailyUsage(),
                PopularCommands = commandUsage
                    .Select(x => new PopularCommand { Name = x.Key, UsageCount = x.Value })
                    .OrderByDescending(x => x.UsageCount)
                    .Take(10)
                    .ToList()
            };
        }

        public SystemStats GetSystemStats()
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();

            return new SystemStats
            {
                ResponseTime = "45ms",
                MemoryUsage = $"{(process.WorkingSet64 / 1024 / 1024):F1} MB",
                CpuLoad = "15%",
                Uptime = $"{(DateTime.Now - _simpleStats.GetStartTime()):h'h 'm'm'}"
            };
        }

        private List<CommandUsage> GenerateDailyUsage()
        {
            var days = new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
            var random = new Random();

            return days.Select(day => new CommandUsage
            {
                Date = day,
                Count = random.Next(20, 100)
            }).ToList();
        }
    }
}