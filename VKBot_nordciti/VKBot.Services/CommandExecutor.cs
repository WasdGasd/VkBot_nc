using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace VKB_WA.Services
{
    public class CommandExecutor
    {
        private readonly ILogger<CommandExecutor> _logger;

        public CommandExecutor(ILogger<CommandExecutor> logger)
        {
            _logger = logger;
        }

        public Task ProcessCommandsAsync()
        {
            _logger.LogInformation("Processing commands...");
            return Task.CompletedTask;
        }
    }
}
