using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Sanctuary.Plugin;

public static class PlaySession
{
    private static readonly TimeSpan ExitLinger = TimeSpan.FromSeconds(10);

    public static async Task<int> RunAsync(PluginPaths paths, PluginLink link)
    {
        using var status = new StatusServer(link.Origin);
        var reportsStatus = status.TryStart();
        using var cancellation = new CancellationTokenSource();

        if (!reportsStatus)
            paths.Log("Status port is busy, continuing without browser status.");

        try
        {
            var settings = PluginSettings.Load(paths);

            if (!settings.IsTrusted(link.Origin))
            {
                status.Set("confirm", "Waiting for you to allow this website...");

                if (!Dialogs.Confirm($"Allow {link.Site.Authority} to start Open Source Free Realms on this computer?\n\nOnly allow websites you trust."))
                {
                    status.Set("denied", "Open Source Free Realms was not allowed to start.");
                    await Task.Delay(ExitLinger);
                    return 1;
                }

                settings.TrustedSites.Add(link.Origin);
                settings.Save(paths);
            }

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"FreeRealmsPlugin/{PluginInfo.Version}");

            status.Set("connecting", "Connecting to the server...");
            var config = await SiteConfig.FetchAsync(httpClient, link.Site, cancellation.Token);

            var clientDirectory = await ClientDownloader.EnsureAsync(httpClient, paths, settings, config, status, cancellation.Token);
            var wine = OperatingSystem.IsWindows() ? null : await WineRuntime.EnsureAsync(httpClient, paths, status, cancellation.Token);

            status.Set("launching", "Launching Open Source Free Realms!", 1);
            using var game = GameLauncher.Start(paths, clientDirectory, wine, link, config);

            status.Set("running", "Game Running", 1);
            await game.WaitForExitAsync();

            status.Set("exited", "Open Source Free Realms has closed.", 1);
            paths.Log($"Game exited with code {game.ExitCode}");
            await Task.Delay(ExitLinger);
            return 0;
        }
        catch (Exception ex)
        {
            paths.Log($"Launch failed: {ex}");
            status.Set("error", ex.Message);
            Dialogs.Error($"Open Source Free Realms could not start.\n\n{ex.Message}");
            await Task.Delay(ExitLinger);
            return 1;
        }
    }
}
