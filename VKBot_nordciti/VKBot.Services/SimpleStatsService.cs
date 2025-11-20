using System.Collections.Concurrent;

namespace VKB_WA.Services
{
    public class SimpleStatsService
    {
        private readonly ConcurrentDictionary<long, DateTime> _userLastActivity = new();
        private int _totalMessagesProcessed = 0;
        private readonly Dictionary<string, int> _commandUsage = new();
        private readonly DateTime _startTime = DateTime.Now;

        // Методы для статистики
        public int GetOnlineUsersCount() => _userLastActivity.Count;
        public int GetTotalMessages() => _totalMessagesProcessed;
        public DateTime GetStartTime() => _startTime;
        public Dictionary<string, int> GetCommandUsage() => new Dictionary<string, int>(_commandUsage);
        public int GetActiveUsersToday() => _userLastActivity.Count(u => u.Value.Date == DateTime.Today);

        // Метод для имитации активности (для тестирования)
        public void SimulateActivity()
        {
            var random = new Random();
            var userId = random.Next(1000, 9999);
            var commands = new[] { "start", "билеты", "информация", "загруженность" };
            var command = commands[random.Next(commands.Length)];

            _userLastActivity[userId] = DateTime.Now;
            _totalMessagesProcessed++;

            if (_commandUsage.ContainsKey(command))
                _commandUsage[command]++;
            else
                _commandUsage[command] = 1;
        }
    }
}