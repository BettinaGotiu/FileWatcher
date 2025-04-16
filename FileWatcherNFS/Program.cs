using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration;

class Program
{
    private static Dictionary<string, FileMetadata> previousSnapshot = new();
    private static string monitoredPath = "";
    private const int PollingIntervalSeconds = 10;

    static void Main(string[] args)
    {
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
        Console.WriteLine("Polling every 10 seconds...\n");

        // Initial snapshot
        previousSnapshot = TakeSnapshot();

        while (true)
        {
            Thread.Sleep(PollingIntervalSeconds * 1000);

            ForceDirectoryRefresh(); // Refresh NFS Folder For Windows

            var currentSnapshot = TakeSnapshot();
            CompareSnapshots(previousSnapshot, currentSnapshot);
            previousSnapshot = currentSnapshot; // Update snapshot
        }
    }

    static Dictionary<string, FileMetadata> TakeSnapshot()
    {
        var snapshot = new Dictionary<string, FileMetadata>();

        try
        {
            var files = Directory.GetFiles(monitoredPath, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var info = new FileInfo(file);
                snapshot[file] = new FileMetadata
                {
                    Path = file,
                    Size = info.Length,
                    LastWriteTime = info.LastWriteTimeUtc,
                    CreationTime = info.CreationTimeUtc
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Error while taking snapshot: {ex.Message}");
        }

        return snapshot;
    }

    static void CompareSnapshots(Dictionary<string, FileMetadata> oldSnap, Dictionary<string, FileMetadata> newSnap)
    {
        var oldFiles = new HashSet<string>(oldSnap.Keys);
        var newFiles = new HashSet<string>(newSnap.Keys);

        // Created files
        foreach (var file in newFiles)
        {
            if (!oldFiles.Contains(file))
            {
                Console.WriteLine($"🆕 Created: {file}");
            }
        }

        // Deleted files
        foreach (var file in oldFiles)
        {
            if (!newFiles.Contains(file))
            {
                Console.WriteLine($"❌ Deleted: {file}");
            }
        }

        // Modified files
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

    // Refresh Function For NFS Folder
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

// Lightweight metadata struct
class FileMetadata
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastWriteTime { get; set; }
    public DateTime CreationTime { get; set; }
}
