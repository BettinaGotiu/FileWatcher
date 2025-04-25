using System;
using System.IO;
using Microsoft.Extensions.Configuration;

class Program
{
    static void Main(string[] args)
    {
        // Load configuration from appsettings.json
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        var path = config["WatchSettings:NfsPath"];

        if (!Directory.Exists(path))
        {
            Console.WriteLine("The specified path does not exist or is not accessible.");
            return;
        }

        using var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName |
                           NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite |
                           NotifyFilters.CreationTime
        };

        watcher.Created += (s, e) =>
            Console.WriteLine($"Created: {e.FullPath}");
        watcher.Changed += (s, e) =>
            Console.WriteLine($"Changed: {e.FullPath}");
        watcher.Deleted += (s, e) =>
            Console.WriteLine($"Deleted: {e.FullPath}");
        watcher.Renamed += (s, e) =>
            Console.WriteLine($"Renamed: {e.OldFullPath} → {e.FullPath}");

        Console.WriteLine($"Watching folder: {path}");
        Console.WriteLine("Press [Enter] to exit.");
        Console.ReadLine();
    }
}
