using System.Collections.Concurrent;

namespace VKBot_nordciti.Services
{
    public interface IBotStatsService
    {
        void RegisterUserMessage(long userId, string message);
        void RegisterBotMessage(long userId, string message);
        void RegisterCommandUsage(long userId, string command);
        void UpdateUserActivity(long userId, bool isOnline);

        BotStats GetStats();
        Dictionary<string, int> GetCommandStats();
        List<UserActivity> GetHourlyActivity();
    }

    public class BotStatsService : IBotStatsService
    {
        private readonly ConcurrentDictionary<long, UserStat> _userStats = new();
        private readonly ConcurrentDictionary<string, int> _commandStats = new();
        private readonly ConcurrentDictionary<int, int> _hourlyMessages = new();

        private DateTime _startTime = DateTime.Now;
        private int _totalMessages = 0;
        private int _totalCommands = 0;

        public void RegisterUserMessage(long userId, string message)
        {
            _totalMessages++;
            var hour = DateTime.Now.Hour;
            _hourlyMessages.AddOrUpdate(hour, 1, (_, count) => count + 1);

            _userStats.AddOrUpdate(userId,
                new UserStat
                {
                    UserId = userId,
                    MessagesCount = 1,
                    LastActivity = DateTime.Now,
                    IsOnline = true
                },
                (_, stat) =>
                {
                    stat.MessagesCount++;
                    stat.LastActivity = DateTime.Now;
                    stat.IsOnline = true;
                    return stat;
                });
        }

        public void RegisterBotMessage(long userId, string message)
        {
            _totalMessages++;
            var hour = DateTime.Now.Hour;
            _hourlyMessages.AddOrUpdate(hour, 1, (_, count) => count + 1);
        }

        public void RegisterCommandUsage(long userId, string command)
        {
            _totalCommands++;

            string normalizedCommand = command.ToLower().Trim();

            Console.WriteLine($"🔍 Original command: '{command}'");
            Console.WriteLine($"🔍 Normalized: '{normalizedCommand}'");

            // Проверяем эмодзи которые приходят как ??
            if (normalizedCommand.Contains("??") || normalizedCommand.Contains("ℹ?"))
            {
                // Это эмодзи-кнопки
                if (normalizedCommand.Contains("?? к информации") || normalizedCommand.Contains("ℹ? к информации"))
                {
                    normalizedCommand = "информация";
                }
                else if (normalizedCommand.Contains("?? время работы") || normalizedCommand.Contains("🕒"))
                {
                    normalizedCommand = "время_работы";
                }
                else if (normalizedCommand.Contains("?? главное меню") || normalizedCommand.Contains("?? назад"))
                {
                    normalizedCommand = "назад";
                }
                else if (normalizedCommand.Contains("?? билеты") || normalizedCommand.Contains("🎫"))
                {
                    normalizedCommand = "билеты";
                }
                else if (normalizedCommand.Contains("📊") || normalizedCommand.Contains("загруженность"))
                {
                    normalizedCommand = "загруженность";
                }
                else
                {
                    normalizedCommand = "кнопка";
                }
            }
            // Проверяем обычные эмодзи
            else if (normalizedCommand.Contains("🔙") ||
                     normalizedCommand.Contains("📅") ||
                     normalizedCommand.Contains("📊") ||
                     normalizedCommand.Contains("ℹ️") ||
                     normalizedCommand.Contains("🎫") ||
                     normalizedCommand.Contains("🕒") ||
                     normalizedCommand.Contains("📞") ||
                     normalizedCommand.Contains("📍") ||
                     normalizedCommand.Contains("🎯") ||
                     normalizedCommand.Contains("💳") ||
                     normalizedCommand.Contains("👤") ||
                     normalizedCommand.Contains("👶"))
            {
                // Убираем эмодзи и оставляем текст
                normalizedCommand = RemoveEmojis(normalizedCommand).Trim();

                if (string.IsNullOrEmpty(normalizedCommand))
                {
                    normalizedCommand = "кнопка";
                }
                else
                {
                    // УБЕРИ button_ префикс!
                    normalizedCommand = normalizedCommand.Replace("button_", "");
                }
            }
            // Если это текстовая команда
            else if (normalizedCommand.StartsWith("/") ||
                     normalizedCommand.Contains("билет") ||
                     normalizedCommand.Contains("загруженность") ||
                     normalizedCommand.Contains("информация") ||
                     normalizedCommand.Contains("начать") ||
                     normalizedCommand.Contains("меню") ||
                     normalizedCommand.Contains("помощь") ||
                     normalizedCommand.Contains("время") ||
                     normalizedCommand.Contains("контакт"))
            {
                // Оставляем как есть, но красиво форматируем
                if (normalizedCommand.Contains("время"))
                {
                    normalizedCommand = "время_работы";
                }
                else if (normalizedCommand.Contains("контакт"))
                {
                    normalizedCommand = "контакты";
                }
                else if (normalizedCommand.StartsWith("/"))
                {
                    // Команды с / оставляем
                }
            }
            else
            {
                // Простое сообщение
                normalizedCommand = "сообщение";
            }

            Console.WriteLine($"📊 Final command: '{normalizedCommand}'");
            _commandStats.AddOrUpdate(normalizedCommand, 1, (_, count) => count + 1);

            // Обновляем активность
            _userStats.AddOrUpdate(userId,
                new UserStat
                {
                    UserId = userId,
                    LastActivity = DateTime.Now,
                    IsOnline = true
                },
                (_, stat) =>
                {
                    stat.LastActivity = DateTime.Now;
                    stat.IsOnline = true;
                    return stat;
                });
        }

