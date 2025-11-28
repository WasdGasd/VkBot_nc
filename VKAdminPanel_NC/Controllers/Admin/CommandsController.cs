using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VKBot_nordciti.Models;
using VKBot_nordciti.Data;

namespace VKAdminPanel_NC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CommandsController : ControllerBase
    {
        private readonly BotDbContext _db;

        public CommandsController(BotDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetCommands()
        {
            var commands = await _db.Commands.ToListAsync();
            return Ok(commands);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetCommand(int id)
        {
            var command = await _db.Commands.FindAsync(id);
            if (command == null) return NotFound();
            return Ok(command);
        }

        [HttpPost]
        public async Task<IActionResult> CreateCommand([FromBody] VKBot_nordciti.Models.Command command)
        {
            _db.Commands.Add(command);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetCommand), new { id = command.Id }, command);
        }


        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCommand(int id, [FromBody] VKBot_nordciti.Models.Command command)
        {
            if (id != command.Id) return BadRequest();

            _db.Commands.Update(command);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCommand(int id)
        {
            var command = await _db.Commands.FindAsync(id);
            if (command == null) return NotFound();

            _db.Commands.Remove(command);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}