using Microsoft.AspNetCore.Mvc;
using VKAdminPanel_NC.Services;

[Route("admin/logs")]
public class LogsController : Controller
{
    private readonly LogsService _logs;

    public LogsController(LogsService logs)
    {
        _logs = logs;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        var lines = _logs.ReadLastLines(200);
        return View(lines);
    }
}
