using Microsoft.EntityFrameworkCore;
using Models;
using VKBD_nc.Data;
using VKBD_nc.models;

namespace BotServices
{
    public class CommandService
    {
        private readonly BotDbContext _db;

        public CommandService(BotDbContext db)
        {
            _db = db;
        }

        public async Task<CommandLog?> FindCommandAsync(string text)
        {
            var lower = text.ToLower();

            return await _db.CommandLogs
                .FirstOrDefaultAsync(c => lower.Contains(c.Name.ToLower()));
        }
    }
}
