namespace Sanctuary.Gateway.Uram;

public sealed class UramOptions
{
    public const string Section = "Uram";

    public bool Enabled { get; set; }

    public int Port { get; set; } = 20044;

    public string CertificateName { get; set; } = "*";

    public string CertificateFolder { get; set; } = "uram";
}
