using Microsoft.Data.Sqlite;
using Dapper;

namespace AdminPanel.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService(IConfiguration configuration)
        {
            // Путь к БД бота (можно вынести в appsettings.json)
            string dbPath = @"C:\Users\pog\Desktop\VkBot_nc\VKBot_nordciti\vkbot.db";
            _connectionString = $"Data Source={dbPath}";
        }

        // Получить все команды из БД бота
        public async Task<List<Command>> GetCommandsAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var commands = await connection.QueryAsync<Command>(
                "SELECT Id, Name, Triggers, Response, KeyboardJson, CommandType FROM Commands");

            return commands.ToList();
        }

        // Добавить новую команду
        public async Task<int> AddCommandAsync(Command command)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                INSERT INTO Commands (Name, Triggers, Response, KeyboardJson, CommandType)
                VALUES (@Name, @Triggers, @Response, @KeyboardJson, @CommandType);
                SELECT last_insert_rowid();";

            var id = await connection.ExecuteScalarAsync<int>(sql, command);
            return id;
        }

        // Обновить команду
        public async Task<bool> UpdateCommandAsync(Command command)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                UPDATE Commands 
                SET Name = @Name, 
                    Triggers = @Triggers, 
                    Response = @Response,
                    KeyboardJson = @KeyboardJson,
                    CommandType = @CommandType
                WHERE Id = @Id";

            var affectedRows = await connection.ExecuteAsync(sql, command);
            return affectedRows > 0;
        }

        // Удалить команду
        public async Task<bool> DeleteCommandAsync(int id)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "DELETE FROM Commands WHERE Id = @Id";
            var affectedRows = await connection.ExecuteAsync(sql, new { Id = id });
            return affectedRows > 0;
        }
    }

    // Модель такая же как в боте
    public class Command
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Triggers { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public string? KeyboardJson { get; set; }
        public string CommandType { get; set; } = "text";
    }
}