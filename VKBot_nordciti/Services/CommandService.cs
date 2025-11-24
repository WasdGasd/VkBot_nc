using Microsoft.EntityFrameworkCore;
using Models;

namespace Services
{
    public class CommandService
    {
        private readonly BotDbContext _db;

        public CommandService(BotDbContext db)
        {
            _db = db;
        }

        public async Task<Command?> FindCommandAsync(string text)
        {
            var lower = text.ToLower();

            return await _db.Commands
                .FirstOrDefaultAsync(c => lower.Contains(c.Name.ToLower()));
        }
    }
}
