using Microsoft.AspNetCore.Mvc;
using VK.Models;
using Services;

[ApiController]
[Route("api/callback")]
public class CallbackController : ControllerBase
{
    private readonly IMessageService _messageService;
    private readonly IConfiguration _config;

    public CallbackController(IMessageService messageService, IConfiguration config)
    {
        _messageService = messageService;
        _config = config;
    }

    [HttpPost]
    public async Task<IActionResult> Callback([FromBody] VkCallbackRequest request)
    {
        //  if (request.Type=="confirmation" && request.Group_Id==233846417) {
        //  return Ok("a45d3f0a");
        //  }

        // Проверка секрета
        if (!string.IsNullOrEmpty(_config["VkSettings:SecretKey"]) && request.Secret != _config["VkSettings:SecretKey"])
            return Unauthorized();

        switch (request.Type)
        {
            case "confirmation":
                return Ok(_config["VkSettings:ConfirmationToken"] ?? ""); // подтверждение сервера
            case "message_new":
                if (request.Object?.Message != null)
                {
                    var msg = request.Object.Message;
                    await _messageService.ProcessMessageAsync(new VkMessage
                    {
                        UserId = msg.FromId,
                        PeerId = msg.PeerId,
                        Text = msg.Text ?? ""
                    });
                }
                break;
            case "message_allow":
            case "message_deny":
            case "message_reply":
                // обработка при необходимости
                break;
        }

        return Ok("ok");
    }
}
