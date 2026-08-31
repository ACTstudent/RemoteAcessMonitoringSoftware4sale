using Server.Models;

namespace Server.Services;

public interface IDeploymentService
{
    Task<DeploymentViewModel> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<DeploymentAsset?> OpenInstallerAsync(CancellationToken cancellationToken = default);
    Task<DeploymentAsset?> OpenManifestAsync(CancellationToken cancellationToken = default);
    Task<DeploymentAsset?> OpenRootCertificateAsync(CancellationToken cancellationToken = default);
    Task<DeploymentBundle> CreateBundleAsync(string endpoint, CancellationToken cancellationToken = default);
}
