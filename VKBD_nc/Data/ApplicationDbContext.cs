using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using VKBD_nc.Models;

namespace VKBD_nc.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<BotCommand> BotCommands { get; set; }
        public DbSet<BotStats> BotStats { get; set; }
        public DbSet<ErrorLog> ErrorLogs { get; set; }
        //public DbSet<VkModels> VkModels { get; set; }
        public DbSet<VkSettings> VkSettings { get; set; }
    }
}