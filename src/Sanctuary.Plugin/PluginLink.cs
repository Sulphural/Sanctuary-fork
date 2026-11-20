using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Web;

namespace Sanctuary.Plugin;

public enum PluginLinkKind
{
    Play,
    Login
}

public sealed partial record PluginLink(PluginLinkKind Kind, Uri Site, string? Guid, string? Ticket, string? Session)
{
    public const string Scheme = "freerealms";

    [GeneratedRegex("^[0-9]{1,20}$")]
    private static partial Regex GuidPattern();

    [GeneratedRegex("^[0-9a-fA-F]{32}$")]
    private static partial Regex TokenPattern();

    public static bool TryParse(string value, [NotNullWhen(true)] out PluginLink? link)
    {
        link = null;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var query = HttpUtility.ParseQueryString(uri.Query);

        if (!TryParseSite(query["site"], out var site))
            return false;

        switch (uri.Host.ToLowerInvariant())
        {
            case "play":
                var guid = query["guid"];
                var ticket = query["ticket"];

                if (guid is null || ticket is null || !GuidPattern().IsMatch(guid) || !TokenPattern().IsMatch(ticket))
                    return false;

                link = new PluginLink(PluginLinkKind.Play, site, guid, ticket, null);
                return true;

            case "login":
                var session = query["session"];

                if (session is null || !TokenPattern().IsMatch(session))
                    return false;

                link = new PluginLink(PluginLinkKind.Login, site, null, null, session);
                return true;

            default:
                return false;
        }
    }

    public static bool TryParseSite(string? value, [NotNullWhen(true)] out Uri? site)
    {
        site = null;

        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (!string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath.Trim('/').Length > 0 || uri.Query.Length > 0)
            return false;

        site = new Uri(uri.GetLeftPart(UriPartial.Authority));
        return true;
    }

    public string Origin => Site.GetLeftPart(UriPartial.Authority);
}
