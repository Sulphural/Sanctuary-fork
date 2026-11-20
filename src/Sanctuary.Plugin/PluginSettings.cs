using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sanctuary.Plugin;

public sealed class PluginSettings
{
    public List<string> TrustedSites { get; set; } = [];

    public string? ClientPath { get; set; }

    public static PluginSettings Load(PluginPaths paths)
    {
        try
        {
            if (File.Exists(paths.SettingsFile))
                return JsonSerializer.Deserialize(File.ReadAllText(paths.SettingsFile), PluginJsonContext.Default.PluginSettings) ?? new PluginSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            paths.Log($"Could not read settings: {ex.Message}");
        }

        return new PluginSettings();
    }

    public void Save(PluginPaths paths)
    {
        File.WriteAllText(paths.SettingsFile, JsonSerializer.Serialize(this, PluginJsonContext.Default.PluginSettings));
    }

    public bool IsTrusted(string origin) => TrustedSites.Contains(origin, StringComparer.OrdinalIgnoreCase);
}

public sealed class PluginConfig
{
    public string Gateway { get; set; } = "";
    public string Login { get; set; } = "";
    public List<string> Arguments { get; set; } = [];
    public string ClientManifestUrl { get; set; } = "";
    public string ClientBaseUrl { get; set; } = "";
}

public sealed class PluginStatus
{
    public string State { get; set; } = "starting";
    public double Progress { get; set; }
    public string Message { get; set; } = "";
    public string Version { get; set; } = PluginInfo.Version;
}

public static class PluginInfo
{
    public const string Version = "1.0.0";
    public const string Name = "Free Realms Plugin";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(PluginSettings))]
[JsonSerializable(typeof(PluginConfig))]
[JsonSerializable(typeof(PluginStatus))]
internal sealed partial class PluginJsonContext : JsonSerializerContext
{
}
