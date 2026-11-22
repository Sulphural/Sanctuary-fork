namespace Sanctuary.WebAPI.Options;

public class PluginOptions
{
    public const string Section = "Plugin";

    public string? PublicSiteUrl { get; set; }

    public string GatewayAddress { get; set; } = "127.0.0.1:20260";

    public string LoginAddress { get; set; } = "127.0.0.1:20042";

    public string? LocalGatewayAddress { get; set; }

    public string? LocalLoginAddress { get; set; }

    public string ClientManifestUrl { get; set; } = "https://opensourcefreerealms.com/clientmanifest.xml";

    public string ClientBaseUrl { get; set; } = "https://opensourcefreerealms.com/client";

    public string? DownloadsPath { get; set; }

    public string? PurchaseAddress { get; set; }

    public string? LocalPurchaseAddress { get; set; }

    public string? PurchaseCertificateFile { get; set; }
}