        private string RemoveEmojis(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Убираем эмодзи и их ?? представление
            string[] emojis = {
        "🔙", "📅", "📊", "ℹ️", "🎫", "🕒", "📞", "📍", "🎯", "💳", "👤", "👶",
        "??", "ℹ?", "🕒?", "📊?", "🎫?"  // ?? версии эмодзи
    };

            foreach (var emoji in emojis)
            {
                text = text.Replace(emoji, "");
            }

            // Убираем "к" или другие короткие слова после эмодзи
            text = text.Replace(" к информации", " информация");
            text = text.Replace(" время работы", " время_работы");
            text = text.Replace(" главное меню", " назад");

            return text.Trim();
        }

        public void UpdateUserActivity(long userId, bool isOnline)
        {
            _userStats.AddOrUpdate(userId,
                new UserStat { UserId = userId, IsOnline = isOnline, LastActivity = DateTime.Now },
                (_, stat) =>
                {
                    stat.IsOnline = isOnline;
                    stat.LastActivity = DateTime.Now;
                    return stat;
                });
        }

        public BotStats GetStats()
        {
            var now = DateTime.Now;
            var today = DateTime.Today;

            var activeToday = _userStats.Values.Count(u => u.LastActivity.Date == today);
            var onlineNow = _userStats.Values.Count(u => u.IsOnline);

            // Подсчет сообщений за последний час
            var lastHour = (now.Hour - 1 + 24) % 24;
            var messagesLastHour = _hourlyMessages.TryGetValue(lastHour, out var count) ? count : 0;

            // Подсчет команд за сегодня
            var commandsToday = _commandStats.Values.Sum();

            return new BotStats
            {
                TotalUsers = _userStats.Count,
                ActiveUsersToday = activeToday,
                OnlineUsers = onlineNow,
                TotalMessages = _totalMessages,
                MessagesLastHour = messagesLastHour,
                TotalCommands = _totalCommands,  // Это ВСЕ команды (включая кнопки)
                Uptime = now - _startTime,
                LastUpdate = now
            };
        }

        public Dictionary<string, int> GetCommandStats()
        {
            return new Dictionary<string, int>(_commandStats);
        }

        public List<UserActivity> GetHourlyActivity()
        {
            var result = new List<UserActivity>();
            var now = DateTime.Now;

            for (int i = 23; i >= 0; i--)
            {
                var hour = (now.Hour - i + 24) % 24;
                var count = _hourlyMessages.TryGetValue(hour, out var c) ? c : 0;

                result.Add(new UserActivity
                {
                    Time = $"{hour:00}:00",
                    Count = count
                });
            }

            return result;
        }
    }

    public class UserStat
    {
        public long UserId { get; set; }
        public int MessagesCount { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastActivity { get; set; }
    }

    public class BotStats
    {
        public int TotalUsers { get; set; }
        public int ActiveUsersToday { get; set; }
        public int OnlineUsers { get; set; }
        public int TotalMessages { get; set; }
        public int MessagesLastHour { get; set; }
        public int TotalCommands { get; set; }
        public TimeSpan Uptime { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    public class UserActivity
    {
        public string Time { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}
