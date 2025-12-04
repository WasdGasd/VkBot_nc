using Microsoft.AspNetCore.Mvc;

namespace AdminPanel.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Dashboard()
        {
            return View();
        }

        public IActionResult Broadcast()
        {
            return View();
        }

        public IActionResult Users()
        {
            return View();
        }

        public IActionResult Error()
        {
            return View();
        }

        public IActionResult Settings()
        {
            return View();
        }
    }
}