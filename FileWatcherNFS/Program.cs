using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

class Program
{
    private static Dictionary<string, EntryMetadata> previousSnapshot = new();
    private static Dictionary<string, List<string>> previousFolderToFiles = new();
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
            Console.WriteLine(" Specified NFS path does not exist or is inaccessible.");
            return;
        }

        Console.WriteLine($" Watching NFS folder: {monitoredPath}");
        Console.WriteLine(" Polling every 10 seconds. Press Ctrl+C or Q to exit.\n");

        // Initial snapshot
        (previousSnapshot, previousFolderToFiles) = TakeSnapshot();

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
                    Console.WriteLine(" NFS path is not accessible (maybe VM is down). Retrying...");
                    continue;
                }

                ForceDirectoryRefresh();

                var (currentSnapshot, currentFolderToFiles) = TakeSnapshot();
                CompareSnapshots(previousSnapshot, currentSnapshot, previousFolderToFiles);
                previousSnapshot = currentSnapshot;
                previousFolderToFiles = currentFolderToFiles;
            }
        }).Wait();

        Console.WriteLine(" App exited cleanly.");
    }

    static (Dictionary<string, EntryMetadata>, Dictionary<string, List<string>>) TakeSnapshot()
    {
        var snapshot = new Dictionary<string, EntryMetadata>(100_000);
        var folderToFiles = new Dictionary<string, List<string>>();

        try
        {
            // Folders
            var dirs = Directory.EnumerateDirectories(monitoredPath, "*", SearchOption.AllDirectories);
            foreach (var dir in dirs)
            {
                try
                {
                    var info = new DirectoryInfo(dir);
                    snapshot[dir] = new EntryMetadata
                    {
                        Path = dir,
                        Type = EntryType.Directory,
                        LastWriteTime = info.LastWriteTimeUtc,
                        CreationTime = info.CreationTimeUtc
                    };
                }
                catch { }
            }

            // Files
            var files = Directory.EnumerateFiles(monitoredPath, "*", SearchOption.AllDirectories);
            Parallel.ForEach(files, file =>
            {
                try
                {
                    var info = new FileInfo(file);
                    var meta = new EntryMetadata
                    {
                        Path = file,
                        Type = EntryType.File,
                        Size = info.Length,
                        LastWriteTime = info.LastWriteTimeUtc,
                        CreationTime = info.CreationTimeUtc
                    };

                    lock (snapshot)
                    {
                        snapshot[file] = meta;
                    }

                    var folder = Path.GetDirectoryName(file) ?? "";
                    lock (folderToFiles)
                    {
                        if (!folderToFiles.ContainsKey(folder))
                            folderToFiles[folder] = new List<string>();
                        folderToFiles[folder].Add(file);
                    }
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($" Error taking snapshot: {ex.Message}");
        }

        return (snapshot, folderToFiles);
    }



    static void CompareSnapshots(
    Dictionary<string, EntryMetadata> oldSnap,
    Dictionary<string, EntryMetadata> newSnap,
    Dictionary<string, List<string>> oldFolderToFiles)
    {
        var oldPaths = new HashSet<string>(oldSnap.Keys);
        var newPaths = new HashSet<string>(newSnap.Keys);

        var implicitlyDeletedFiles = new HashSet<string>();

        // Created
        foreach (var path in newPaths)
        {
            if (!oldPaths.Contains(path))
            {
                var type = newSnap[path].Type;
                if (type == EntryType.File)
                    Console.WriteLine($" Created file: {path}");
                else
                    Console.WriteLine($" Created folder: {path}");
            }
        }

        // Deleted folders (and the folders inside)
        var deletedFolders = oldPaths
            .Where(p => !newPaths.Contains(p) && oldSnap[p].Type == EntryType.Directory)
            .OrderBy(p => p.Length)
            .ToList();

        var topLevelDeleted = new List<string>();

        foreach (var folder in deletedFolders)
        {
            if (!topLevelDeleted.Any(parent => folder.StartsWith(parent + Path.DirectorySeparatorChar)))
            {
                topLevelDeleted.Add(folder);
            }
        }

        // only top-level folders
        foreach (var path in topLevelDeleted)
        {
            Console.WriteLine($" Deleted folder: {path}");

            if (oldFolderToFiles.TryGetValue(path, out var files))
            {
                foreach (var file in files)
                    implicitlyDeletedFiles.Add(file);
            }
        }


        // Deleted files (if they are not inside a deleted folder)
        foreach (var path in oldPaths)
        {
            if (!newPaths.Contains(path) &&
                oldSnap[path].Type == EntryType.File &&
                !implicitlyDeletedFiles.Contains(path))
            {
                Console.WriteLine($" Deleted file: {path}");
            }
        }

        // Modified
        foreach (var path in newPaths)
        {
            if (oldSnap.ContainsKey(path))
            {
                var oldMeta = oldSnap[path];
                var newMeta = newSnap[path];

                if (oldMeta.Type == EntryType.File && newMeta.Type == EntryType.File)
                {
                    if (oldMeta.Size != newMeta.Size || oldMeta.LastWriteTime != newMeta.LastWriteTime)
                    {
                        Console.WriteLine($" Modified file: {path}");
                    }
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
            Console.WriteLine($" Could not refresh directory: {ex.Message}");
        }
    }
}

enum EntryType
{
    File,
    Directory
}

class EntryMetadata
{
    public string Path { get; set; } = "";
    public EntryType Type { get; set; }
    public long? Size { get; set; } // null for directory
    public DateTime LastWriteTime { get; set; }
    public DateTime CreationTime { get; set; }
}
