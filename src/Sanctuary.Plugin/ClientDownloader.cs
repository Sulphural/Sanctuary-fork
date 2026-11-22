using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Sanctuary.Plugin;

public static class ClientDownloader
{
    public const string ClientExecutable = "FreeRealms.exe";

    private const int DownloadWorkers = 8;
    private const int BufferSize = 1024 * 1024;
    private const string HtmlContentType = "text/html";

    private static readonly string[] LauncherClientPath = ["OSFRLauncher", "Servers", "OSFR Public Server", "Client"];

    public static async Task<string> EnsureAsync(HttpClient httpClient, PluginPaths paths, PluginSettings settings, PluginConfig config, StatusServer status, CancellationToken cancellationToken)
    {
        if (settings.ClientPath is { Length: > 0 } custom && HasClient(custom))
            return custom;

        var launcherClient = Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), .. LauncherClientPath]);

        if (!HasClient(paths.ClientDirectory) && HasClient(launcherClient))
            return launcherClient;

        List<ManifestFile> files;

        try
        {
            status.Set("checking", "Checking for game updates...");
            files = await ReadManifestAsync(httpClient, config.ClientManifestUrl, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && HasClient(paths.ClientDirectory))
        {
            paths.Log($"Update check failed, using the installed client: {ex.Message}");
            return paths.ClientDirectory;
        }

        var missing = files.Where(file => !IsComplete(paths.ClientDirectory, file)).ToList();

        if (missing.Count == 0)
            return paths.ClientDirectory;

        var total = missing.Sum(file => file.Size);
        long done = 0;
        var firstInstall = !HasClient(paths.ClientDirectory);
        var verb = firstInstall ? "Downloading Open Source Free Realms" : "Updating Open Source Free Realms";

        status.Set("downloading", $"{verb}...", 0);

        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = DownloadWorkers, CancellationToken = cancellationToken }, async (file, token) =>
        {
            await DownloadFileAsync(httpClient, config.ClientBaseUrl, paths.ClientDirectory, file, bytes =>
            {
                var current = Interlocked.Add(ref done, bytes);
                status.Set("downloading", $"{verb}... {FormatSize(current)} of {FormatSize(total)}", (double)current / Math.Max(1, total));
            }, token);
        });

        return paths.ClientDirectory;
    }

    public static bool HasClient(string folder) => File.Exists(Path.Combine(folder, ClientExecutable));

    private static bool IsComplete(string root, ManifestFile file)
    {
        var path = Path.Combine([root, .. file.Path]);
        return File.Exists(path) && new FileInfo(path).Length == file.Size;
    }

    private static async Task<List<ManifestFile>> ReadManifestAsync(HttpClient httpClient, string manifestUrl, CancellationToken cancellationToken)
    {
        var manifest = XDocument.Parse(await httpClient.GetStringAsync(manifestUrl, cancellationToken));
        var files = new List<ManifestFile>();

        Visit(manifest.Root!, [], files);

        return files;
    }

    private static void Visit(XElement element, string[] parent, List<ManifestFile> files)
    {
        foreach (var child in element.Elements())
        {
            var name = child.Attribute("name")?.Value;

            if (child.Name == "Folder")
            {
                Visit(child, string.IsNullOrEmpty(name) ? parent : [.. parent, name], files);
            }
            else if (child.Name == "File" && name is not null)
            {
                if (Path.IsPathRooted(name) || name.Split('/', '\\').Contains("..") || parent.Contains(".."))
                    throw new InvalidDataException($"Unsafe path in client manifest: {name}");

                files.Add(new ManifestFile([.. parent, name], long.Parse(child.Attribute("size")!.Value)));
            }
        }
    }

    private static async Task DownloadFileAsync(HttpClient httpClient, string baseUrl, string root, ManifestFile file, Action<long> progress, CancellationToken cancellationToken)
    {
        var destination = Path.Combine([root, .. file.Path]);
        var partial = destination + ".part";
        var url = $"{baseUrl}/{string.Join('/', file.Path.Select(Uri.EscapeDataString))}";

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long written = 0;

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = File.Create(partial))
        {
            var buffer = new byte[BufferSize];
            int read;

            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                var keep = (int)Math.Min(read, Math.Max(0, file.Size - written));
                await output.WriteAsync(buffer.AsMemory(0, keep), cancellationToken);
                written += read;
                progress(keep);
            }
        }

        var edgeInjectedHtml = response.Content.Headers.ContentType?.MediaType == HtmlContentType && written >= file.Size;

        if (written != file.Size && !edgeInjectedHtml)
        {
            File.Delete(partial);
            throw new IOException($"Download of {string.Join('/', file.Path)} was incomplete.");
        }

        File.Move(partial, destination, true);
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }

    private sealed record ManifestFile(string[] Path, long Size);
}
