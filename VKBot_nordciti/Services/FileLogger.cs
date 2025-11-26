using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;

namespace VKBot_nordciti.Services
{
    public class FileLogger
    {
        private readonly string _logFilePath;

        public FileLogger(IConfiguration configuration)
        {
            var logFolder = configuration["Logging:Folder"] ?? "logs";
            _logFilePath = Path.Combine(logFolder, "bot.log");

            // Создаем папку для логов если её нет
            Directory.CreateDirectory(logFolder);
        }

        public void Info(string message)
        {
            Log("INFO", message);
        }

        public void Warn(string message)
        {
            Log("WARN", message);
        }

        public void Error(Exception ex, string context)
        {
            Log("ERROR", $"{context}: {ex.Message}\n{ex.StackTrace}");
        }

        private void Log(string level, string message)
        {
            var logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
            Console.WriteLine(logMessage);

            try
            {
                File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);
            }
            catch (Exception)
            {
                // Ignore file errors
            }
        }
    }
}