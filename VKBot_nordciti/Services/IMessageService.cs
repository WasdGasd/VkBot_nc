using VKBot_nordciti.VK.Models;

namespace VKBot_nordciti.Services
{
    public interface IMessageService
    {
        Task ProcessMessageAsync(VkMessage message);
        Task HandleMessageAllowEvent(long userId);
        Task ProcessButtonClickAsync(long userId, string eventId, string payload);
    }
}