using VK.Models;

namespace Services
{
    public interface IMessageService
    {
        Task ProcessMessageAsync(VkMessage message);
    }
}
