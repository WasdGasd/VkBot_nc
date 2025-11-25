using VK.Models;

namespace BotServices
{
    public interface IMessageService
    {
        Task ProcessMessageAsync(VkMessage message);
    }
}
