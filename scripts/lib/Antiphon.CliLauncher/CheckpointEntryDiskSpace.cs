using System;
using System.Collections.Generic;
using System.IO;

namespace Antiphon.CliLauncher
{
    internal static class CheckpointEntryDiskSpace
    {
        public static bool Refuse(IReadOnlyList<string> args, string checkout, CliPublishOptions publish)
        {
            if (args.Count < 2 || args[0] != "checkpoints" || args[1] is not ("row" or "run" or "start"))
                return false;

            var measure = publish.DiskFreeBytes ?? AvailableBytes;
            foreach (var path in new[] { checkout, Environment.CurrentDirectory, Path.GetTempPath() })
            {
                if (measure(path) is not long free || free >= 20L << 30)
                    continue;
                publish.Error.WriteLine($"checkpoints: workspace_disk_low: {path} has {free} bytes available. Free at least 20 GiB before retrying.");
                return true;
            }
            return false;
        }

        private static long? AvailableBytes(string path)
        {
            try
            {
                return new DriveInfo(OperatingSystem.IsWindows() ? Path.GetPathRoot(path) : path).AvailableFreeSpace;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }
        }
    }
}
