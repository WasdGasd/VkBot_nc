using System.Text;

namespace Services
{
    // Очень простой файловый логгер для отладки
    public class FileLogger
    {
        private readonly string _path;
        private readonly object _lock = new();

        public FileLogger(IConfiguration config)
        {
            var logsFolder = config["Logging:Folder"] ?? "logs";
            if (!Directory.Exists(logsFolder)) Directory.CreateDirectory(logsFolder);
            _path = Path.Combine(logsFolder, "bot.log");
        }

        private void Write(string level, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
            lock (_lock)
            {
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
        }

        public void Info(string message) => Write("INFO", message);
        public void Warn(string message) => Write("WARN", message);
        public void Error(string message) => Write("ERROR", message);
        public void Error(Exception ex, string context = "")
        {
            Write("ERROR", $"{context} {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
