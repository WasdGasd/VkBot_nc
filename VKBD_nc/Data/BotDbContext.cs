using Microsoft.EntityFrameworkCore;
using VKBD_nc.models;

namespace VKBD_nc.Data
{
    public class BotDbContext : DbContext
    {
        public DbSet<CommandLog> CommandLogs { get; set; }
        public DbSet<UserSession> UserSessions { get; set; }

        public BotDbContext(DbContextOptions<BotDbContext> options) : base(options)
        {
        }
    }
}
