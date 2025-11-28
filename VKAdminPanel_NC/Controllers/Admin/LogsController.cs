using Microsoft.AspNetCore.Mvc;

namespace VKAdminPanel_NC.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LogsController : ControllerBase
    {
        private readonly LogsService _logs;

        public LogsController(LogsService logs)
        {
            _logs = logs;
        }

        [HttpGet]
        public IActionResult GetLogs([FromQuery] int count = 200)
        {
            var lines = _logs.ReadLastLines(count);
            return Ok(lines);
        }
    }
}