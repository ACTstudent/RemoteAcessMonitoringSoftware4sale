namespace Server.Models;

public sealed class DeploymentViewModel
{
    public bool AssetsReady { get; init; }
    public string? Product { get; init; }
    public string? ClientVersion { get; init; }
    public string ServerVersion { get; init; } = string.Empty;
    public long? InstallerSize { get; init; }
    public string? InstallerSha256 { get; init; }
    public int ConnectedClientCount { get; init; }
    public string CertificateMode { get; init; } = string.Empty;
    public string? CertificateSubject { get; init; }
    public string? CertificateThumbprint { get; init; }
    public string? CertificateSha256 { get; init; }
    public DateTimeOffset? CertificateExpiresUtc { get; init; }
    public bool RootCertificateAvailable { get; init; }
    public IReadOnlyList<DeploymentEndpointViewModel> Endpoints { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DeploymentEndpointViewModel(string Url, bool CertificateCompatible, string? Warning);

public sealed record DeploymentAsset(string FileName, string ContentType, FileStream Stream);

public sealed record DeploymentBundle(string FileName, FileStream Stream);
