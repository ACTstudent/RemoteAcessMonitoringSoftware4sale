using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Server.Services;

namespace Server.Tests.Services;

public sealed class DeploymentServiceTests
{
    [Fact]
    public async Task ValidAssets_ReportReadyAndCanBeDownloaded()
    {
        using var fixture = new DeploymentFixture();
        fixture.Monitoring.RegisterStudent("connection", "student", "PC-01");

        var status = await fixture.Service.GetStatusAsync();
        var installer = await fixture.Service.OpenInstallerAsync();

        Assert.True(status.AssetsReady);
        Assert.Equal("2.8.0", status.ClientVersion);
        Assert.Equal(1, status.ConnectedClientCount);
        Assert.NotNull(installer);
        await installer!.Stream.DisposeAsync();
    }

    [Fact]
    public async Task MissingAssets_ReportNotReadyAndDownloadsFailValidation()
    {
        using var fixture = new DeploymentFixture();
        File.Delete(Path.Combine(fixture.AssetDirectory, DeploymentService.ManifestFileName));

        var status = await fixture.Service.GetStatusAsync();

        Assert.False(status.AssetsReady);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.OpenInstallerAsync());
    }

    [Fact]
    public async Task CorruptManifest_IsRejected()
    {
        using var fixture = new DeploymentFixture();
        File.WriteAllText(Path.Combine(fixture.AssetDirectory, DeploymentService.ManifestFileName), "{not-json");

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.OpenManifestAsync());
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("version")]
    public async Task ManifestMismatch_IsRejected(string mismatch)
    {
        using var fixture = new DeploymentFixture();
        fixture.WriteManifest(
            hash: mismatch == "hash" ? new string('A', 64) : fixture.Hash,
            size: mismatch == "size" ? fixture.Installer.Length + 1 : fixture.Installer.Length,
            serverVersion: mismatch == "version" ? "99.0.0" : null);

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.OpenInstallerAsync());
    }

    [Fact]
    public async Task Bundle_ContainsOnlyAllowlistedPublicFilesAndScripts()
    {
        using var fixture = new DeploymentFixture();
        await using var bundle = (await fixture.Service.CreateBundleAsync(
            "https://127.0.0.1:5000/remoteMonitoringHub")).Stream;
        using var archive = new ZipArchive(bundle, ZipArchiveMode.Read, leaveOpen: true);

        var names = archive.Entries.Select(entry => entry.FullName).OrderBy(name => name).ToArray();
        var expectedNames = new[]
        {
            "CAMS-Client-Setup.exe",
            "CAMS-Client-Setup.exe.sha256",
            "CAMS-Server-Root.cer",
            "Install-CAMS-Client.cmd",
            "Install-CAMS-Client.ps1",
            "README.txt",
            "deployment-manifest.json"
        };
        Assert.Equal(expectedNames.OrderBy(name => name), names);
        Assert.DoesNotContain(names, name => name.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase));

        using var reader = new StreamReader(archive.GetEntry("Install-CAMS-Client.ps1")!.Open());
        var script = await reader.ReadToEndAsync();
        Assert.Contains("Get-FileHash", script);
        Assert.Contains("CertificateAuthority", script);
        Assert.Contains("Root certificate SHA256 verification failed", script);
        Assert.Contains("Cert:\\CurrentUser\\Root", script);
        Assert.DoesNotContain("Cert:\\LocalMachine\\Root", script);
        Assert.Contains("/ServerUrl", script);
        Assert.Contains("/ServerRootCert", script);
        Assert.DoesNotContain("password", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductionCertificateMode_OmitsLocalRootFromBundle()
    {
        using var fixture = new DeploymentFixture(productionMode: true);
        await using var bundle = (await fixture.Service.CreateBundleAsync(
            "https://127.0.0.1:5000/remoteMonitoringHub")).Stream;
        using var archive = new ZipArchive(bundle, ZipArchiveMode.Read);

        Assert.Null(archive.GetEntry(DeploymentService.RootCertificateFileName));
        Assert.False((await fixture.Service.GetStatusAsync()).RootCertificateAvailable);
    }

    [Fact]
    public async Task RootDownload_IsPublicCaWithoutPrivateKey_AndRejectsPrivateCertificate()
    {
        using var fixture = new DeploymentFixture();
        var asset = await fixture.Service.OpenRootCertificateAsync();
        Assert.NotNull(asset);
        await using (asset!.Stream)
        {
            using var memory = new MemoryStream();
            await asset.Stream.CopyToAsync(memory);
            using var root = new X509Certificate2(memory.ToArray());
            Assert.False(root.HasPrivateKey);
            Assert.True(root.Extensions.OfType<X509BasicConstraintsExtension>().Single().CertificateAuthority);
        }

        File.Copy(fixture.Generated.CertificatePath,
            Path.Combine(fixture.BaseDirectory, DeploymentService.RootCertificateFileName), overwrite: true);
        Assert.Null(await fixture.Service.OpenRootCertificateAsync());
    }

    [Fact]
    public void EndpointValidation_RequiresExactHttpsHubLanAddressAndMatchingSan()
    {
        using var fixture = new DeploymentFixture();
        using var certificate = new X509Certificate2(fixture.Generated.CertificatePath, string.Empty,
            X509KeyStorageFlags.EphemeralKeySet);
        var addresses = new[] { IPAddress.Loopback };

        Assert.True(DeploymentService.TryValidateEndpoint(
            "https://127.0.0.1:5000/remoteMonitoringHub", addresses, certificate, out var normalized, out _));
        Assert.Equal("https://127.0.0.1:5000/remoteMonitoringHub", normalized);
        Assert.False(DeploymentService.TryValidateEndpoint("http://127.0.0.1:5000/remoteMonitoringHub", addresses, certificate, out _, out _));
        Assert.False(DeploymentService.TryValidateEndpoint("https://user@127.0.0.1:5000/remoteMonitoringHub", addresses, certificate, out _, out _));
        Assert.False(DeploymentService.TryValidateEndpoint("https://127.0.0.1:5443/remoteMonitoringHub", addresses, certificate, out _, out _, 5000));
        Assert.False(DeploymentService.TryValidateEndpoint("https://127.0.0.1:5000/appsettings.json", addresses, certificate, out _, out _));
        Assert.False(DeploymentService.TryValidateEndpoint("https://10.20.30.40:5000/remoteMonitoringHub", addresses, certificate, out _, out _));
        Assert.True(DeploymentService.CertificateMatchesHost(certificate, "localhost"));
        Assert.False(DeploymentService.CertificateMatchesHost(certificate, "not-this-server.invalid"));
    }

    private sealed class DeploymentFixture : IDisposable
    {
        public string BaseDirectory { get; } = Path.Combine(Path.GetTempPath(), $"cams-deployment-{Guid.NewGuid():N}");
        public string AssetDirectory => Path.Combine(BaseDirectory, "DeploymentAssets");
        public byte[] Installer { get; } = "test CAMS installer bytes"u8.ToArray();
        public string Hash { get; }
        public GeneratedServerCertificate Generated { get; }
        public MonitoringService Monitoring { get; } = new();
        public DeploymentService Service { get; }

        public DeploymentFixture(bool productionMode = false)
        {
            Directory.CreateDirectory(AssetDirectory);
            Generated = ServerCertificateManager.EnsureGeneratedCertificate(BaseDirectory);
            Hash = Convert.ToHexString(SHA256.HashData(Installer));
            File.WriteAllBytes(Path.Combine(AssetDirectory, DeploymentService.InstallerFileName), Installer);
            File.WriteAllText(Path.Combine(AssetDirectory, DeploymentService.ChecksumFileName),
                $"{Hash}  {DeploymentService.InstallerFileName}\n");
            WriteManifest(Hash, Installer.Length, null);

            var values = new Dictionary<string, string?> { ["Cams:HttpsPort"] = "5000" };
            if (productionMode) values["Cams:CertificatePath"] = Generated.CertificatePath;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            Service = new DeploymentService(BaseDirectory, configuration, Monitoring,
                () => new[] { IPAddress.Loopback });
        }

        public void WriteManifest(string hash, long size, string? serverVersion)
        {
            File.WriteAllText(Path.Combine(AssetDirectory, DeploymentService.ManifestFileName),
                JsonSerializer.Serialize(new
                {
                    product = "CAMS Student Client",
                    clientVersion = "2.8.0",
                    serverVersion,
                    installerFileName = DeploymentService.InstallerFileName,
                    installerSize = size,
                    installerSha256 = hash
                }));
        }

        public void Dispose()
        {
            if (Directory.Exists(BaseDirectory)) Directory.Delete(BaseDirectory, recursive: true);
        }
    }
}
