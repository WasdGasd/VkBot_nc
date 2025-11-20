using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace VKB_WA.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LogsController : ControllerBase
    {
        [HttpGet]
        public IEnumerable<object> Get()
        {
            return new[] { new { message = "Bot started" } };
        }
    }
}
