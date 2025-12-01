using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VKBot_nordciti.Models;

namespace VKBot_nordciti.Data
{
    public class BotDbContext : DbContext
    {
        public BotDbContext(DbContextOptions<BotDbContext> options) : base(options) { }

        public DbSet<Command> Commands { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.ApplyConfiguration(new CommandsConfiguration());
        }
    }
    public class CommandsConfiguration : IEntityTypeConfiguration<Command> {
        public void Configure(EntityTypeBuilder<Command> builder)
        {
            builder.ToTable("Commands");
            builder.HasKey(e => e.Id);
        }
    }

}