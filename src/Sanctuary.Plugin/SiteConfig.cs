using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Sanctuary.Plugin;

public static partial class SiteConfig
{
    private static readonly string[] ReservedArguments = ["Server", "GUID", "Ticket", "SessionId", "Internationalization:Locale"];

    [GeneratedRegex(@"^[A-Za-z0-9.\-]{1,253}:[0-9]{1,5}$")]
    private static partial Regex AddressPattern();

    [GeneratedRegex(@"^[A-Za-z]+(:[A-Za-z]+)*=[^\s""]{1,512}$")]
    private static partial Regex ArgumentPattern();

    public static async Task<PluginConfig> FetchAsync(HttpClient httpClient, Uri site, CancellationToken cancellationToken)
    {
        var config = await httpClient.GetFromJsonAsync(new Uri(site, "/website/plugin/config"), PluginJsonContext.Default.PluginConfig, cancellationToken)
            ?? throw new InvalidOperationException("The website did not send the server settings.");

        if (!AddressPattern().IsMatch(config.Gateway) || !AddressPattern().IsMatch(config.Login))
            throw new InvalidOperationException("The website sent an invalid server address.");

        foreach (var argument in config.Arguments)
        {
            var key = argument.Split('=', 2)[0];

            if (!ArgumentPattern().IsMatch(argument) || ReservedArguments.Contains(key, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The website sent an invalid game setting: {argument}");
        }

        if (!IsWebUrl(config.ClientManifestUrl) || !IsWebUrl(config.ClientBaseUrl))
            throw new InvalidOperationException("The website sent an invalid client download address.");

        config.ClientBaseUrl = config.ClientBaseUrl.TrimEnd('/');

        return config;
    }

    private static bool IsWebUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
