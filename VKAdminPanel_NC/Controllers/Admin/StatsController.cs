using Microsoft.AspNetCore.Mvc;
using VKAdminPanel_NC.Services;

[Route("admin/stats")]
public class StatsController : Controller
{
    private readonly StatsService _stats;

    public StatsController(StatsService stats)
    {
        _stats = stats;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        var data = _stats.GetLast30Days();
        return View(data);
    }
}
