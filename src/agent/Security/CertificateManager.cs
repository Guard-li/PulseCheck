using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PulseCheck.Agent.Security;

public sealed record AgentCertificate(X509Certificate2 Certificate, string Sha256Fingerprint)
{
    public string Sha256Spaced => string.Join(":", Enumerable.Range(0, Sha256Fingerprint.Length / 2).Select(i => Sha256Fingerprint.Substring(i * 2, 2))).ToUpperInvariant();
}

public static class CertificateManager
{
    private const string Password = "PulseCheck-local-pfx";

    public static AgentCertificate GetOrCreate(string dataDirectory, string deviceId, string displayName)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "agent-tls.pfx");
        if (File.Exists(path))
        {
            try
            {
                var existing = new X509Certificate2(path, Password, X509KeyStorageFlags.Exportable);
                if (existing.NotAfter > DateTime.UtcNow.AddDays(30)) return CreateResult(existing);
            }
            catch { }
        }

        var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN=PulseCheck-{displayName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddDnsName(Environment.MachineName);
        san.AddIpAddress(IPAddress.Loopback);
        foreach (var address in GetLocalAddresses(includeLoopback: false))
            san.AddIpAddress(IPAddress.Parse(address));
        request.CertificateExtensions.Add(san.Build());
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        var bytes = certificate.Export(X509ContentType.Pkcs12, Password);
        File.WriteAllBytes(path, bytes);
        try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); } catch { }
        certificate.Dispose();
        rsa.Dispose();
        var persisted = new X509Certificate2(bytes, Password, X509KeyStorageFlags.Exportable);
        return CreateResult(persisted);
    }

    public static string[] GetLocalAddresses(bool includeLoopback = true)
    {
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(x => x.GetIPProperties().UnicastAddresses)
                .Select(x => x.Address)
                .Where(x => x.AddressFamily == AddressFamily.InterNetwork || (includeLoopback && IPAddress.IsLoopback(x)))
                .Where(x => includeLoopback || !IPAddress.IsLoopback(x))
                .Select(x => x.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return includeLoopback ? [IPAddress.Loopback.ToString(), .. addresses] : addresses;
        }
        catch
        {
            return includeLoopback ? [IPAddress.Loopback.ToString()] : [];
        }
    }

    private static AgentCertificate CreateResult(X509Certificate2 certificate)
    {
        var hash = Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();
        return new AgentCertificate(certificate, hash);
    }
}