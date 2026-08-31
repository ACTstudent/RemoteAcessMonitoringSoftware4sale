using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;

namespace Server.Controllers;

[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
[Route("Admin/Deployment")]
public sealed class AdminDeploymentController : Controller
{
    private readonly IDeploymentService _deployment;

    public AdminDeploymentController(IDeploymentService deployment) => _deployment = deployment;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        NoStore();
        return View("~/Views/AdminDeployment/Index.cshtml", await _deployment.GetStatusAsync(cancellationToken));
    }

    [HttpGet("installer")]
    public Task<IActionResult> Installer(CancellationToken cancellationToken) =>
        Download(() => _deployment.OpenInstallerAsync(cancellationToken));

    [HttpGet("manifest")]
    public Task<IActionResult> Manifest(CancellationToken cancellationToken) =>
        Download(() => _deployment.OpenManifestAsync(cancellationToken));

    [HttpGet("root-certificate")]
    public Task<IActionResult> RootCertificate(CancellationToken cancellationToken) =>
        Download(() => _deployment.OpenRootCertificateAsync(cancellationToken));

    [HttpPost("bundle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Bundle([FromForm] string endpoint, CancellationToken cancellationToken)
    {
        NoStore();
        try
        {
            var bundle = await _deployment.CreateBundleAsync(endpoint, cancellationToken);
            return File(bundle.Stream, "application/zip", bundle.FileName, enableRangeProcessing: false);
        }
        catch (ArgumentException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidDataException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    private async Task<IActionResult> Download(Func<Task<DeploymentAsset?>> open)
    {
        NoStore();
        try
        {
            var asset = await open();
            return asset is null ? NotFound() : File(asset.Stream, asset.ContentType, asset.FileName, enableRangeProcessing: false);
        }
        catch (InvalidDataException)
        {
            return Conflict("Deployment assets failed integrity validation.");
        }
    }

    private void NoStore()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
    }
}
