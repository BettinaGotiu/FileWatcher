using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

class Program
{
    private static Dictionary<string, FileMetadata> previousSnapshot = new();
    private static string monitoredPath = "";
    private const int PollingIntervalSeconds = 10;
    private static CancellationTokenSource cts = new();

    static void Main(string[] args)
    {
        Console.CancelKeyPress += (s, e) =>
        {
            Console.WriteLine("\n Exit requested. Shutting down...");
            cts.Cancel();
            e.Cancel = true;
        };

        // Load path from config
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();
        monitoredPath = config["WatchSettings:NfsPath"];

        if (!Directory.Exists(monitoredPath))
        {
            Console.WriteLine("❌ Specified NFS path does not exist or is inaccessible.");
            return;
        }

        Console.WriteLine($"🟢 Watching NFS folder: {monitoredPath}");
        Console.WriteLine("📅 Polling every 10 seconds. Press Ctrl+C or Q to exit.\n");

        // Initial snapshot
        previousSnapshot = TakeSnapshot();

        Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
                {
                    cts.Cancel();
                    break;
                }

                Thread.Sleep(PollingIntervalSeconds * 1000);

                if (!Directory.Exists(monitoredPath))
                {
                    Console.WriteLine("❌ NFS path is not accessible (maybe VM is down). Retrying...");
                    continue;
                }

                ForceDirectoryRefresh();

                var currentSnapshot = TakeSnapshot();
                CompareSnapshots(previousSnapshot, currentSnapshot);
                previousSnapshot = currentSnapshot;
            }
        }).Wait();

        Console.WriteLine("✅ App exited cleanly.");
    }

    static Dictionary<string, FileMetadata> TakeSnapshot()
    {
        var snapshot = new Dictionary<string, FileMetadata>(100_000);

        try
        {
            var files = Directory.EnumerateFiles(monitoredPath, "*", SearchOption.AllDirectories);

            Parallel.ForEach(files, file =>
            {
                try
                {
                    var info = new FileInfo(file);
                    var metadata = new FileMetadata
                    {
                        Path = file,
                        Size = info.Length,
                        LastWriteTime = info.LastWriteTimeUtc,
                        CreationTime = info.CreationTimeUtc
                    };

                    lock (snapshot)
                    {
                        snapshot[file] = metadata;
                    }
                }
                catch
                {
                    // Skip unreadable files
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Error taking snapshot: {ex.Message}");
        }

        return snapshot;
    }

    static void CompareSnapshots(Dictionary<string, FileMetadata> oldSnap, Dictionary<string, FileMetadata> newSnap)
    {
        var oldFiles = new HashSet<string>(oldSnap.Keys);
        var newFiles = new HashSet<string>(newSnap.Keys);

        foreach (var file in newFiles)
        {
            if (!oldFiles.Contains(file))
            {
                Console.WriteLine($"🆕 Created: {file}");
            }
        }

        foreach (var file in oldFiles)
        {
            if (!newFiles.Contains(file))
            {
                Console.WriteLine($"❌ Deleted: {file}");
            }
        }

        foreach (var file in newFiles)
        {
            if (oldSnap.ContainsKey(file))
            {
                var oldMeta = oldSnap[file];
                var newMeta = newSnap[file];

                if (oldMeta.Size != newMeta.Size || oldMeta.LastWriteTime != newMeta.LastWriteTime)
                {
                    Console.WriteLine($"✏️ Modified: {file}");
                }
            }
        }
    }

    static void ForceDirectoryRefresh()
    {
        try
        {
            var dir = new DirectoryInfo(monitoredPath);
            dir.Refresh();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Could not refresh directory: {ex.Message}");
        }
    }
}

class FileMetadata
{
 public string Path { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastWriteTime { get; set; }
    public DateTime CreationTime { get; set; }
}