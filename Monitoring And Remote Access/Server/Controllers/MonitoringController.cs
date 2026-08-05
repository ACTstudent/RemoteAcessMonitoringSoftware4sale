using Microsoft.AspNetCore.Mvc;

namespace Server.Controllers
{
    public class MonitoringController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
