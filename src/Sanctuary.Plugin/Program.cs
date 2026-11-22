using System;
using System.IO;
using System.Threading.Tasks;

namespace Sanctuary.Plugin;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var paths = PluginPaths.Create();

        if (args.Length > 0 && args[0].StartsWith(PluginLink.Scheme + ":", StringComparison.OrdinalIgnoreCase))
        {
            if (!PluginLink.TryParse(args[0], out var link))
            {
                paths.Log($"Rejected link: {args[0]}");
                Dialogs.Error("This Open Source Free Realms link is not valid.");
                return 1;
            }

            return await PlaySession.RunAsync(paths, link);
        }

        if (args.Length > 0 && args[0] == "--uninstall")
        {
            ProtocolRegistration.Uninstall();
            Dialogs.Info("The Open Source Free Realms Plugin has been removed.");
            return 0;
        }

        if (args.Length > 1 && args[0] == "--client")
        {
            var folder = Path.GetFullPath(args[1]);

            if (!ClientDownloader.HasClient(folder))
            {
                Dialogs.Error($"There is no {ClientDownloader.ClientExecutable} in {folder}.");
                return 1;
            }

            var settings = PluginSettings.Load(paths);
            settings.ClientPath = folder;
            settings.Save(paths);
        }

        try
        {
            ProtocolRegistration.Install(paths);
        }
        catch (Exception ex)
        {
            paths.Log($"Install failed: {ex}");
            Dialogs.Error($"The Open Source Free Realms Plugin could not be installed.\n\n{ex.Message}");
            return 1;
        }

        if (args.Length == 0 || args[0] != "--quiet")
            Dialogs.Info("The Open Source Free Realms Plugin is installed!\n\nGo back to your browser and press Play Now! to start Open Source Free Realms.");

        return 0;
    }
}
