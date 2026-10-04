using Microsoft.AspNetCore.Mvc;
using Server.Services;

namespace Server.Controllers
{
    public class MonitoringController : Controller
    {
        public IActionResult Index()
        {
            // Route to the appropriate dashboard by role: a teacher's is My
            // Classroom, the page they land on after logging in.
            if (HttpContext.IsTeacher()) return RedirectToAction("Dashboard", "Teacher");
            if (HttpContext.IsAdmin()) return RedirectToAction("Index", "Admin");
            return RedirectToAction("Index", "Student");
        }
    }
}