using Microsoft.AspNetCore.Mvc;

[Route("admin/bot")]
public class BotController : Controller
{
    private readonly BotStateService _bot;

    public BotController(BotStateService bot)
    {
        _bot = bot;
    }

    public IActionResult Index() => View(_bot);

    [HttpPost("enable")]
    public IActionResult Enable() { _bot.Enable(); return RedirectToAction("Index"); }

    [HttpPost("disable")]
    public IActionResult Disable() { _bot.Disable(); return RedirectToAction("Index"); }

    [HttpPost("restart")]
    public IActionResult Restart() { _bot.Restart(); return RedirectToAction("Index"); }
}
