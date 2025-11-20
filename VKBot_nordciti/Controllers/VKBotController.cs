using Microsoft.AspNetCore.Mvc;
using VKBot.Services;

namespace VKBot_nordciti.Controllers
{
    public class VKBotController: ControllerBase
    {
        private readonly   BotService _botService;
        public VKBotController(BotService botService)
        {
            _botService = botService;
        }
        /// <summary>
        /// Test method
        /// </summary>
        /// <returns></returns>
        [HttpGet("Test")]
        public async Task<IActionResult> getTest()
        {
            try
            {


                return Ok("Hi");
            }
            catch (Exception ex)
            {
                return Ok("World");
            }
        }
    }
}
