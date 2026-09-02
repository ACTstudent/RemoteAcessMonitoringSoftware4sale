using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Server.Data;

namespace Server.Authorization;

public sealed class ActiveTeacherAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly ApplicationDbContext _context;

    public ActiveTeacherAuthorizationFilter(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (!user.IsInRole("Teacher") ||
            !int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var teacherId) ||
            !await _context.Teachers.AsNoTracking().AnyAsync(teacher =>
                teacher.TeacherId == teacherId &&
                (teacher.Status == "Active" || teacher.Status == null || teacher.Status == string.Empty)))
        {
            context.Result = new ForbidResult();
        }
    }
}
