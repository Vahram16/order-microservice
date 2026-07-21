using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Identity.Api.Configuration;

internal static class CertificateLoader
{
    public static IReadOnlyList<X509Certificate2> Load(
        IEnumerable<CertificateOptions> configuredCertificates,
        IHostEnvironment environment,
        TimeProvider timeProvider)
    {
        var certificates = configuredCertificates
            .Select(configuration => Load(configuration, environment.ContentRootPath))
            .ToArray();

        var now = timeProvider.GetUtcNow();
        if (!certificates.Any(certificate =>
                certificate.HasPrivateKey &&
                certificate.NotBefore.ToUniversalTime() <= now.UtcDateTime &&
                certificate.NotAfter.ToUniversalTime() > now.UtcDateTime))
        {
            throw new InvalidOperationException(
                "At least one configured certificate with a private key must currently be valid.");
        }

        return certificates;
    }

    private static X509Certificate2 Load(
        CertificateOptions options,
        string contentRootPath)
    {
        var path = Path.IsPathRooted(options.Path)
            ? options.Path
            : Path.GetFullPath(options.Path, contentRootPath);

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Identity certificate '{path}' does not exist.");
        }

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            path,
            options.Password,
            X509KeyStorageFlags.EphemeralKeySet);

        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException($"Identity certificate '{path}' has no private key.");
        }

        using var rsa = certificate.GetRSAPrivateKey();
        if (rsa is null || rsa.KeySize < 3072)
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                $"Identity certificate '{path}' must contain an RSA private key of at least 3072 bits.");
        }

        return certificate;
    }
}
