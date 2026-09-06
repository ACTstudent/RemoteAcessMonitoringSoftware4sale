using Microsoft.AspNetCore.Http;
using Shared.Contracts;

namespace Server.Services
{
    public static class SessionHelper
    {
        public static bool IsAdmin(this HttpContext context)
        {
            return context.Session.GetString("Role") == RoleNames.Admin && context.Session.GetInt32("AdminId").HasValue;
        }

        public static bool IsTeacher(this HttpContext context)
        {
            return context.Session.GetString("Role") == RoleNames.Teacher && context.Session.GetInt32("TeacherId").HasValue;
        }

        public static bool IsStudent(this HttpContext context)
        {
            return context.Session.GetString("Role") == RoleNames.Student && context.Session.GetInt32("StudentId").HasValue;
        }

        public static bool IsAuthenticated(this HttpContext context)
        {
            return context.IsAdmin() || context.IsTeacher() || context.IsStudent();
        }
    }
}
