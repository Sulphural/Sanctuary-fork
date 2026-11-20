using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Sanctuary.Plugin;

public sealed record WineBuild(string Name, string Url, string Sha256);

public static class WineRuntime
{
    public static readonly WineBuild Linux = new(
        "wine-11.20-staging-amd64-wow64",
        "https://github.com/Kron4ek/Wine-Builds/releases/download/11.20/wine-11.20-staging-amd64-wow64.tar.xz",
        "fad302e59925777bac89e3e4fc3e55ad396663d031c62718d0b029dd1e0fa823");

    public static readonly WineBuild MacOS = new(
        "wine-staging-11.20-osx64",
        "https://github.com/Gcenx/macOS_Wine_builds/releases/download/11.20/wine-staging-11.20-osx64.tar.xz",
        "4df858c5e72920deaf123cbc29fa2b9e64c23bcf2dea59e0a7047b879b9cd952");

    private const int BufferSize = 1024 * 1024;

    public static WineBuild Current()
    {
        if (OperatingSystem.IsMacOS())
            return MacOS;

        if (OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64)
            return Linux;

        throw new PlatformNotSupportedException("Free Realms needs a 64-bit Intel/AMD Linux PC or a Mac.");
    }

    public static async Task<string> EnsureAsync(HttpClient httpClient, PluginPaths paths, StatusServer status, CancellationToken cancellationToken)
    {
        var build = Current();
        var installDirectory = Path.Combine(paths.RuntimeDirectory, build.Name);
        var marker = Path.Combine(installDirectory, ".complete");

        if (File.Exists(marker) && FindWine(installDirectory) is { } existing)
            return existing;

        Directory.CreateDirectory(paths.RuntimeDirectory);

        var archive = Path.Combine(paths.RuntimeDirectory, build.Name + ".tar.xz");

        if (!File.Exists(archive) || !await HasHashAsync(archive, build.Sha256, cancellationToken))
            await DownloadAsync(httpClient, build, archive, status, cancellationToken);

        status.Set("installing", "Installing the Free Realms Plugin game runtime...", 1);

        if (Directory.Exists(installDirectory))
            Directory.Delete(installDirectory, true);

        Directory.CreateDirectory(installDirectory);

        var tar = new ProcessStartInfo("tar") { UseShellExecute = false, RedirectStandardError = true };
        tar.ArgumentList.Add("-xJf");
        tar.ArgumentList.Add(archive);
        tar.ArgumentList.Add("-C");
        tar.ArgumentList.Add(installDirectory);

        using (var process = Process.Start(tar) ?? throw new InvalidOperationException("Could not run tar."))
        {
            var errors = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Could not unpack the game runtime: {errors.Trim()}");
        }

        var wine = FindWine(installDirectory) ?? throw new InvalidOperationException("The game runtime is missing the wine program.");

        File.WriteAllText(marker, build.Sha256);
        File.Delete(archive);

        return wine;
    }

    private static string? FindWine(string root)
    {
        if (!Directory.Exists(root))
            return null;

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) is "wine" or "wine64")
            .Where(path => Path.GetFileName(Path.GetDirectoryName(path)) == "bin")
            .OrderBy(path => Path.GetFileName(path) == "wine" ? 0 : 1)
            .ThenBy(path => path.Length)
            .FirstOrDefault();
    }

    private static async Task DownloadAsync(HttpClient httpClient, WineBuild build, string archive, StatusServer status, CancellationToken cancellationToken)
    {
        var partial = archive + ".part";

        using var response = await httpClient.GetAsync(build.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        long done = 0;

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = File.Create(partial))
        {
            var buffer = new byte[BufferSize];
            int read;

            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                done += read;
                status.Set("downloading", $"Downloading the Free Realms Plugin game runtime... {done / 1048576} MB of {Math.Max(1, total / 1048576)} MB", total > 0 ? (double)done / total : 0);
            }
        }

        if (!await HasHashAsync(partial, build.Sha256, cancellationToken))
        {
            File.Delete(partial);
            throw new InvalidOperationException("The game runtime download was damaged. Please try again.");
        }

        File.Move(partial, archive, true);
    }

    private static async Task<bool> HasHashAsync(string file, string expected, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(file);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), expected, StringComparison.OrdinalIgnoreCase);
    }
}
