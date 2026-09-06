using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Shared.Contracts;
using Server.Models;

namespace Server.Authorization;

public sealed class AdminControllerAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly ApplicationDbContext _context;

    public AdminControllerAuthorizationFilter(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.IsInRole(RoleNames.Admin))
        {
            return;
        }

        if (!user.IsInRole(RoleNames.Teacher) ||
            !int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var teacherId))
        {
            context.Result = new ForbidResult();
            return;
        }

        var isActive = await _context.Teachers.AsNoTracking().AnyAsync(teacher =>
            teacher.TeacherId == teacherId &&
            (teacher.Status == RecordStatus.Active || teacher.Status == null || teacher.Status == string.Empty));
        var isSharedAction = context.ActionDescriptor is ControllerActionDescriptor action &&
            action.MethodInfo.IsDefined(typeof(TeacherSharedActionAttribute), inherit: true);

        if (!isActive || !isSharedAction)
        {
            context.Result = new ForbidResult();
        }
    }
}
