using Microsoft.AspNetCore.Mvc;
using VKAdminPanel_NC.Services;

[ApiController]
[Route("api/[controller]")]
public class StatsController : ControllerBase
{
    private readonly StatsService _stats;
    private readonly SimpleStatsService _simpleStats;

    public StatsController(StatsService stats, SimpleStatsService simpleStats)
    {
        _stats = stats;
        _simpleStats = simpleStats;
    }

    [HttpGet("bot")]
    public IActionResult GetBotStats()
    {
        var stats = _stats.GetBotStats();
        return Ok(stats);
    }

    [HttpGet("commands")]
    public IActionResult GetCommandStats()
    {
        var stats = _stats.GetCommandStats();
        return Ok(stats);
    }

    [HttpGet("system")]
    public IActionResult GetSystemStats()
    {
        var stats = _stats.GetSystemStats();
        return Ok(stats);
    }

    [HttpPost("simulate")]
    public IActionResult SimulateActivity()
    {
        _simpleStats.SimulateActivity();
        return Ok(new { message = "Activity simulated" });
    }
}