using VKBot_nordciti.Models;

namespace VKBot_nordciti.Services
{
    public interface ICommandService
    {
        Task<Command?> FindCommandAsync(string messageText);
        Task<List<Command>> GetAllCommandsAsync();
        Task<string> ProcessCommandAsync(Command command, Dictionary<string, string>? parameters = null);
    }
}