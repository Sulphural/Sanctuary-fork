using System;
using System.IO;

namespace Sanctuary.Plugin;

public sealed class PluginPaths
{
    public required string DataDirectory { get; init; }

    public string ClientDirectory => Path.Combine(DataDirectory, "client");
    public string RuntimeDirectory => Path.Combine(DataDirectory, "runtime");
    public string PrefixDirectory => Path.Combine(DataDirectory, "prefix");
    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public string LogFile => Path.Combine(DataDirectory, "plugin.log");

    public static PluginPaths Create()
    {
        string root;

        if (OperatingSystem.IsWindows())
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        else if (OperatingSystem.IsMacOS())
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
        else
            root = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        var paths = new PluginPaths { DataDirectory = Path.Combine(root, "FreeRealmsPlugin") };

        Directory.CreateDirectory(paths.DataDirectory);

        return paths;
    }

    public void Log(string message)
    {
        try
        {
            File.AppendAllText(LogFile, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }
}
