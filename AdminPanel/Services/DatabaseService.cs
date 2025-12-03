using Microsoft.Data.Sqlite;

namespace AdminPanel.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService(IConfiguration configuration)
        {
            // Путь к БД бота
            string dbPath = @"C:\Users\kde\source\repos\VkBot_nordciti\VKBot_nordciti\vkbot.db";
            _connectionString = $"Data Source={dbPath}";
        }

        // Получить все команды из БД бота
        public async Task<List<Models.Command>> GetCommandsAsync()
        {
            var commands = new List<Models.Command>();

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = new SqliteCommand("SELECT Id, Name, Triggers, Response, KeyboardJson, CommandType FROM Commands", connection);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                commands.Add(new Models.Command
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Triggers = reader.GetString(2),
                    Response = reader.GetString(3),
                    KeyboardJson = reader.IsDBNull(4) ? null : reader.GetString(4),
                    CommandType = reader.GetString(5)
                });
            }

            return commands;
        }

        // Добавить новую команду
        public async Task<int> AddCommandAsync(Models.Command command)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                INSERT INTO Commands (Name, Triggers, Response, KeyboardJson, CommandType)
                VALUES (@Name, @Triggers, @Response, @KeyboardJson, @CommandType);
                SELECT last_insert_rowid();";

            var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Name", command.Name);
            cmd.Parameters.AddWithValue("@Triggers", command.Triggers);
            cmd.Parameters.AddWithValue("@Response", command.Response);
            cmd.Parameters.AddWithValue("@KeyboardJson", command.KeyboardJson ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@CommandType", command.CommandType);

            var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            return id;
        }

        // Обновить команду
        public async Task<bool> UpdateCommandAsync(Models.Command command)
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

            var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Id", command.Id);
            cmd.Parameters.AddWithValue("@Name", command.Name);
            cmd.Parameters.AddWithValue("@Triggers", command.Triggers);
            cmd.Parameters.AddWithValue("@Response", command.Response);
            cmd.Parameters.AddWithValue("@KeyboardJson", command.KeyboardJson ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@CommandType", command.CommandType);

            var affectedRows = await cmd.ExecuteNonQueryAsync();
            return affectedRows > 0;
        }

        // Удалить команду
        public async Task<bool> DeleteCommandAsync(int id)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "DELETE FROM Commands WHERE Id = @Id";
            var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Id", id);

            var affectedRows = await cmd.ExecuteNonQueryAsync();
            return affectedRows > 0;
        }
    }
}