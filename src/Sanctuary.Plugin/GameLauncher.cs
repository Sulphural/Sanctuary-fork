using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Sanctuary.Plugin;

public static class GameLauncher
{
    public const string PurchaseCertificateFile = "sanctuary-uram-ca.pem";

    public static Process Start(PluginPaths paths, string clientDirectory, string? wine, PluginLink link, PluginConfig config)
    {
        if (config.PurchaseCertificate is not null)
        {
            try
            {
                File.WriteAllText(Path.Combine(clientDirectory, PurchaseCertificateFile), config.PurchaseCertificate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                paths.Log($"Could not save the in-game purchase certificate: {ex.Message}");
            }
        }

        var arguments = new List<string> { "Internationalization:Locale=8" };

        if (link.Kind == PluginLinkKind.Play)
        {
            arguments.Add($"Server={config.Gateway}");
            arguments.Add($"GUID={link.Guid}");
            arguments.Add($"Ticket={link.Ticket}");
        }
        else
        {
            arguments.Add($"Server={config.Login}");
            arguments.Add($"SessionId={link.Session}");
        }

        arguments.AddRange(config.Arguments);

        var executable = Path.Combine(clientDirectory, ClientDownloader.ClientExecutable);
        ProcessStartInfo startInfo;

        if (wine is null)
        {
            startInfo = new ProcessStartInfo(executable);
        }
        else
        {
            startInfo = new ProcessStartInfo(wine);
            startInfo.ArgumentList.Add(executable);
            startInfo.Environment["WINEPREFIX"] = paths.PrefixDirectory;
            startInfo.Environment["WINEDEBUG"] = "-all";
            startInfo.Environment["WINEDLLOVERRIDES"] = "winemenubuilder.exe=d;mscoree=d;mshtml=d";
            Directory.CreateDirectory(paths.PrefixDirectory);
        }

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        startInfo.WorkingDirectory = clientDirectory;
        startInfo.UseShellExecute = false;

        paths.Log($"Starting {(wine is null ? "client" : "client with " + wine)} for {link.Kind} on {link.Origin}");

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Open Source Free Realms.");
    }
}
