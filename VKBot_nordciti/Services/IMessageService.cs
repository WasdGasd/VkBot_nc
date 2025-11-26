using VKBot_nordciti.VK.Models;

namespace VKBot_nordciti.Services
{
    public interface IMessageService
    {
        Task ProcessMessageAsync(VkMessage message);
    }
}