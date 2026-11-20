using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;

using Microsoft.Win32;

namespace Sanctuary.Plugin;

public static class ProtocolRegistration
{
    private const string DesktopFileName = "freerealms-plugin.desktop";
    private const string WindowsClassesKey = @"Software\Classes\" + PluginLink.Scheme;

    public static string Install(PluginPaths paths)
    {
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find the plugin executable.");

        if (OperatingSystem.IsMacOS())
            return current;

        var installed = Path.Combine(paths.DataDirectory, Path.GetFileName(current));

        if (!string.Equals(Path.GetFullPath(current), Path.GetFullPath(installed), StringComparison.OrdinalIgnoreCase))
            File.Copy(current, installed, true);

        if (OperatingSystem.IsWindows())
            RegisterWindows(installed);
        else if (OperatingSystem.IsLinux())
            RegisterLinux(installed);

        return installed;
    }

    public static void Uninstall()
    {
        if (OperatingSystem.IsWindows())
        {
            Registry.CurrentUser.DeleteSubKeyTree(WindowsClassesKey, false);
        }
        else if (OperatingSystem.IsLinux())
        {
            var desktopFile = Path.Combine(ApplicationsDirectory(), DesktopFileName);

            if (File.Exists(desktopFile))
                File.Delete(desktopFile);

            Run("update-desktop-database", ApplicationsDirectory());
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RegisterWindows(string executable)
    {
        using var root = Registry.CurrentUser.CreateSubKey(WindowsClassesKey);
        root.SetValue("", $"URL:{PluginInfo.Name}");
        root.SetValue("URL Protocol", "");

        using (var icon = root.CreateSubKey("DefaultIcon"))
            icon.SetValue("", $"\"{executable}\",0");

        using var command = root.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{executable}\" \"%1\"");
    }

    [UnsupportedOSPlatform("windows")]
    private static void RegisterLinux(string executable)
    {
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        var applications = ApplicationsDirectory();
        Directory.CreateDirectory(applications);

        File.WriteAllText(Path.Combine(applications, DesktopFileName),
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            $"Name={PluginInfo.Name}\n" +
            $"Exec=\"{executable}\" %u\n" +
            "Terminal=false\n" +
            "NoDisplay=true\n" +
            $"MimeType=x-scheme-handler/{PluginLink.Scheme};\n");

        Run("xdg-mime", "default", DesktopFileName, $"x-scheme-handler/{PluginLink.Scheme}");
        Run("update-desktop-database", applications);
    }

    private static string ApplicationsDirectory()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        return Path.Combine(dataHome, "applications");
    }

    private static void Run(string fileName, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            process?.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
