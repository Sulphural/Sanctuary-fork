using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.Database;

namespace Sanctuary.WebAPI.Website;

public sealed class BackupOptions
{
    public const string Section = "Backup";

    public string? Folder { get; set; }
    public int KeepCount { get; set; } = 14;
    public int IntervalHours { get; set; } = 24;
}

public sealed record BackupFile(string Name, long Size, DateTime Created);

public sealed class DatabaseBackupService : BackgroundService
{
    private const string Prefix = "sanctuary-";
    private const string Extension = ".db";

    private readonly ILogger<DatabaseBackupService> _logger;
    private readonly BackupOptions _options;
    private readonly DatabaseOptions _databaseOptions;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DatabaseBackupService(ILogger<DatabaseBackupService> logger, IOptions<BackupOptions> options, IOptions<DatabaseOptions> databaseOptions)
    {
        _logger = logger;
        _options = options.Value;
        _databaseOptions = databaseOptions.Value;
    }

    public bool Enabled => _databaseOptions.Provider == DatabaseProvider.Sqlite && !string.IsNullOrWhiteSpace(_options.Folder);

    private string Folder => Path.GetFullPath(_options.Folder!);

    public IReadOnlyList<BackupFile> List()
    {
        if (!Enabled || !Directory.Exists(Folder))
            return [];

        return new DirectoryInfo(Folder)
            .GetFiles(Prefix + "*" + Extension)
            .OrderByDescending(x => x.CreationTimeUtc)
            .Select(x => new BackupFile(x.Name, x.Length, x.CreationTimeUtc))
            .ToList();
    }

    public async Task<BackupFile?> BackupNowAsync(CancellationToken cancellationToken)
    {
        if (!Enabled)
            return null;

        await _lock.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(Folder);

            var name = Prefix + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + Extension;
            var target = Path.Combine(Folder, name);
            var partial = target + ".partial";

            var source = new SqliteConnectionStringBuilder(_databaseOptions.ConnectionString) { Mode = SqliteOpenMode.ReadOnly }.ToString();

            await using (var sourceConnection = new SqliteConnection(source))
            await using (var targetConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = partial, Pooling = false }.ToString()))
            {
                await sourceConnection.OpenAsync(cancellationToken);
                await targetConnection.OpenAsync(cancellationToken);
                sourceConnection.BackupDatabase(targetConnection);
            }

            SqliteConnection.ClearAllPools();
            File.Move(partial, target, true);

            foreach (var old in List().Skip(Math.Max(1, _options.KeepCount)))
                File.Delete(Path.Combine(Folder, old.Name));

            var info = new FileInfo(target);

            _logger.LogInformation("Database backed up to {Backup} ({Size} bytes).", target, info.Length);

            return new BackupFile(info.Name, info.Length, info.CreationTimeUtc);
        }
        finally
        {
            _lock.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
            return;

        var interval = TimeSpan.FromHours(Math.Max(1, _options.IntervalHours));

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var latest = List().FirstOrDefault();
                var due = latest is null ? TimeSpan.Zero : interval - (DateTime.UtcNow - latest.Created);

                if (due > TimeSpan.Zero)
                {
                    await Task.Delay(due, stoppingToken);
                    continue;
                }

                try
                {
                    await BackupNowAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Database backup failed.");
                    await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
