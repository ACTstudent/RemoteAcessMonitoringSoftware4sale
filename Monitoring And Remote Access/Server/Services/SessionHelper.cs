using Microsoft.AspNetCore.Http;

namespace Server.Services
{
    public static class SessionHelper
    {
        public static bool IsAdmin(this HttpContext context)
        {
            return context.Session.GetString("Role") == "Admin" && context.Session.GetInt32("AdminId").HasValue;
        }

        public static bool IsTeacher(this HttpContext context)
        {
            return context.Session.GetString("Role") == "Teacher" && context.Session.GetInt32("TeacherId").HasValue;
        }

        public static bool IsStudent(this HttpContext context)
        {
            return context.Session.GetString("Role") == "Student" && context.Session.GetInt32("StudentId").HasValue;
        }

        public static bool IsAuthenticated(this HttpContext context)
        {
            return context.IsAdmin() || context.IsTeacher() || context.IsStudent();
        }
    }
}
