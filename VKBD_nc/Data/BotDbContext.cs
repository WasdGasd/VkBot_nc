using Microsoft.EntityFrameworkCore;
using VKBot_nordciti.Models;

namespace VKBD_nc.Data
{
    public class BotDbContext : DbContext
    {
        public BotDbContext(DbContextOptions<BotDbContext> options) : base(options) { }

        public DbSet<Command> Commands { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Command>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Response).IsRequired();
                entity.Property(e => e.CommandType).HasMaxLength(50).HasDefaultValue("text");

                entity.Property(e => e.Triggers)
                    .HasConversion(
                        v => string.Join(';', v),
                        v => v.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    .HasMaxLength(500);
            });
        }
    }
}