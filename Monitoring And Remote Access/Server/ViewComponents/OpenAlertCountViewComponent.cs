using Microsoft.AspNetCore.Mvc;
using Server.Services;

namespace Server.ViewComponents;

/// <summary>
/// Supplies the open alert count for the teacher sidebar badge.
///
/// The layout used to inject the database context and run its own copy of the
/// scope predicate. That copy was not updated when the student scope changed,
/// so the badge and the alerts page reported different totals. Both now go
/// through <see cref="IAnalyticsService"/>, which owns the scope.
/// </summary>
public sealed class OpenAlertCountViewComponent : ViewComponent
{
    private readonly IAnalyticsService _analytics;

    public OpenAlertCountViewComponent(IAnalyticsService analytics) => _analytics = analytics;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var teacherId = HttpContext.Session.GetInt32("TeacherId");
        if (!teacherId.HasValue)
        {
            return View(0);
        }

        var count = await _analytics.GetOpenAlertGroupCountAsync(teacherId.Value, HttpContext.RequestAborted);
        return View(count);
    }
}
