using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using VKBot.Web.Models;

namespace VKB_WA.Services
{ 
    public class ErrorLogger
    {
        private readonly ILogger<ErrorLogger> _logger;
        private readonly string _connectionString;

        public ErrorLogger(ILogger<ErrorLogger> logger)
        {
            _logger = logger;

            // Создаем базу данных в папке logs
            var currentDir = Directory.GetCurrentDirectory();
            var dbPath = Path.Combine(currentDir, "logs", "errors.db");

            // Создаем директорию если не существует
            var logDir = Path.GetDirectoryName(dbPath);
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir!);
            }

            _connectionString = $"Data Source={dbPath}";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var createTableCommand = @"
                CREATE TABLE IF NOT EXISTS error_logs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                    error_message TEXT NOT NULL,
                    user_id INTEGER,
                    command TEXT,
                    additional_data TEXT
                )";

                using var command = new SqliteCommand(createTableCommand, connection);
                command.ExecuteNonQuery();
                _logger.LogInformation("✅ SQLite таблица error_logs готова");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка при создании таблицы error_logs");
            }
        }

        public async Task LogErrorAsync(Exception error,
                      long? userId = null,
                      string? command = null,
                      object? additionalData = null)
        {
            try
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var additionalDataJson = additionalData != null
                    ? JsonSerializer.Serialize(additionalData, new JsonSerializerOptions
                    {
                        WriteIndented = false,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    })
                    : null;

                var sqlQuery = @"
                INSERT INTO error_logs 
                (error_message, user_id, command, additional_data)
                VALUES (@errorMessage, @userId, @command, @additionalData)";

                await using var dbCommand = new SqliteCommand(sqlQuery, connection);
                dbCommand.Parameters.AddWithValue("@errorMessage", error.Message);
                dbCommand.Parameters.AddWithValue("@userId", userId ?? (object)DBNull.Value);
                dbCommand.Parameters.AddWithValue("@command", command ?? (object)DBNull.Value);
                dbCommand.Parameters.AddWithValue("@additionalData", additionalDataJson ?? (object)DBNull.Value);

                await dbCommand.ExecuteNonQueryAsync();
                _logger.LogInformation("📝 Ошибка записана в БД: {ErrorMessage}", error.Message);
            }
            catch (Exception dbError)
            {
                _logger.LogError(dbError, "❌ Не удалось записать ошибку в БД. Исходная ошибка: {OriginalError}", error.Message);
            }
        }

        // Перегруженный метод для совместимости с текущим кодом
        public async Task LogErrorAsync(Exception ex, string level = "ERROR", long? userId = null, object? additional = null)
        {
            string? command = null;

            // Извлекаем command из additional данных если возможно
            if (additional != null)
            {
                try
                {
                    var additionalJson = JsonSerializer.Serialize(additional);
                    var additionalDict = JsonSerializer.Deserialize<Dictionary<string, object>>(additionalJson);

                    if (additionalDict != null)
                    {
                        if (additionalDict.ContainsKey("command"))
                        {
                            command = additionalDict["command"]?.ToString();
                        }
                        else if (additionalDict.ContainsKey("Message"))
                        {
                            command = additionalDict["Message"]?.ToString();
                        }
                    }
                }
                catch
                {
                    // Если не получилось - используем значения по умолчанию
                }
            }

            await LogErrorAsync(ex, userId, command, additional);
        }

        public async Task<List<ErrorLog>> GetRecentErrorsAsync(int limit = 10)
        {
            var errors = new List<ErrorLog>();
            try
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var selectCommand = @"
                SELECT timestamp, error_message, user_id, command, additional_data
                FROM error_logs 
                ORDER BY timestamp DESC 
                LIMIT @limit";

                await using var command = new SqliteCommand(selectCommand, connection);
                command.Parameters.AddWithValue("@limit", limit);

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    errors.Add(new ErrorLog
                    {
                        Timestamp = reader.GetDateTime(0),
                        ErrorMessage = reader.GetString(1),
                        UserId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                        Command = reader.IsDBNull(3) ? null : reader.GetString(3),
                        AdditionalData = reader.IsDBNull(4) ? null : reader.GetString(4)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка при получении ошибок из БД");
            }
            return errors;
        }
    }
}
