using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sanctuary.Gateway.Uram;

public static class UramCertificates
{
    public const string AuthorityPemFile = "sanctuary-uram-ca.pem";
    private const string AuthorityKeyFile = "sanctuary-uram-ca.key";

    public static (X509Certificate2 Leaf, X509Certificate2 Authority) Load(string folder, string certificateName)
    {
        Directory.CreateDirectory(folder);

        var authority = LoadOrCreateAuthority(folder);

        using var leafKey = RSA.Create(2048);
        var request = new CertificateRequest(new X500DistinguishedName("CN=" + certificateName), leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));

        using var issued = request.Create(authority, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2), RandomNumberGenerator.GetBytes(16));
        var leaf = issued.CopyWithPrivateKey(leafKey);

        return (leaf, authority);
    }

    private static X509Certificate2 LoadOrCreateAuthority(string folder)
    {
        var pemPath = Path.Combine(folder, AuthorityPemFile);
        var keyPath = Path.Combine(folder, AuthorityKeyFile);

        if (File.Exists(pemPath) && File.Exists(keyPath))
            return X509Certificate2.CreateFromPemFile(pemPath, keyPath);

        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Sanctuary In-Game Purchase Authority", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20));

        File.WriteAllText(pemPath, created.ExportCertificatePem());
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());

        return X509Certificate2.CreateFromPemFile(pemPath, keyPath);
    }
}
