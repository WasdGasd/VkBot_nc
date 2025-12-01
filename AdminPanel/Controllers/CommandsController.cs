using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace AdminPanel.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CommandsController : ControllerBase
    {
        private readonly string _dbPath = @"C:\Users\pog\Desktop\VkBot_nc\VKBot_nordciti\vkbot.db";

        [HttpGet]
        public IActionResult GetCommands()
        {
            try
            {
                var commands = new List<object>();

                using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    connection.Open();
                    var command = new SqliteCommand("SELECT * FROM Commands", connection);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            commands.Add(new
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                Triggers = reader.GetString(2),
                                Response = reader.GetString(3),
                                KeyboardJson = reader.IsDBNull(4) ? null : reader.GetString(4),
                                CommandType = reader.GetString(5)
                            });
                        }
                    }
                }

                return Ok(new
                {
                    success = true,
                    count = commands.Count,
                    commands = commands
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    error = ex.Message,
                    message = "Ошибка при чтении таблицы Commands"
                });
            }
        }

        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok(new
            {
                message = "API работает!",
                dbPath = _dbPath,
                exists = System.IO.File.Exists(_dbPath),
                timestamp = DateTime.Now
            });
        }
    }
}