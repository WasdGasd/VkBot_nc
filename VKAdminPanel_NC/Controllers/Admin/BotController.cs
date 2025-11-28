using Microsoft.AspNetCore.Mvc;

namespace VKAdminPanel_NC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BotController : ControllerBase
    {
        private readonly BotStateService _bot;

        public BotController(BotStateService bot)
        {
            _bot = bot;
        }

        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            return Ok(new { enabled = _bot.Enabled });
        }

        [HttpPost("enable")]
        public IActionResult Enable()
        {
            _bot.Enable();
            return Ok(new { message = "Bot enabled" });
        }

        [HttpPost("disable")]
        public IActionResult Disable()
        {
            _bot.Disable();
            return Ok(new { message = "Bot disabled" });
        }

        [HttpPost("restart")]
        public IActionResult Restart()
        {
            _bot.Restart();
            return Ok(new { message = "Bot restarted" });
        }
    }
}