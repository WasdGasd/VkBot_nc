// Controllers/CommandsController.cs
using Microsoft.AspNetCore.Mvc;
using VKBD_nc.Models;
using VKBD_nc.Data;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace VKAdminPanel_NC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CommandsController : ControllerBase
    {
        private static List<BotCommand> _commands = new()
        {
            new BotCommand { Id = 1, Name = "start", Description = "Start the bot", Response = "Welcome! Bot is ready to help you.", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new BotCommand { Id = 2, Name = "help", Description = "Show help information", Response = "Available commands: /start, /help, /status", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new BotCommand { Id = 3, Name = "status", Description = "Check bot status", Response = "🤖 Bot is running normally", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };

        [HttpGet]
        public ActionResult<IEnumerable<BotCommand>> Get()
        {
            return Ok(_commands.OrderBy(c => c.Id).ToList());
        }

        [HttpPost]
        public ActionResult<BotCommand> Create([FromBody] BotCommand cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd.Name) || string.IsNullOrWhiteSpace(cmd.Response))
                return BadRequest(new { error = "Name and Response are required" });

            if (_commands.Any(c => c.Name.ToLower() == cmd.Name.ToLower().Trim()))
                return BadRequest(new { error = "Command with this name already exists" });

            var newId = _commands.Count > 0 ? _commands.Max(c => c.Id) + 1 : 1;

            var newCommand = new BotCommand
            {
                Id = newId,
                Name = cmd.Name.Trim(),
                Description = cmd.Description?.Trim() ?? string.Empty,
                Response = cmd.Response.Trim(),
                IsActive = cmd.IsActive,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _commands.Add(newCommand);
            return Ok(newCommand);
        }

        [HttpPut("{id}")]
        public ActionResult<BotCommand> Update(int id, [FromBody] BotCommand cmd)
        {
            var existingCommand = _commands.FirstOrDefault(c => c.Id == id);
            if (existingCommand == null)
                return NotFound(new { error = "Command not found" });

            if (_commands.Any(c => c.Id != id && c.Name.ToLower() == cmd.Name.ToLower().Trim()))
                return BadRequest(new { error = "Command with this name already exists" });

            existingCommand.Name = cmd.Name.Trim();
            existingCommand.Description = cmd.Description?.Trim() ?? string.Empty;
            existingCommand.Response = cmd.Response.Trim();
            existingCommand.IsActive = cmd.IsActive;
            existingCommand.UpdatedAt = DateTime.UtcNow;

            return Ok(existingCommand);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var command = _commands.FirstOrDefault(c => c.Id == id);
            if (command == null)
                return NotFound(new { error = "Command not found" });

            _commands.Remove(command);
            return Ok(new { message = "Command deleted successfully" });
        }
    }
}