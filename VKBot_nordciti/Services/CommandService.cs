using Microsoft.EntityFrameworkCore;
using Models;
using VKBD_nc.Data;
using VKBD_nc.models;
using CommandLog = VKBD_nc.models.CommandLog;

namespace BotServices
{
    public class CommandService
    {
        private readonly BotDbContext _db;

        public CommandService(BotDbContext db)
        {
            _db = db;
        }

        public async Task<CommandLog?> FindCommandAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            string msg = message.ToLower();

            return await _db.CommandLogs
                .FirstOrDefaultAsync(c => msg.Contains(c.Name.ToLower()));
        }

    }
}
