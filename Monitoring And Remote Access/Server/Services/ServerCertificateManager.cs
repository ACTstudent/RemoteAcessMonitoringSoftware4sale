using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Server.Services;

public sealed record GeneratedServerCertificate(
    string CertificatePath,
    string CertificatePassword,
    string RootCertificatePath,
    string ServerCertificatePath,
    string Thumbprint);

public static class ServerCertificateManager
{
    private const string CertificateDirectoryName = "certificates";
    private const string RootPfxFileName = "CAMS-Server-Root.pfx";
    private const string ServerPfxFileName = "CAMS-Server.pfx";
    private const string RootPublicFileName = "CAMS-Server-Root.cer";
    private const string ServerPublicFileName = "CAMS-Server.cer";

    public static GeneratedServerCertificate EnsureGeneratedCertificate(string baseDirectory)
    {
        var certificateDirectory = Path.Combine(baseDirectory, CertificateDirectoryName);
        Directory.CreateDirectory(certificateDirectory);

        var rootPfxPath = Path.Combine(certificateDirectory, RootPfxFileName);
        var serverPfxPath = Path.Combine(certificateDirectory, ServerPfxFileName);
        var rootPublicPath = Path.Combine(baseDirectory, RootPublicFileName);
        var serverPublicPath = Path.Combine(baseDirectory, ServerPublicFileName);

        using var rootCertificate = LoadOrCreateRootCertificate(rootPfxPath);
        WriteCertificate(rootCertificate, rootPublicPath);

        using var serverCertificate = CreateServerCertificate(rootCertificate);
        WriteCertificate(serverCertificate, serverPfxPath, isPrivate: true);
        WriteCertificate(serverCertificate, serverPublicPath);

        return new GeneratedServerCertificate(
            serverPfxPath,
            string.Empty,
            rootPublicPath,
            serverPublicPath,
            serverCertificate.Thumbprint ?? string.Empty);
    }

    private static X509Certificate2 LoadOrCreateRootCertificate(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                var existing = LoadCertificate(path);
                var basicConstraints = existing.Extensions
                    .OfType<X509BasicConstraintsExtension>()
                    .FirstOrDefault();

                if (existing.HasPrivateKey &&
                    existing.NotAfter > DateTime.UtcNow.AddDays(30) &&
                    basicConstraints?.CertificateAuthority == true)
                {
                    return existing;
                }

                existing.Dispose();
            }
            catch
            {
                // Replace an incomplete or unreadable generated certificate.
            }
        }

        using var key = RSA.Create(3072);
        var request = new CertificateRequest(
            "CN=CAMS Local Root CA",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        using var created = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(10));

        var exported = created.Export(X509ContentType.Pfx, string.Empty);
        var certificate = LoadCertificate(exported);
        WriteCertificate(certificate, path, isPrivate: true);
        return certificate;
    }

    private static X509Certificate2 CreateServerCertificate(X509Certificate2 rootCertificate)
    {
        using var key = RSA.Create(2048);
        var machineName = Environment.MachineName;
        var request = new CertificateRequest(
            $"CN={machineName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));
        var enhancedKeyUsages = new OidCollection();
        enhancedKeyUsages.Add(new Oid("1.3.6.1.5.5.7.3.1", "Server Authentication"));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(enhancedKeyUsages, true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var subjectNames = new SubjectAlternativeNameBuilder();
        subjectNames.AddDnsName("localhost");
        if (!string.IsNullOrWhiteSpace(machineName))
            subjectNames.AddDnsName(machineName);

        var ipAddresses = GetLanIpAddresses();
        foreach (var address in ipAddresses)
            subjectNames.AddIpAddress(address);

        request.CertificateExtensions.Add(subjectNames.Build());

        using var unsignedCertificate = request.Create(
            rootCertificate,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(2),
            RandomNumberGenerator.GetBytes(16));
        using var certificateWithKey = unsignedCertificate.CopyWithPrivateKey(key);

        var exported = certificateWithKey.Export(X509ContentType.Pfx, string.Empty);
        return LoadCertificate(exported);
    }

    private static IReadOnlyList<IPAddress> GetLanIpAddresses()
    {
        var addresses = new List<IPAddress> { IPAddress.Loopback };

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
            {
                var ipAddress = address.Address;
                if (ipAddress.AddressFamily != AddressFamily.InterNetwork ||
                    IPAddress.IsLoopback(ipAddress) ||
                    IsLinkLocal(ipAddress) ||
                    addresses.Contains(ipAddress))
                {
                    continue;
                }

                addresses.Add(ipAddress);
            }
        }

        return addresses;
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    private static X509Certificate2 LoadCertificate(string path)
    {
        return new X509Certificate2(
            path,
            string.Empty,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
    }

    private static X509Certificate2 LoadCertificate(byte[] contents)
    {
        return new X509Certificate2(
            contents,
            string.Empty,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
    }

    private static void WriteCertificate(
        X509Certificate2 certificate,
        string path,
        bool isPrivate = false)
    {
        var contents = isPrivate
            ? certificate.Export(X509ContentType.Pfx, string.Empty)
            : certificate.Export(X509ContentType.Cert);

        if (File.Exists(path))
        {
            var attributes = File.GetAttributes(path);
            File.SetAttributes(
                path,
                attributes & ~FileAttributes.Hidden & ~FileAttributes.ReadOnly & ~FileAttributes.System);
        }

        File.WriteAllBytes(path, contents);
        if (isPrivate)
            File.SetAttributes(path, FileAttributes.Hidden);
    }
}
