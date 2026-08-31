using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Server.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/deployment/ping")]
public sealed class DeploymentPingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        Response.Headers.CacheControl = "no-store";
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "unknown";
        return Ok(new { product = "CAMS Server", version, status = "ok", utc = DateTimeOffset.UtcNow });
    }
}
