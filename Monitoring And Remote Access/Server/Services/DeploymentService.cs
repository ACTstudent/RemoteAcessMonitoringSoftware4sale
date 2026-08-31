using System.Diagnostics;
using System.Formats.Asn1;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Server.Models;

namespace Server.Services;

public sealed class DeploymentService : IDeploymentService
{
    public const string InstallerFileName = "CAMS-Client-Setup.exe";
    public const string ManifestFileName = "deployment-manifest.json";
    public const string ChecksumFileName = "CAMS-Client-Setup.exe.sha256";
    public const string RootCertificateFileName = "CAMS-Server-Root.cer";
    private const long MaximumInstallerBytes = 512L * 1024 * 1024;
    private const long MaximumManifestBytes = 1024 * 1024;
    private const long MaximumChecksumBytes = 4096;

    private readonly string _baseDirectory;
    private readonly string _assetDirectory;
    private readonly IConfiguration _configuration;
    private readonly IMonitoringService _monitoring;
    private readonly Func<IReadOnlyList<IPAddress>> _lanAddressProvider;

    public DeploymentService(IConfiguration configuration, IMonitoringService monitoring)
        : this(AppContext.BaseDirectory, configuration, monitoring)
    {
    }

    public DeploymentService(string baseDirectory, IConfiguration configuration, IMonitoringService monitoring,
        Func<IReadOnlyList<IPAddress>>? lanAddressProvider = null)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory);
        _assetDirectory = Path.Combine(_baseDirectory, "DeploymentAssets");
        _configuration = configuration;
        _monitoring = monitoring;
        _lanAddressProvider = lanAddressProvider ?? GetLanAddresses;
    }

    public async Task<DeploymentViewModel> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        ValidatedAssets? assets = null;
        try
        {
            assets = await ValidateAssetsAsync(cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            warnings.Add(ex.Message);
        }

        using var certificate = TryLoadServerCertificate(warnings);
        var localMode = string.IsNullOrWhiteSpace(_configuration["Cams:CertificatePath"]);
        var endpoints = _lanAddressProvider().Select(address =>
        {
            var url = $"https://{address}:{_configuration.GetValue("Cams:HttpsPort", 5000)}/remoteMonitoringHub";
            var compatible = certificate is not null && CertificateIsTimeValid(certificate) && CertificateMatchesHost(certificate, address.ToString());
            return new DeploymentEndpointViewModel(url, compatible,
                compatible ? null : "The active HTTPS certificate does not contain this IP address in its subject alternative names.");
        }).ToArray();

        if (endpoints.Length == 0)
            warnings.Add("No viable active LAN IPv4 endpoint was detected.");
        else if (endpoints.All(endpoint => !endpoint.CertificateCompatible))
            warnings.Add("No detected LAN endpoint is covered by the active HTTPS certificate.");
        if (certificate is not null && !CertificateIsTimeValid(certificate))
            warnings.Add("The active HTTPS certificate is expired or not yet valid.");

        var rootAvailable = localMode && TryValidatePublicRoot(out _);
        if (localMode && !rootAvailable)
            warnings.Add("The local public root certificate is missing or is not a safe CA certificate.");

        var serverVersion = GetServerVersion();
        var model = new DeploymentViewModel
        {
            AssetsReady = assets is not null,
            Product = assets?.Manifest.Product,
            ClientVersion = assets?.Manifest.ClientVersion,
            ServerVersion = serverVersion,
            InstallerSize = assets?.InstallerSize,
            InstallerSha256 = assets?.InstallerSha256,
            ConnectedClientCount = _monitoring.ActiveStudents.Count,
            CertificateMode = localMode ? "Local CAMS CA" : "Production / public certificate",
            CertificateSubject = certificate?.Subject,
            CertificateThumbprint = certificate?.Thumbprint,
            CertificateSha256 = certificate is null ? null : Convert.ToHexString(SHA256.HashData(certificate.RawData)),
            CertificateExpiresUtc = certificate is null ? null : new DateTimeOffset(certificate.NotAfter.ToUniversalTime()),
            RootCertificateAvailable = rootAvailable,
            Endpoints = endpoints,
            Warnings = warnings
        };
        assets?.Dispose();
        return model;
    }

    public async Task<DeploymentAsset?> OpenInstallerAsync(CancellationToken cancellationToken = default)
    {
        using var validation = await ValidateAssetsAsync(cancellationToken);
        return OpenAsset(InstallerFileName, "application/octet-stream");
    }

    public async Task<DeploymentAsset?> OpenManifestAsync(CancellationToken cancellationToken = default)
    {
        using var validation = await ValidateAssetsAsync(cancellationToken);
        return OpenAsset(ManifestFileName, "application/json");
    }

    public Task<DeploymentAsset?> OpenRootCertificateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(_configuration["Cams:CertificatePath"]) || !TryValidatePublicRoot(out var path))
            return Task.FromResult<DeploymentAsset?>(null);

        return Task.FromResult<DeploymentAsset?>(new DeploymentAsset(
            RootCertificateFileName,
            "application/pkix-cert",
            new FileStream(path!, FileMode.Open, FileAccess.Read, FileShare.Read)));
    }

    public async Task<DeploymentBundle> CreateBundleAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        using var assets = await ValidateAssetsAsync(cancellationToken);
        if (!TryValidateEndpoint(endpoint, _lanAddressProvider(), assets.Certificate, out var normalizedEndpoint, out var error,
                _configuration.GetValue("Cams:HttpsPort", 5000)))
            throw new ArgumentException(error, nameof(endpoint));

        var temporaryPath = Path.Combine(Path.GetTempPath(), $"CAMS-Deployment-{Guid.NewGuid():N}.zip");
        var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                await AddFileAsync(archive, InstallerFileName, AssetPath(InstallerFileName), cancellationToken);
                await AddFileAsync(archive, ChecksumFileName, AssetPath(ChecksumFileName), cancellationToken);
                await AddFileAsync(archive, ManifestFileName, AssetPath(ManifestFileName), cancellationToken);

                var root = await OpenRootCertificateAsync(cancellationToken);
                string? rootSha256 = null;
                if (root is not null)
                {
                    await using (root.Stream)
                    {
                        rootSha256 = Convert.ToHexString(await SHA256.HashDataAsync(root.Stream, cancellationToken));
                        root.Stream.Position = 0;
                        await AddStreamAsync(archive, RootCertificateFileName, root.Stream, cancellationToken);
                    }
                }

                await AddTextAsync(archive, "README.txt", BuildReadme(normalizedEndpoint!, root is not null), cancellationToken);
                await AddTextAsync(archive, "Install-CAMS-Client.ps1", BuildPowerShell(normalizedEndpoint!, assets.InstallerSha256, rootSha256), cancellationToken);
                await AddTextAsync(archive, "Install-CAMS-Client.cmd", "@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%~dp0Install-CAMS-Client.ps1\"\r\nexit /b %ERRORLEVEL%\r\n", cancellationToken);
            }
            stream.Position = 0;
            return new DeploymentBundle($"CAMS-Client-{assets.Manifest.ClientVersion}-Deployment.zip", stream);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    public static bool TryValidateEndpoint(string? endpoint, IEnumerable<IPAddress> viableAddresses,
        X509Certificate2? certificate, out string? normalizedEndpoint, out string? error, int? requiredPort = null)
    {
        normalizedEndpoint = null;
        error = null;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.AbsolutePath.TrimEnd('/'), "/remoteMonitoringHub", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.UserInfo) || (requiredPort.HasValue && uri.Port != requiredPort.Value) ||
            !IPAddress.TryParse(uri.Host, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            error = "Select an HTTPS LAN IPv4 /remoteMonitoringHub endpoint shown on this page.";
            return false;
        }

        if (!viableAddresses.Contains(address))
        {
            error = "The selected endpoint is not an active LAN address on this server.";
            return false;
        }

        if (certificate is null || !CertificateIsTimeValid(certificate) || !CertificateMatchesHost(certificate, uri.Host))
        {
            error = "The active HTTPS certificate is not valid for the selected endpoint.";
            return false;
        }

        normalizedEndpoint = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return true;
    }

    public static bool CertificateMatchesHost(X509Certificate2 certificate, string host)
    {
        var san = certificate.Extensions["2.5.29.17"];
        if (san is null) return false;
        try
        {
            var reader = new AsnReader(san.RawData, AsnEncodingRules.DER);
            var sequence = reader.ReadSequence();
            while (sequence.HasData)
            {
                var tag = sequence.PeekTag();
                if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 7)))
                {
                    var bytes = sequence.ReadOctetString(tag);
                    if (IPAddress.TryParse(host, out var expected) && expected.GetAddressBytes().SequenceEqual(bytes)) return true;
                }
                else if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 2)))
                {
                    var dns = sequence.ReadCharacterString(UniversalTagNumber.IA5String, tag);
                    if (string.Equals(dns, host, StringComparison.OrdinalIgnoreCase)) return true;
                }
                else sequence.ReadEncodedValue();
            }
        }
        catch (AsnContentException) { }
        return false;
    }

    private static bool CertificateIsTimeValid(X509Certificate2 certificate) =>
        certificate.NotBefore.ToUniversalTime() <= DateTime.UtcNow && certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow;

    private async Task<ValidatedAssets> ValidateAssetsAsync(CancellationToken cancellationToken)
    {
        var manifestPath = AssetPath(ManifestFileName);
        var installerPath = AssetPath(InstallerFileName);
        var checksumPath = AssetPath(ChecksumFileName);
        if (!File.Exists(manifestPath) || !File.Exists(installerPath) || !File.Exists(checksumPath))
            throw new InvalidDataException("Deployment assets are incomplete. The manifest, installer, and checksum are all required.");
        if (new FileInfo(manifestPath).Length is <= 0 or > MaximumManifestBytes ||
            new FileInfo(checksumPath).Length is <= 0 or > MaximumChecksumBytes)
            throw new InvalidDataException("Deployment manifest or checksum exceeds the accepted size.");

        DeploymentManifest? manifest;
        try
        {
            await using var manifestStream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            manifest = await JsonSerializer.DeserializeAsync<DeploymentManifest>(manifestStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Deployment manifest is not valid JSON.", ex);
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Product) ||
            string.IsNullOrWhiteSpace(manifest.ClientVersion) ||
            !string.Equals(manifest.InstallerFileName, InstallerFileName, StringComparison.Ordinal) ||
            manifest.InstallerSize <= 0 || manifest.InstallerSize > MaximumInstallerBytes ||
            !IsSha256(manifest.InstallerSha256))
            throw new InvalidDataException("Deployment manifest is missing valid product, clientVersion, installerFileName, installerSize, or installerSha256 metadata.");

        var serverVersion = GetServerVersion();
        if (!string.IsNullOrWhiteSpace(manifest.ServerVersion) && !VersionsEqual(manifest.ServerVersion, serverVersion))
            throw new InvalidDataException($"Manifest server version {manifest.ServerVersion} does not match running server version {serverVersion}.");

        var fileInfo = new FileInfo(installerPath);
        if (fileInfo.Length != manifest.InstallerSize)
            throw new InvalidDataException("Installer size does not match the deployment manifest.");

        await using var installer = new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(installer, cancellationToken));
        if (!actualHash.Equals(NormalizeHash(manifest.InstallerSha256), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installer SHA256 does not match the deployment manifest.");

        var checksumText = (await File.ReadAllTextAsync(checksumPath, cancellationToken)).Trim();
        var checksumParts = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (checksumParts.Length != 2 || !IsSha256(checksumParts[0]) ||
            !checksumParts[1].TrimStart('*').Equals(InstallerFileName, StringComparison.Ordinal) ||
            !NormalizeHash(checksumParts[0]).Equals(actualHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installer checksum file is invalid or does not match the installer.");

        var fileVersion = FileVersionInfo.GetVersionInfo(installerPath).ProductVersion;
        if (!string.IsNullOrWhiteSpace(fileVersion) && !VersionsEqual(fileVersion, manifest.ClientVersion))
            throw new InvalidDataException("Installer product version does not match the deployment manifest.");

        var warnings = new List<string>();
        var certificate = TryLoadServerCertificate(warnings);
        if (certificate is null)
            throw new InvalidDataException(warnings.FirstOrDefault() ?? "The active HTTPS certificate could not be loaded.");
        return new ValidatedAssets(manifest, fileInfo.Length, actualHash, certificate);
    }

    private X509Certificate2? TryLoadServerCertificate(ICollection<string> warnings)
    {
        var configuredPath = _configuration["Cams:CertificatePath"];
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(_baseDirectory, "certificates", "CAMS-Server.pfx")
            : Path.GetFullPath(configuredPath, _baseDirectory);
        try
        {
            return new X509Certificate2(path, _configuration["Cams:CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        {
            warnings.Add("The active HTTPS certificate could not be inspected.");
            return null;
        }
    }

    private bool TryValidatePublicRoot(out string? path)
    {
        path = Path.Combine(_baseDirectory, RootCertificateFileName);
        try
        {
            using var certificate = new X509Certificate2(path);
            var constraints = certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
            return !certificate.HasPrivateKey && constraints?.CertificateAuthority == true;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        {
            path = null;
            return false;
        }
    }

    private static IReadOnlyList<IPAddress> GetLanAddresses()
    {
        var addresses = new List<IPAddress>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            foreach (var item in nic.GetIPProperties().UnicastAddresses)
            {
                var address = item.Address;
                var bytes = address.GetAddressBytes();
                if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address) &&
                    !(bytes[0] == 169 && bytes[1] == 254) && !addresses.Contains(address)) addresses.Add(address);
            }
        }
        return addresses;
    }

    private DeploymentAsset? OpenAsset(string fileName, string contentType)
    {
        var path = AssetPath(fileName);
        return File.Exists(path) ? new DeploymentAsset(fileName, contentType,
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) : null;
    }

    private string AssetPath(string exactFileName) => Path.Combine(_assetDirectory, exactFileName);
    private static bool IsSha256(string? value) => value is not null && NormalizeHash(value).Length == 64 && NormalizeHash(value).All(Uri.IsHexDigit);
    private static string NormalizeHash(string value) => value.Replace("-", string.Empty).Trim();
    private static string GetServerVersion() => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
    private static bool VersionsEqual(string left, string right)
    {
        if (!Version.TryParse(left.Split('+')[0], out var leftVersion) ||
            !Version.TryParse(right.Split('+')[0], out var rightVersion))
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        return leftVersion.Major == rightVersion.Major && leftVersion.Minor == rightVersion.Minor &&
               Math.Max(0, leftVersion.Build) == Math.Max(0, rightVersion.Build) &&
               Math.Max(0, leftVersion.Revision) == Math.Max(0, rightVersion.Revision);
    }

    private static async Task AddFileAsync(ZipArchive archive, string name, string path, CancellationToken token)
    {
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await AddStreamAsync(archive, name, source, token);
    }

    private static async Task AddStreamAsync(ZipArchive archive, string name, Stream source, CancellationToken token)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var destination = entry.Open();
        await source.CopyToAsync(destination, 64 * 1024, token);
    }

    private static Task AddTextAsync(ZipArchive archive, string name, string text, CancellationToken token) =>
        AddStreamAsync(archive, name, new MemoryStream(Encoding.UTF8.GetBytes(text)), token);

    private static string BuildReadme(string endpoint, bool hasRoot) =>
        $"CAMS Client Deployment\r\n======================\r\n\r\nServer endpoint: {endpoint}\r\n\r\nSign in as the Windows user who will run CAMS, then run Install-CAMS-Client.cmd. The script verifies the installer SHA256{(hasRoot ? " and public root certificate SHA256" : string.Empty)}, installs trust for that user, installs the client, and tests the server TLS endpoint. No credentials or private keys are stored in this bundle.\r\n";

    private static string BuildPowerShell(string endpoint, string hash, string? rootSha256)
    {
        var rootBlock = rootSha256 is not null ? $@"
$rootPath = Join-Path $PSScriptRoot 'CAMS-Server-Root.cer'
$expectedRootHash = '{rootSha256}'
$actualRootHash = (Get-FileHash -LiteralPath $rootPath -Algorithm SHA256).Hash
if ($actualRootHash -ne $expectedRootHash) {{ throw 'Root certificate SHA256 verification failed.' }}
$root = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($rootPath)
if ($root.HasPrivateKey) {{ throw 'The root certificate unexpectedly contains a private key.' }}
$basic = $root.Extensions | Where-Object {{ $_ -is [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension] }}
if (-not $basic -or -not $basic.CertificateAuthority) {{ throw 'The root certificate is not a CA certificate.' }}
$keyUsage = $root.Extensions | Where-Object {{ $_ -is [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension] }}
if (-not $keyUsage -or -not ($keyUsage.KeyUsages -band [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign)) {{ throw 'The root certificate cannot sign certificates.' }}
if ($root.NotBefore.ToUniversalTime() -gt [DateTime]::UtcNow -or $root.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow) {{ throw 'The root certificate is outside its validity period.' }}
Import-Certificate -FilePath $rootPath -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
$rootArgument = @('/ServerRootCert=' + $rootPath)
" : "$rootArgument = @()\n";
        return $@"$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot '{InstallerFileName}'
$expectedHash = '{hash}'
$actualHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
if ($actualHash -ne $expectedHash) {{ throw 'Installer SHA256 verification failed.' }}
{rootBlock}& $installer '/ServerUrl={endpoint}' @rootArgument
if ($LASTEXITCODE -ne 0) {{ throw 'CAMS client setup failed with exit code ' + $LASTEXITCODE + '.' }}
$ping = '{endpoint}'.Replace('/remoteMonitoringHub', '/api/deployment/ping')
$response = Invoke-RestMethod -Uri $ping -Method Get -TimeoutSec 15
if ($response.status -ne 'ok') {{ throw 'CAMS TLS ping did not return ready status.' }}
Write-Host 'CAMS client installed and server TLS ping succeeded.' -ForegroundColor Green
";
    }

    private sealed class DeploymentManifest
    {
        public string Product { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;
        public string? ServerVersion { get; set; }
        public string InstallerFileName { get; set; } = string.Empty;
        public long InstallerSize { get; set; }
        public string InstallerSha256 { get; set; } = string.Empty;
    }

    private sealed record ValidatedAssets(DeploymentManifest Manifest, long InstallerSize, string InstallerSha256, X509Certificate2 Certificate) : IDisposable
    {
        public void Dispose() => Certificate.Dispose();
    }
}
