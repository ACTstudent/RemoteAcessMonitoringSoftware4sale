using System.Security.Cryptography.X509Certificates;
using Server.Services;

namespace Server.Tests.Services;

public class ServerCertificateManagerTests
{
    [Fact]
    public void EnsureGeneratedCertificate_CreatesTrustedServerCertificate()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cams-certificates-{Guid.NewGuid():N}");

        try
        {
            var generated = ServerCertificateManager.EnsureGeneratedCertificate(directory);

            Assert.True(File.Exists(generated.RootCertificatePath));
            Assert.True(File.Exists(generated.ServerCertificatePath));
            Assert.True(File.Exists(generated.CertificatePath));

            string rootThumbprint;
            using (var rootCertificate = new X509Certificate2(generated.RootCertificatePath))
            using (var serverCertificate = new X509Certificate2(
                       generated.CertificatePath,
                       string.Empty,
                       X509KeyStorageFlags.EphemeralKeySet))
            {
                rootThumbprint = rootCertificate.Thumbprint ?? string.Empty;
                Assert.True(serverCertificate.HasPrivateKey);
                Assert.Equal(serverCertificate.Thumbprint, generated.Thumbprint);

                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(rootCertificate);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

                var chainStatus = string.Join(
                    "; ",
                    chain.ChainStatus.Select(status => status.StatusInformation.Trim()));
                Assert.True(chain.Build(serverCertificate), chainStatus);
            }

            var secondRun = ServerCertificateManager.EnsureGeneratedCertificate(directory);
            using var secondRootCertificate = new X509Certificate2(secondRun.RootCertificatePath);
            Assert.Equal(rootThumbprint, secondRootCertificate.Thumbprint);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
